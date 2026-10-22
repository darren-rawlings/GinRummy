using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace GinRummy.Core;

public enum GinPhase
{
	/// <summary>The first turn: the non-dealer, then the dealer, may take the up-card or pass.</summary>
	Offer,
	/// <summary>Draw from the stock or take the top discard.</summary>
	Draw,
	/// <summary>Discard a card (or knock, discarding face down).</summary>
	Discard,
	Over,
}

/// <summary>How a hand ended and what it scored.</summary>
public sealed record GinResult(
	int Knocker, bool Gin, bool Undercut, int Winner, int Points,
	Arrangement KnockerHand, Arrangement DefenderHand, IReadOnlyList<(Card Card, int Meld)> LaidOff, bool Drawn)
{
	public static GinResult DrawnHand => new(-1, false, false, -1, 0,
		new Arrangement(Array.Empty<IReadOnlyList<Card>>(), Array.Empty<Card>()),
		new Arrangement(Array.Empty<IReadOnlyList<Card>>(), Array.Empty<Card>()), Array.Empty<(Card, int)>(), true);
}

/// <summary>
/// One hand of gin rummy for two (the Bicycle rules). Ten cards each; the next card is turned up
/// to start the discard pile and the rest is the stock. On the first turn the non-dealer may
/// take the up-card or pass, then the dealer; if both pass, the non-dealer draws from the stock.
/// Each turn: draw (stock or top discard), then discard (not the card just taken from the pile).
/// With 10 or less deadwood after discarding you may knock; with none, it's gin. The defender
/// then lays off deadwood on the knocker's melds (not after gin). The knocker scores the
/// difference in deadwood; gin adds 25 and the defender lays nothing off; if the defender's
/// deadwood is equal or less, it's an undercut: the defender scores the difference plus 20. If
/// only two cards are left in the stock, the hand is a draw.
/// </summary>
public sealed class GinHand
{
	public const int HandSize = 10, KnockLimit = 10, GinBonus = 25, UndercutBonus = 20;

	private readonly List<Card>[] _hands = { new(), new() };
	private readonly List<Card> _stock;
	private readonly List<Card> _discard = new();
	private int _passes;
	private Card? _takenFromPile;

	/// <summary>Deals from a deck (top card first): ten each, alternately, starting with the non-dealer.</summary>
	public GinHand(IReadOnlyList<Card> deck, int dealer)
	{
		if (deck.Count != 52 || deck.Distinct().Count() != 52)
			throw new ArgumentException("Gin rummy needs a full deck.", nameof(deck));
		Dealer = dealer;
		int first = 1 - dealer;
		Dealt = deck.Take(2 * HandSize).ToList();
		for (int i = 0; i < Dealt.Count; i++)
			_hands[(first + i) % 2].Add(Dealt[i]);
		UpCard = deck[2 * HandSize];
		_discard.Add(UpCard);
		_stock = deck.Skip(2 * HandSize + 1).ToList();
		Current = first;
	}

	public int Dealer { get; }
	public IReadOnlyList<Card> Dealt { get; }
	public Card UpCard { get; }
	public int Current { get; private set; }
	public GinPhase Phase { get; private set; } = GinPhase.Offer;
	public GinResult? Result { get; private set; }

	public IReadOnlyList<Card> Hand(int player) => _hands[player];

	/// <summary>The stock, top card first.</summary>
	public IReadOnlyList<Card> Stock => _stock;

	/// <summary>The discard pile, bottom first (the last is the top, which can be taken).</summary>
	public IReadOnlyList<Card> DiscardPile => _discard;

	public Card? TopDiscard => _discard.Count > 0 ? _discard[^1] : null;

	/// <summary>The card the current player just took from the discard pile (it can't go straight back).</summary>
	public Card? TakenFromPile => _takenFromPile;

	public Arrangement Best(int player) => Gin.Best(_hands[player]);

	/// <summary>The least deadwood a player could have after discarding one card (in the Discard phase).</summary>
	public (int Deadwood, Card Discard) BestDiscard(int player)
	{
		var options = _hands[player].Where(c => c != _takenFromPile).ToList();
		return options.Select(c => (Gin.Best(_hands[player].Where(x => x != c).ToList()).DeadwoodPoints, c))
			.OrderBy(o => o.Item1).ThenByDescending(o => Gin.Points(o.c)).First();
	}

	/// <summary>Whether the current player may knock by discarding this card.</summary>
	public bool CanKnock(Card discard) =>
		Phase == GinPhase.Discard && discard != _takenFromPile && _hands[Current].Contains(discard)
		&& Gin.Best(_hands[Current].Where(c => c != discard).ToList()).DeadwoodPoints <= KnockLimit;

