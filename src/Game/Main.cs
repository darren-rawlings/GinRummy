using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GinRummy.Core;
using Godot;
using Renegade.Cards;
using Renegade.Core;
using Renegade.Play;
using Renegade.Ui;

namespace GinRummy.Game;

/// <summary>
/// Gin rummy against Rosie, a match to 100 points (or as chosen). Your hand is kept arranged with its melds
/// together and the deadwood at the end, with your deadwood count shown. On your turn tap the
/// stock or the top of the discard pile to draw, then tap a card in your hand to discard it, or
/// press Knock (or Gin!) when you can. At the end of each hand both hands are laid out face up.
/// </summary>
public partial class Main : CardGame
{
	private readonly Dictionary<Card, Card3D> _nodes = new();
	private GinMatch _match = new();
	private GinHand? _hand;
	private GinAi _ai = null!;
	private Button _pass = null!;
	private Button _knock = null!;
	private Button _boast = null!;

	protected override string Title => "Gin Rummy";
	protected override string Hint => "Draw, then tap a card to discard it";
	protected override bool SortYourHand => false;
	protected override string NewGameText => _match.IsOver ? "New match" : "Next hand";
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => !_match.IsOver && (_match.Hands > 0 || _hand is { Phase: not GinPhase.Over });

	private Vector3 StockAt => Table.Centre + new Vector3(-0.45f, 0, 0.05f);
	private Vector3 DiscardAt => Table.Centre + new Vector3(0.45f, 0, 0.05f);

	protected override void LevelChanged(Difficulty difficulty) => _ai = new GinAi(1, difficulty, Random);

	/// <summary>The optional rules chosen in Settings.</summary>
	private GinRules Rules => new(Flag("okla"), Flag("cyclic"), Flag("rainbow"), Flag("tedesco"), Flag("boast"), Flag("jokers"), HandsToWin);

	/// <summary>Gin Pro: hands a winner must also have won (0 for none).</summary>
	private int HandsToWin => Settings.Extra.TryGetValue("gpro", out var h) && int.TryParse(h, out int n) ? n : 0;

	/// <summary>The points a match is played to (Settings › Play to).</summary>
	private int Target => Settings.Extra.TryGetValue("target", out var t) && int.TryParse(t, out int n) && n > 0 ? n : 100;

	/// <summary>`--variants=okla,jokers,boast` (any of okla, cyclic, rainbow, tedesco, boast, jokers) for checks.</summary>
	protected override void DevArg(string name, string value)
	{
		if (name == "--variants")
			foreach (var key in value.Split(','))
				Settings.Extra[key.Trim()] = "on";
	}

