using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;
using Renegade.Core;

namespace GinRummy.Core;

/// <summary>
/// The computer's play. Easy takes the up-card only when it completes a meld, discards its
/// highest loose card and knocks the moment it can. Medium takes a discard whenever it cuts its
/// deadwood, and knocks at 10 or less. Hard also remembers what you pick up and pass on, so it
/// avoids feeding you cards that fit your melds, and holds out for gin while the stock is deep.
/// </summary>
public sealed class GinAi
{
	private readonly Random _random;
	private readonly HashSet<Card> _theyTook = new();
	private readonly HashSet<Card> _theyRefused = new();

	public GinAi(int player, Difficulty difficulty, Random random)
	{
		Player = player;
		Difficulty = difficulty;
		_random = random;
	}

	public int Player { get; }
	public Difficulty Difficulty { get; }

	/// <summary>Notes the other player taking a card from the discard pile.</summary>
	public void TheyTook(Card card) => _theyTook.Add(card);

	/// <summary>Notes the other player passing a card by (drawing from the stock instead, or passing the up-card).</summary>
	public void TheyRefused(Card card) => _theyRefused.Add(card);

	private int DeadwoodWith(IReadOnlyList<Card> hand, Card add) =>
		hand.Append(add).Select(drop => Gin.Best(hand.Append(add).Where(c => c != drop).ToList()).DeadwoodPoints).Min();

	/// <summary>Whether to take the top discard (or the up-card on the first turn) rather than draw from the stock.</summary>
	public bool WantsDiscard(GinHand hand)
	{
		var top = hand.TopDiscard;
		if (top is not { } card)
			return false;
		var mine = hand.Hand(Player);
		int now = Gin.Best(mine).DeadwoodPoints;
		if (Difficulty == Difficulty.Easy)
			return Gin.Best(mine.Append(card).ToList()).Melds.Any(m => m.Contains(card));
		int with = DeadwoodWith(mine, card);
		// Take it if it really helps (a meld, or a clear cut in deadwood).
		return Gin.Best(mine.Append(card).ToList()).Melds.Any(m => m.Contains(card)) || with <= now - 4;
	}

	/// <summary>Which card to throw, in the Discard phase.</summary>
	public Card ChooseDiscard(GinHand hand)
	{
		var mine = hand.Hand(Player);
		var options = mine.Where(c => c != hand.TakenFromPile).ToList();
		var scored = options.Select(c => (Card: c, Dead: Gin.Best(mine.Where(x => x != c).ToList()).DeadwoodPoints, Danger: Danger(c))).ToList();
		if (Difficulty == Difficulty.Easy)
			return scored.OrderBy(s => s.Dead).ThenByDescending(s => Gin.Points(s.Card)).First().Card;
		int best = scored.Min(s => s.Dead);
		// Among the discards that keep deadwood near its lowest, avoid feeding the opponent.
		var near = scored.Where(s => s.Dead <= best + (Difficulty == Difficulty.Hard ? 2 : 0)).ToList();
		return near.OrderBy(s => Difficulty == Difficulty.Hard ? s.Danger : 0).ThenBy(s => s.Dead).ThenByDescending(s => Gin.Points(s.Card)).First().Card;
	}

	/// <summary>How likely a card is to help the opponent, from what they've taken and passed.</summary>
	private int Danger(Card card)
	{
		int danger = 0;
		foreach (var taken in _theyTook)
		{
			if (taken.Rank == card.Rank)
				danger += 3;
			if (taken.Suit == card.Suit && Math.Abs(taken.Rank - card.Rank) <= 2)
				danger += 2;
		}
		foreach (var refused in _theyRefused)
			if (refused.Rank == card.Rank)
				danger -= 1;
		return danger;
	}

	/// <summary>Whether to knock now (in the Discard phase), and with which card.</summary>
	public Card? KnockWith(GinHand hand)
	{
		var (dead, discard) = hand.BestDiscard(Player);
		if (dead > GinHand.KnockLimit)
			return null;
		if (dead == 0 || Difficulty == Difficulty.Easy)
			return discard;
		if (Difficulty == Difficulty.Medium)
			return dead <= 7 || hand.Stock.Count < 14 ? discard : null;
		// Hard: knock low early, and hold out for gin only while the stock is deep and the hand close.
		return dead <= 4 || hand.Stock.Count < 18 || dead <= 8 && hand.Stock.Count < 24 ? discard : null;
	}
}
