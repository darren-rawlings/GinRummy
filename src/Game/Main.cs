using GameTemplate.Core;
using Godot;
using Renegade.Ads;
using Renegade.Audio;
using Renegade.Core;
using Renegade.Dev;
using Renegade.Looks;
using Renegade.Play;
using Renegade.Ui;

namespace GameTemplate.Game;

/// <summary>
/// Starting point for a new game, built the way every Renegade game is: the shared stage (lights,
/// reflection sky, table), a board on a rig that spins with a drag, the shared HUD (modes,
/// difficulty, Style menu, Undo (ad), Start over, Settings with sound and freeze, a video
/// tutorial behind Demo), material sounds, ads every 5th game, the winner's fire and the flight
/// recorder. The placeholder game: tap the board to knock it, five knocks finish a game.
/// Replace the board, the rules (src/Core), the tap, the tutorial pages and the clips with the
/// real game. Tutorial clips: give each a case in PlayClip, then run
/// addons/renegade_core/tools/Record-Clips.ps1 with their names.
/// </summary>
public partial class Main : Node3D
{
	public const string Title = "Game Template";

	/// <summary>Google's test ad units until the real ones exist in AdMob (see the release playbook).</summary>
	private static readonly AdMobConfig AdUnits = AdMobConfig.Test;

	private const float Thickness = 0.4f;

	private readonly ImpactBank<Style> _impacts = new(Styles.Impact);
	private readonly Scoreboard _wins = new();

	private GameSettings _settings = null!;
	private Counter _game = new(5);
	private Node3D _rig = null!;
	private Camera3D _camera = null!;
	private MeshInstance3D _board = null!;
	private SoundPool _sound = null!;
	private GameHud _hud = null!;
	private IAdService _ads = null!;
	private AdSchedule? _adSchedule;
	private SpinControl _spin = null!;
	private Flames? _fire;

	private bool _demo;
	private bool _quitAfterDemo;
	private string? _clip;
	private float _demoTimer;