	protected override void AddSettings()
	{
		var targets = new[] { 50, 100, 150, 250 };
		Hud.AddChoice("Play to", targets.Select(t => t.ToString()).ToArray(), System.Math.Max(0, System.Array.IndexOf(targets, Target)), choice =>
		{
			Settings.Extra["target"] = targets[choice].ToString();
			Settings.Save();
			if (!InProgress)
				Run(StartRound());
		});
		// The variants Gin Rummy Palace offers; each takes effect from the next deal.
		void Variant(string label, string key)
		{
			Hud.AddToggle(label, Flag(key), on =>
			{
				SetFlag(key, on);
				if (!InProgress)
					Run(StartRound());
			});
		}
		Variant("Oklahoma (bottom card sets the knock)", "okla");
		Variant("Cyclic runs (Q-K-A, K-A-2)", "cyclic");
		Variant("Rainbow runs", "rainbow");
		Variant("Tedesco", "tedesco");
		Variant("Boasting", "boast");
		Variant("Jokers (three, wild)", "jokers");
		var pro = new[] { 0, 3, 5 };
		Hud.AddChoice("Gin Pro: hands to win", new[] { "Off", "3", "5" }, System.Math.Max(0, System.Array.IndexOf(pro, HandsToWin)), choice =>
		{
			Settings.Extra["gpro"] = pro[choice].ToString();
			Settings.Save();
			if (!InProgress)
				Run(StartRound());
		});
		_pass = Hud.AddButton("Pass", true, Control.LayoutPreset.CenterBottom, -150, 150, -192, -128, 28);
		_pass.Visible = false;
		_pass.Pressed += () =>
		{
			if (!Busy && _hand is { Phase: GinPhase.Offer, Current: 0 })
				Run(Offer(0, take: false));
		};
		_knock = Hud.AddButton("Knock", true, Control.LayoutPreset.CenterBottom, -150, 150, -192, -128, 28);
		_knock.Visible = false;
		_knock.Pressed += () =>
		{
			if (!Busy && _hand is { Phase: GinPhase.Discard, Current: 0 } hand && hand.BestDiscard(0) is var best && best.Deadwood <= hand.KnockLimit && hand.CanKnock(best.Discard))
				Run(Knock(0, best.Discard));
		};
		_boast = Hud.AddButton("Boast", false, Control.LayoutPreset.BottomRight, -200, -20, -192, -128, 24);
		_boast.Visible = false;
		_boast.Pressed += () =>
		{
			if (Busy || _hand is not { Phase: GinPhase.Discard, Current: 0, CanBoast: true } hand)
				return;
			hand.Boast();
			Say(0, "I'll knock within three turns!", 1);
			_boast.Visible = _knock.Visible = false;
		};
	}

	protected override async Task NewRound()
	{
		int round = Round;
		_pass.Visible = _knock.Visible = _boast.Visible = false;
		if (_match.IsOver || _match.Hands == 0)
			_match = new GinMatch(Target, HandsToWin);
		var rules = Rules;
		_ai = new GinAi(1, Settings.Difficulty, Random);
		_nodes.Clear();
		_hand = null;
		SetScore();
		var order = Gin.Pack(rules, Random);
		var nodes = await ShuffleOnTable(order);
		if (round != Round)
			return;
		for (int i = 0; i < order.Count; i++)
			_nodes[order[i]] = nodes[i];
		var hand = new GinHand(order, _match.NextDealer, rules);
		_hand = hand;

		// Ten each, from the non-dealer; then the up-card beside the stock.
		await DealRound(nodes.Take(20).ToList(), Enumerable.Range(0, 20).Select(i => (1 - hand.Dealer + i) % 2).ToList(), spacing: 0.1f);
		if (round != Round)
			return;
		var stock = hand.Stock.Select(c => _nodes[c]).ToList();
		Tween? last = null;
		for (int i = 0; i < stock.Count; i++)
			last = CardMotion.Slide(stock[i], Dealer.InStack(StockAt, stock.Count - 1 - i), 0.2f);
		// Oklahoma: the bottom card lies face up across the foot of the stock: it sets the knock.
		if (hand.OklahomaCard is { } bottom)
		{
			last = CardMotion.Slide(_nodes[bottom], FaceUpAt(StockAt + new Vector3(0, 0, 0.36f), Mathf.Pi / 2), 0.3f);
			News(hand.KnockLimit == 0 ? $"Oklahoma: the bottom card is the {bottom}, so this hand is gin only." : $"Oklahoma: the bottom card is the {bottom}: knock with {hand.KnockLimit} or less.");
		}
		await Await(last);
		await Await(CardMotion.Fly(_nodes[hand.UpCard], FaceUpAt(DiscardAt), 0.3f, lift: 0.2f, landed: () => Sounds.PlaySlap(DiscardAt)));
		await PickUpAll(p => hand.Hand(p).Select(c => _nodes[c]));
		Arrange();
		Hud.SetRestartVisible(true);
		await Next();
	}

	/// <summary>Keeps your fan in meld order: each meld together, the deadwood last.</summary>
	private void Arrange()
	{
		var best = _hand!.Best(0);
		var order = best.Melds.SelectMany(m => m.OrderBy(c => c.Rank)).Concat(best.Deadwood.OrderBy(c => c.Rank).ThenBy(c => c.Suit)).Select(c => _nodes[c]);
		Fans[0].Reorder(order);
		Fans[0].Settle(0.18f);
	}