	/// <summary>The first turn: take the up-card (then discard), or pass it on.</summary>
	public void Offer(bool take)
	{
		Need(GinPhase.Offer);
		if (take)
		{
			TakeDiscard();
			return;
		}
		_passes++;
		if (_passes == 1)
		{
			Current = Dealer;
		}
		else
		{
			// Both passed: the non-dealer starts by drawing from the stock.
			Current = 1 - Dealer;
			Phase = GinPhase.Draw;
		}
	}

	public Card DrawStock()
	{
		Need(GinPhase.Draw);
		var card = _stock[0];
		_stock.RemoveAt(0);
		_hands[Current].Add(card);
		_takenFromPile = null;
		Phase = GinPhase.Discard;
		return card;
	}

	public Card DrawDiscard()
	{
		Need(GinPhase.Draw);
		return TakeDiscard();
	}

	private Card TakeDiscard()
	{
		var card = _discard[^1];
		_discard.RemoveAt(_discard.Count - 1);
		_hands[Current].Add(card);
		_takenFromPile = card;
		Phase = GinPhase.Discard;
		return card;
	}

	/// <summary>Discards a card; the turn passes (or, with two cards left in the stock, the hand is drawn).</summary>
	public void Discard(Card card)
	{
		Need(GinPhase.Discard);
		if (card == _takenFromPile)
			throw new InvalidOperationException("You can't discard the card you just took from the pile.");
		if (!_hands[Current].Remove(card))
			throw new InvalidOperationException("That card isn't in your hand.");
		_discard.Add(card);
		_takenFromPile = null;
		if (_stock.Count <= 2)
		{
			Phase = GinPhase.Over;
			Result = GinResult.DrawnHand;
			return;
		}
		Current = 1 - Current;
		Phase = GinPhase.Draw;
	}

	/// <summary>Knocks (or goes gin), discarding a card face down, and scores the hand.</summary>
	public GinResult Knock(Card discard)
	{
		if (!CanKnock(discard))
			throw new InvalidOperationException("You can't knock with that.");
		_hands[Current].Remove(discard);
		_discard.Add(discard);
		int knocker = Current, defender = 1 - knocker;
		var knock = Gin.Best(_hands[knocker]);
		var defence = Gin.Best(_hands[defender]);
		bool gin = knock.DeadwoodPoints == 0;
		var laid = new List<(Card, int)>();
		var defenderDeadwood = defence.Deadwood.ToList();
		if (!gin)
		{
			var melds = knock.Melds.Select(m => m.ToList()).ToList();
			laid = Gin.LayOff(melds, defenderDeadwood);
		}
		int knockPoints = knock.DeadwoodPoints;
		int defencePoints = defenderDeadwood.Sum(Gin.Points);
		GinResult result;
		if (gin)
			result = new GinResult(knocker, true, false, knocker, defencePoints + GinBonus, knock, defence, laid, false);
		else if (defencePoints <= knockPoints)
			result = new GinResult(knocker, false, true, defender, knockPoints - defencePoints + UndercutBonus, knock,
				defence with { Deadwood = defenderDeadwood }, laid, false);
		else
			result = new GinResult(knocker, false, false, knocker, defencePoints - knockPoints, knock,
				defence with { Deadwood = defenderDeadwood }, laid, false);
		Phase = GinPhase.Over;
		Result = result;
		return result;
	}

	private void Need(GinPhase phase)
	{
		if (Phase != phase)
			throw new InvalidOperationException($"Not now: the hand is at {Phase}.");
	}
}

/// <summary>
/// A match, scored as Gin Rummy Palace does: each hand's winner scores its points plus a box bonus
/// of 10, and the first to reach the target (100 unless chosen otherwise) wins. No game bonus.
/// </summary>
public sealed class GinMatch
{
	public const int BoxBonus = 10;

	public GinMatch(int target = 100) => Target = target;

	public int Target { get; }

	public int[] Score { get; } = new int[2];
	public int[] Boxes { get; } = new int[2];
	public int Hands { get; private set; }
	public int NextDealer { get; private set; } = 1;
	public bool IsOver => Score.Max() >= Target;
	public int Winner => !IsOver ? -1 : Score[0] >= Target ? 0 : 1;

	/// <summary>Books a finished hand. The winner of a hand deals the next (after a drawn hand, the same dealer deals again).</summary>
	public void Record(GinHand hand)
	{
		var result = hand.Result ?? throw new InvalidOperationException("The hand isn't over.");
		Hands++;
		if (result.Drawn)
			return;
		Score[result.Winner] += result.Points + BoxBonus;
		Boxes[result.Winner]++;
		NextDealer = result.Winner;
	}

	/// <summary>A player's total (the box bonuses are in the score already).</summary>
	public int Total(int player) => Score[player];
}
