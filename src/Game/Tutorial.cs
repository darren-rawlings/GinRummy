using System.Collections.Generic;
using Renegade.Ui;

namespace GinRummy.Game;

/// <summary>The guided tour behind the Demo button: a few short pages.</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Melds",
			"Make melds: sets of three or four of a kind, or runs of three or more in one suit (aces are low). Cards that aren't in a meld are deadwood: aces count 1, picture cards 10. Your hand stays arranged with your melds together."),
		new TutorialPage("Your turn",
			"Draw by tapping the stock or the top of the discard pile, then tap a card in your hand to discard it. You can't throw back a card you just took from the pile."),
		new TutorialPage("Knock or go gin",
			"With 10 or less deadwood, press Knock: you score the difference between the deadwood counts, after Rosie lays her loose cards off on your melds. No deadwood at all is gin: 25 more, and no laying off. But if Rosie has as little as you, it's an undercut and she scores, plus 20."),
		new TutorialPage("The match",
			"Every hand won also scores a box bonus of 10. First to 100 wins the match (Settings › Play to changes the target)."),
		new TutorialPage("Variants",
			"Settings has the variants from Gin Rummy Palace: Oklahoma (the bottom card of the stock sets the most you may knock with; an ace means gin only), cyclic runs (Q-K-A), rainbow runs (four in a row of different suits, red and black in turn), Tedesco (aces count 15 against the defender), Boasting (promise to knock within three turns for a bonus), three wild jokers, and Gin Pro (a set number of hands to win as well as the points)."),
	};
}