	private string DeadwoodNote()
	{
		var hand = _hand!;
		if (hand.Phase == GinPhase.Discard && hand.Current == 0)
		{
			var (dead, _) = hand.BestDiscard(0);
			return dead == 0 ? "You have gin!" : dead <= hand.KnockLimit ? $"You can knock with {dead}." : $"Deadwood {hand.Best(0).DeadwoodPoints}.";
		}
		return $"Deadwood {hand.Best(0).DeadwoodPoints}.";
	}

	/// <summary>Hands over to whoever moves next: you get the buttons and taps, Rosie thinks and plays.</summary>
	private async Task Next()
	{
		var hand = _hand!;
		int round = Round;
		SetScore();
		if (hand.Phase == GinPhase.Over)
		{
			await EndHand();
			return;
		}
		ShowTurn(hand.Current);
		if (hand.Current == 1 || Demo)
		{
			if (hand.Current == 1)
				Hud.SetStatus("Rosie's turn.", Muted);
			Busy = true;
			_pass.Visible = _knock.Visible = _boast.Visible = false;
			await Pause(0.8f);
			if (round != Round)
				return;
			await ComputerMove(hand.Current);
			return;
		}
		Busy = false;
		_pass.Visible = hand.Phase == GinPhase.Offer;
		_knock.Visible = _boast.Visible = false;
		switch (hand.Phase)
		{
			case GinPhase.Offer:
				Hud.SetStatus($"Take the up-card (tap it), or pass. {DeadwoodNote()}", Bright);
				break;
			case GinPhase.Draw:
				Hud.SetStatus($"Your turn: tap the stock or the discard pile. {DeadwoodNote()}", Bright);
				break;
			case GinPhase.Discard:
				var (dead, discard) = hand.BestDiscard(0);
				_knock.Visible = dead <= hand.KnockLimit && hand.CanKnock(discard);
				_boast.Visible = hand.CanBoast && !_knock.Visible;
				_knock.Text = dead == 0 ? "Gin!" : "Knock";
				Hud.SetStatus($"Tap a card to discard it. {DeadwoodNote()}", Bright);
				break;
		}
	}

	private async Task ComputerMove(int p)
	{
		var hand = _hand!;
		var ai = p == 1 ? _ai : new GinAi(0, Renegade.Core.Difficulty.Medium, Random);
		switch (hand.Phase)
		{
			case GinPhase.Offer:
				await Offer(p, ai.WantsDiscard(hand));
				return;
			case GinPhase.Draw:
				await DrawCard(p, fromPile: ai.WantsDiscard(hand));
				return;
			case GinPhase.Discard:
				if (ai.WantsBoast(hand))
				{
					hand.Boast();
					Say(p, "I'll knock within three turns!", 1 - p);
					await Pause(0.8f);
				}
				if (ai.KnockWith(hand) is { } knock)
					await Knock(p, knock);
				else
					await Throw(p, ai.ChooseDiscard(hand));
				return;
		}
	}

	private async Task Offer(int p, bool take)
	{
		var hand = _hand!;
		Busy = true;
		_pass.Visible = false;
		if (take)
		{
			News(p == 0 ? "You take the up-card." : "Rosie takes the up-card.");
			var card = hand.TopDiscard!.Value;
			hand.Offer(take: true);
			if (p == 0)
				_ai.TheyTook(card);
			await IntoFan(p, new[] { _nodes[card] });
			if (p == 0)
				Arrange();
		}
		else
		{
			if (p == 0)
				_ai.TheyRefused(hand.TopDiscard!.Value);
			Say(p, "Pass.");
			hand.Offer(take: false);
			await Pause(0.6f);
			SayNone();
		}
		await Next();
	}