	public override void _Ready()
	{
		DevLog.Tag = "gt";
		AudioWatch.Attach(this, Title);
		_settings = GameSettings.Load();
		ReadDeveloperArgs();
		AudioServer.SetBusMute(0, _settings.Muted);

		_camera = new Camera3D { Position = new Vector3(0, 10f, 6.5f), Fov = 52, KeepAspect = Camera3D.KeepAspectEnum.Width };
		AddChild(_camera);
		_camera.LookAt(new Vector3(0, -0.4f, 0.3f));
		Stage.Build(this, -Thickness, new Color("1a0f09"));
		_rig = new Node3D();
		AddChild(_rig);
		_board = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(7, Thickness, 7) },
			Position = new Vector3(0, -Thickness / 2, 0),
			MaterialOverride = Materials.For(Styles.BoardLook(_settings.Board)),
		};
		_rig.AddChild(_board);
		Stage.WarmUp(this, -Thickness, Sparkle.Halo);

		_sound = new SoundPool();
		AddChild(_sound);
		_adSchedule ??= new AdSchedule(_settings.GamesSinceAd);
		var theme = HudTheme.Warm;
		_ads = AdServices.Create(this, AdUnits, _demo, new PlaceholderAds { TextColor = theme.Text });

		_hud = new GameHud(theme);
		AddChild(_hud);
		// The HUD saves Sound and Freeze the board itself; add a game's own Settings rows with
		// _hud.AddToggle and _hud.AddChoice (keep their values in _settings.Extra).
		_hud.Build(new HudOptions(Title, "Tap the board  ·  Drag sideways to spin"), _settings);
		_hud.NewGamePressed += OnNewGamePressed;
		_hud.RestartPressed += OnRestartPressed;
		_hud.SetTutorial(Tutorial.Pages);
		_hud.ModeChosen += (mode, difficulty) =>
		{
			_settings.Mode = mode;
			_settings.Difficulty = difficulty;
			_settings.Save();
		};
		_hud.BoardChosen += style =>
		{
			_settings.Board = style;
			_settings.Save();
			_board.MaterialOverride = Materials.For(Styles.BoardLook(style));
		};
		_hud.PiecesChosen += style =>
		{
			_settings.Pieces = style;
			_settings.Save();
		};

		_spin = new SpinControl(_rig, () => !_settings.FreezeBoard);
		// Freeze the board also turns it back to where it started.
		_spin.Frozen = () => _settings.FreezeBoard;
		_spin.Tapped += OnTapped;
		RefreshUi();
		if (_clip != null)
		{
			_hud.Visible = false;
			_ = PlayClip(_clip);
			return;
		}
		if (!_demo)
			_hud.OfferTutorialOnce(_settings);
		_hud.ApplyDevFlags();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_hud.MenuOpen)
			_spin.HandleInput(@event);
	}

	/// <summary>A tap on the board: a knock in the board's material, sparks on the last one.</summary>
	private void OnTapped(Vector2 screen)
	{
		if (_game.IsDone)
			return;
		var from = _camera.ProjectRayOrigin(screen);
		var direction = _camera.ProjectRayNormal(screen);
		if (direction.Y > -0.01f)
			return;
		var hit = from + direction * (-from.Y / direction.Y);
		var local = _rig.ToLocal(hit);
		if (Mathf.Abs(local.X) > 3.5f || Mathf.Abs(local.Z) > 3.5f)
			return;
		_hud.HideHint();
		_game.Tap();
		_sound.PlayImpact(_impacts.Random(_settings.Board), hit, 1f, Styles.GainDb(_settings.Board));
		if (_game.IsDone)
		{
			_wins.Win(first: true);
			Sparkle.Burst(_rig, local + new Vector3(0, 0.3f, 0));
			// The winner's fire: in a real game, set the winning pieces alight.
			_fire = Flames.SetAlight(_rig, local, new[] { _board }, radius: 0.6f, size: 1.4f, withLight: true);
		}
		RefreshUi();
	}

	private void OnNewGamePressed()
	{
		bool adDue = _adSchedule!.GameFinished();
		_settings.GamesSinceAd = _adSchedule.GamesSinceAd;
		_settings.Save();
		if (adDue)
		{
			DevLog.Write("ad: between-games interstitial");
			_ads.ShowInterstitial(StartGame);
		}
		else
		{
			StartGame();
		}
	}

	private void StartGame()
	{
		_fire?.Extinguish();
		_fire = null;
		_board.MaterialOverlay = null;
		_game = new Counter(5);
		RefreshUi();
	}

	/// <summary>
	/// Start over mid-game. Against the computer it counts as resigning (a loss) unless the player
	/// watches an ad; between two players it just asks first.
	/// </summary>
	private void OnRestartPressed()
	{
		if (_game.IsDone || _game.Count == 0)
		{
			StartGame();
			return;
		}
		_hud.AskRestart(_settings.Mode == GameMode.PlayerVsComputer && !_demo, countLoss =>
		{
			if (countLoss)
				_wins.Win(first: false);
			StartGame();
		}, then => _ads.ShowRewarded(rewarded: then, closed: () => { }));
	}

	/// <summary>
	/// Plays one scripted scene for the tutorial (with --clip=name), then quits; the HUD is hidden
	/// so the clip shows only the board. Record them with addons/renegade_core/tools/Record-Clips.ps1.
	/// </summary>
	private async System.Threading.Tasks.Task PlayClip(string clip)
	{
		_settings.Muted = false;
		await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
		switch (clip)
		{
			default:
				for (int i = 0; i < 5; i++)
				{
					OnTapped(GetViewport().GetVisibleRect().Size / 2);
					await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
				}
				break;
		}
		await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
		GetTree().Quit();
	}

	private void RefreshUi()
	{
		_hud.SetScore($"Games {_wins.First}");
		_hud.SetNewGameVisible(_game.IsDone);
		_hud.SetRestartVisible(!_game.IsDone);
		_hud.SetStatus(_game.IsDone ? "Done!" : $"Knock {_game.Count} of {_game.Target}", new Color("fff3e0"));
		if (_game.IsDone && _demo && _quitAfterDemo && _wins.First >= 5)
			GetTree().Quit();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_spin.Process(dt);
		if (!_demo)
			return;
		_demoTimer += dt;
		if (_demoTimer < 0.3f)
			return;
		_demoTimer = 0f;
		if (_game.IsDone)
			OnNewGamePressed();
		else
			OnTapped(GetViewport().GetVisibleRect().Size / 2);
	}

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo`,
	/// `--quit-after-demo`, `--games-since-ad=4`, `--clip=knock` (plays a tutorial clip and quits),
	/// plus the HUD's `--tutorial=N` and `--settings`. Add the game's own flags here.
	/// </summary>
	private void ReadDeveloperArgs()
	{
		foreach (var (name, value) in DevArgs.Read())
		{
			switch (name)
			{
				case "--log":
					DevLog.Enabled = true;
					break;
				case "--demo":
					_demo = true;
					_settings.Muted = false;
					break;
				case "--quit-after-demo":
					_quitAfterDemo = true;
					break;
				case "--games-since-ad":
					_adSchedule = new AdSchedule(int.Parse(value));
					break;
				case "--clip":
					_clip = value;
					break;
			}
		}
	}
}
