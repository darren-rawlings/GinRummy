using System.Collections.Generic;
using Renegade.Ui;

namespace GameTemplate.Game;

/// <summary>
/// The guided tour behind the Demo button: a few short pages, each with a looping clip recorded
/// from the game itself (see Main.PlayClip and addons/renegade_core/tools/Record-Clips.ps1).
/// Text only, no voice. A page without a clip (Video = null) just shows its text.
/// </summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Knock five times",
			"Tap the board to knock on it. Five knocks finish a game, and the board catches fire. Replace this page with the real game's rules.",
			"res://tutorial/knock.ogv"),
		new TutorialPage("Tips",
			"Drag sideways to spin the board, or freeze it in Settings."),
	};
}