	private async Task DrawCard(int p, bool fromPile)
	{
		ClipMoment("draw", 6);
		var hand = _hand!;
		Busy = true;
		var top = hand.TopDiscard;
		var card = fromPile ? hand.DrawDiscard() : hand.DrawStock();
		if (p == 0)
		{
			if (fromPile)
				_ai.TheyTook(card);
			else if (top is { } passed)
				_ai.TheyRefused(passed);
		}
		News(fromPile ? (p == 0 ? "You take the discard." : $"Rosie takes the {Describe(card)}.") : (p == 0 ? "You draw from the stock." : "Rosie draws from the stock."));
		await IntoFan(p, new[] { _nodes[card] });
		if (p == 0)
			Arrange();
		await Next();
	}

	private async Task Throw(int p, Card card)
	{
		var hand = _hand!;
		Busy = true;
		_knock.Visible = _boast.Visible = false;
		hand.Discard(card);
		var target = FaceUpAt(DiscardAt + Vector3.Up * (0.004f * hand.DiscardPile.Count), ((float)Random.NextDouble() - 0.5f) * 0.3f);
		await PlayToTable(p, _nodes[card], target);
		if (p == 0)
			Arrange();
		News(p == 0 ? $"You discard the {Describe(card)}." : $"Rosie discards the {Describe(card)}.");
		await Next();
	}

	private async Task Knock(int p, Card discard)
	{
		ClipMoment("knock", 10);
		var hand = _hand!;
		Busy = true;
		_knock.Visible = _boast.Visible = false;
		var result = hand.Knock(discard);
		await PlayToTable(p, _nodes[discard], FaceDownAt(DiscardAt + Vector3.Up * (0.004f * hand.DiscardPile.Count), 0.2f));
		Say(p, result.Gin ? "Gin!" : "Knock!");
		await Pause(0.8f);
		await Next();
	}

	/// <summary>Both hands go down face up in their melds, the defender's lay-offs join the knocker's melds, and the hand is scored.</summary>
	private async Task EndHand()
	{
		var hand = _hand!;
		var result = hand.Result!;
		Busy = true;
		_pass.Visible = _knock.Visible = _boast.Visible = false;
		SayNone();
		_match.Record(hand);
		if (result.Drawn)
		{
			Hud.SetStatus("Only two cards left in the stock: this hand is a draw.", Bright);
			await Pause(0.5f);
			Finish("Only two cards left in the stock: this hand is a draw.", -1);
			SetScore();
			return;
		}
		int knocker = result.Knocker, defender = 1 - knocker;
		var knockMelds = result.KnockerHand.Melds.Select(m => m.ToList()).ToList();
		foreach (var (card, meld) in result.LaidOff)
			knockMelds[meld].Add(card);
		await Spread(knocker, knockMelds.Select(m => (IReadOnlyList<Card>)m.Where(c => !result.LaidOff.Any(l => l.Card == c)).ToList()).ToList(), result.KnockerHand.Deadwood);
		await Spread(defender, result.DefenderHand.Melds, result.DefenderHand.Deadwood);
		// Lay-offs: the defender's cards that fit the knocker's melds slide onto them.
		foreach (var (card, meld) in result.LaidOff)
		{
			var group = knockMelds[meld];
			int i = group.IndexOf(card);
			await Await(CardMotion.Fly(_nodes[card], SpreadSlot(knocker, meld, i), 0.3f, lift: 0.2f, landed: () => Sounds.PlaySlap(_nodes[card].GlobalPosition)));
		}
		string who = knocker == 0 ? "You" : "Rosie";
		string winner = result.Winner == 0 ? "you" : "Rosie";
		string how = result.Gin ? $"{who} went gin! +{result.Points} to {winner}."
			: result.Undercut ? $"Undercut! {(defender == 0 ? "You" : "Rosie")} had {result.DefenderHand.DeadwoodPoints - result.LaidOff.Sum(l => Gin.Points(l.Card))} or less: +{result.Points} to {winner}."
			: $"{who} knocked with {result.KnockerHand.DeadwoodPoints}: +{result.Points} to {winner}.";
		if (result.BonusTo >= 0)
			how += result.BonusTo == hand.Boaster ? $" Boast made: +{result.Bonus} more." : $" The boast failed: +{result.Bonus} to {(result.BonusTo == 0 ? "you" : "Rosie")}.";
		News(how);
		Sparkle.Burst(this, RowAt(result.Winner) + Vector3.Up * 0.2f);
		SetScore();
		if (_match.IsOver)
		{
			int w = _match.Winner;
			Finish($"{how} {(w == 0 ? "You win the match" : "Rosie wins the match")}, {_match.Total(w)} to {_match.Total(1 - w)}!", w,
				hand.Hand(w).Select(c => _nodes[c]), RowAt(w));
		}
		else
		{
			Finish(how, -1);
		}
	}

	/// <summary>
	/// The middle of the row each player lays their melds in: yours just above the top of your fan,
	/// where you can see it; Rosie's in front of her place setting.
	/// </summary>
	private Vector3 RowAt(int p) => p == 0 ? new Vector3(0, CardTable.FeltY, Table.YourPlayZ) : Table.Pile[1] + new Vector3(0, 0, 0.95f);

	/// <summary>Where a card in a laid-out meld goes: a row in front of each player, the melds side by side.</summary>
	private Transform3D SpreadSlot(int p, int group, int index, int groups = 4)
	{
		float z = RowAt(p).Z;
		float x = -1.6f + group * 0.95f + index * 0.17f;
		return FaceUpAt(new Vector3(x, CardTable.FeltY + 0.004f * (index + 1), z + (group % 2) * 0.05f));
	}

	private async Task Spread(int p, IReadOnlyList<IReadOnlyList<Card>> melds, IReadOnlyList<Card> deadwood)
	{
		var cards = new List<Card3D>();
		var targets = new List<Transform3D>();
		for (int m = 0; m < melds.Count; m++)
			for (int i = 0; i < melds[m].Count; i++)
			{
				cards.Add(_nodes[melds[m][i]]);
				targets.Add(SpreadSlot(p, m, i));
			}
		for (int i = 0; i < deadwood.Count; i++)
		{
			cards.Add(_nodes[deadwood[i]]);
			targets.Add(SpreadSlot(p, melds.Count, i));
		}
		foreach (var card in cards)
			Fans[p].Remove(card);
		Tween? last = null;
		for (int i = 0; i < cards.Count; i++)
		{
			var card = cards[i];
			last = CardMotion.Fly(card, targets[i], 0.4f, lift: 0.3f, delay: i * 0.05f, landed: i % 3 == 0 ? () => Sounds.PlaySlap(card.GlobalPosition) : null);
		}
		await Await(last);
	}

	private static string Describe(Card card) => $"{Card.RankName(card.Rank).ToLowerInvariant()} of {card.Suit.ToString().ToLowerInvariant()}";

	private void SetScore()
	{
		Hud.SetScore($"Match to {_match.Target}: you {_match.Score[0]} · {_match.Score[1]} Rosie   ·   Wins {Wins.First}–{Wins.Second}");
	}

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_hand is not { Current: 0 } hand)
			return;
		switch (hand.Phase)
		{
			case GinPhase.Offer:
				if (hand.TopDiscard is { } up && _nodes[up].Hit(origin, direction) != null)
					Run(Offer(0, take: true));
				break;
			case GinPhase.Draw:
				if (hand.TopDiscard is { } top && _nodes[top].Hit(origin, direction) != null)
					Run(DrawCard(0, fromPile: true));
				else if (HitCard(hand.Stock.Select(c => _nodes[c]), origin, direction) != null)
					Run(DrawCard(0, fromPile: false));
				break;
			case GinPhase.Discard:
				if (Fans[0].At(origin, direction) is { } card)
				{
					if (card.Card == hand.TakenFromPile)
					{
						Hud.SetStatus("You can't throw back the card you just took. Discard another.", Bright);
						return;
					}
					Hud.HideHint();
					Run(Throw(0, card.Card));
				}
				break;
		}
	}
}
