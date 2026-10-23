using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace GinRummy.Core;

/// <summary>How a hand splits into melds (sets and runs) and the deadwood left over.</summary>
public sealed record Arrangement(IReadOnlyList<IReadOnlyList<Card>> Melds, IReadOnlyList<Card> Deadwood)
{
	public int DeadwoodPoints => Deadwood.Sum(Gin.Points);
}

/// <summary>
/// The optional rules (the variants Gin Rummy Palace offers):
/// - <b>Oklahoma:</b> the bottom card of the stock is shown, and its value is the most deadwood you
///   may knock with (an ace, or a joker: gin only).
/// - <b>Cyclic runs:</b> the ace goes round the corner: Q-K-A and K-A-2 are runs.
/// - <b>Rainbow runs:</b> four cards of consecutive ranks, all different suits, alternating red and
///   black, are a meld too.
/// - <b>Tedesco:</b> when you knock, aces in your opponent's hand count 15; and after taking the
///   discard you may knock that turn only if the card you took is in one of your melds.
/// - <b>Boasting:</b> instead of knocking, announce you will within three turns; knock and win on
///   your first, second or third turn after for 5, 10 or 20 more; fail, and your opponent gets 15.
/// - <b>Jokers:</b> three jokers join the deck and stand for any card in a meld; in deadwood one
///   counts 15; knocking by discarding a joker doubles your opponent's deadwood; a deadwood card
///   that fits where the knocker's joker stands can be laid off on it.
/// - <b>Gin Pro:</b> winning the match also takes a number of hands won.
/// </summary>
public sealed record GinRules(bool Oklahoma = false, bool Cyclic = false, bool Rainbow = false, bool Tedesco = false,
	bool Boasting = false, bool Jokers = false, int HandsToWin = 0)
{
	public static readonly GinRules Standard = new();
}

/// <summary>
/// Melds in gin rummy: a set is three or four cards of one rank; a run is three or more cards of
/// one suit in sequence (ace low only: A-2-3, never Q-K-A, unless runs are cyclic). Jokers, when
/// in play, stand for any card. Deadwood counts aces 1, numbers at face value, and jacks, queens
/// and kings 10 (a joker 15).
/// </summary>
public static class Gin
{
	/// <summary>The three jokers of the jokers variant.</summary>
	public static readonly Card[] Jokers = { Card.Joker(big: true), Card.Joker(big: false), new(Card.LittleJoker, Suit.Hearts) };

	public static int Points(Card card) => card.IsJoker ? 15 : Math.Min(card.Rank, 10);

	/// <summary>The deck for a deal under these rules, shuffled: 52 cards, or 55 with the jokers.</summary>
	public static List<Card> Pack(GinRules rules, Random random)
	{
		var cards = Deck.Standard();
		if (rules.Jokers)
			cards.AddRange(Jokers);
		Deck.Shuffle(cards, random);
		return cards;
	}

	public static bool IsSet(IReadOnlyCollection<Card> cards, GinRules? rules = null)
	{
		rules ??= GinRules.Standard;
		var naturals = cards.Where(c => !c.IsJoker).ToList();
		if (naturals.Count < cards.Count && !rules.Jokers)
			return false;
		return cards.Count is 3 or 4 && naturals.Count >= 1 && naturals.Select(c => c.Rank).Distinct().Count() == 1;
	}

	public static bool IsRun(IReadOnlyCollection<Card> cards, GinRules? rules = null)
	{
		rules ??= GinRules.Standard;
		var naturals = cards.Where(c => !c.IsJoker).ToList();
		if (naturals.Count < cards.Count && !rules.Jokers)
			return false;
		if (cards.Count < 3 || cards.Count > 13 || naturals.Count == 0 || naturals.Select(c => c.Suit).Distinct().Count() != 1)
			return false;
		var ranks = naturals.Select(c => c.Rank).OrderBy(r => r).ToList();
		if (ranks.Distinct().Count() != ranks.Count)
			return false;
		// The naturals must fit within a stretch of consecutive ranks as long as the meld.
		int span = ranks[^1] - ranks[0] + 1;
		if (rules.Cyclic)
		{
			// Round the corner: the shortest arc of the 13 ranks that covers them all.
			int widestGap = 0;
			for (int i = 0; i < ranks.Count; i++)
			{
				int next = i + 1 < ranks.Count ? ranks[i + 1] : ranks[0] + 13;
				widestGap = Math.Max(widestGap, next - ranks[i] - 1);
			}
			span = Math.Min(span, 13 - widestGap);
		}
		return span <= cards.Count;
	}

	/// <summary>A rainbow run: four consecutive ranks, every suit different, alternating red and black.</summary>
	public static bool IsRainbow(IReadOnlyCollection<Card> cards, GinRules? rules = null)
	{
		rules ??= GinRules.Standard;
		if (!rules.Rainbow || cards.Count != 4 || cards.Any(c => c.IsJoker) || cards.Select(c => c.Suit).Distinct().Count() != 4)
			return false;
		var sorted = cards.OrderBy(c => c.Rank).ToList();
		bool consecutive = sorted.Zip(sorted.Skip(1)).All(p => p.Second.Rank == p.First.Rank + 1);
		if (!consecutive && rules.Cyclic)
		{
			// Q-K-A-2 and the like: try each card as the start of the sequence round the corner.
			for (int start = 0; start < 4 && !consecutive; start++)
			{
				var turned = sorted.Skip(start).Concat(sorted.Take(start)).ToList();
				if (turned.Zip(turned.Skip(1)).All(p => (p.First.Rank % 13) + 1 == p.Second.Rank))
				{
					sorted = turned;
					consecutive = true;
				}
			}
		}
		return consecutive && sorted.Zip(sorted.Skip(1)).All(p => p.First.IsRed != p.Second.IsRed);
	}

	public static bool IsMeld(IReadOnlyCollection<Card> cards, GinRules? rules = null) =>
		IsSet(cards, rules) || IsRun(cards, rules) || IsRainbow(cards, rules);

	/// <summary>Every meld that can be made from some of these cards (jokers filling in, under those rules).</summary>
	public static List<List<Card>> Candidates(IReadOnlyList<Card> hand, GinRules? rules = null)
	{
		rules ??= GinRules.Standard;
		var naturals = hand.Where(c => !c.IsJoker).ToList();
		var jokers = rules.Jokers ? hand.Where(c => c.IsJoker).ToList() : new List<Card>();
		var found = new List<List<Card>>();

		// Every choice of k jokers from those held.
		IEnumerable<List<Card>> JokerChoices(int k)
		{
			if (k == 0)
			{
				yield return new List<Card>();
				yield break;
			}
			for (int mask = 1; mask < 1 << jokers.Count; mask++)
				if (System.Numerics.BitOperations.PopCount((uint)mask) == k)
					yield return jokers.Where((_, i) => (mask & (1 << i)) != 0).ToList();
		}

		// Sets: some of one rank (any non-empty part of the group), with jokers making it three or four.
		foreach (var group in naturals.GroupBy(c => c.Rank))
		{
			var cards = group.ToList();
			for (int mask = 1; mask < 1 << cards.Count; mask++)
			{
				var part = cards.Where((_, i) => (mask & (1 << i)) != 0).ToList();
				for (int k = 0; k <= jokers.Count; k++)
					if (part.Count + k is 3 or 4)
						foreach (var extra in JokerChoices(k))
							found.Add(part.Concat(extra).ToList());
			}
		}

		// Runs: every stretch of consecutive ranks in a suit, jokers filling what's missing.
		foreach (var suit in naturals.GroupBy(c => c.Suit))
		{
			var byRank = suit.ToDictionary(c => c.Rank);
			int starts = rules.Cyclic ? 13 : 11;
			for (int start = 1; start <= starts; start++)
				for (int length = 3; length <= 13; length++)
				{
					if (!rules.Cyclic && start + length - 1 > 13)
						break;
					var present = new List<Card>();
					for (int i = 0; i < length; i++)
					{
						int rank = (start - 1 + i) % 13 + 1;
						if (byRank.TryGetValue(rank, out var card))
							present.Add(card);
					}
					int missing = length - present.Count;
					if (present.Count == 0 || missing > jokers.Count)
						continue;
					// A stretch that's only a shorter run with jokers tacked on both ends is still fine.
					foreach (var extra in JokerChoices(missing))
						found.Add(present.Concat(extra).ToList());
				}
		}

		// Rainbow runs: one card of each of four consecutive ranks, suits all different, colours alternating.
		if (rules.Rainbow)
			for (int start = 1; start <= (rules.Cyclic ? 13 : 10); start++)
			{
				var ranks = Enumerable.Range(0, 4).Select(i => (start - 1 + i) % 13 + 1).ToList();
				var options = ranks.Select(r => naturals.Where(c => c.Rank == r).ToList()).ToList();
				if (options.Any(o => o.Count == 0))
					continue;
				foreach (var a in options[0])
					foreach (var b in options[1])
						foreach (var c in options[2])
							foreach (var d in options[3])
							{
								var run = new List<Card> { a, b, c, d };
								if (IsRainbow(run, rules))
									found.Add(run);
							}
			}
		return found;
	}

	/// <summary>The arrangement with the least deadwood (ties: the fewest cards in deadwood).</summary>
	public static Arrangement Best(IReadOnlyList<Card> hand, GinRules? rules = null)
	{
		var candidates = Candidates(hand, rules);
		var best = new Arrangement(Array.Empty<IReadOnlyList<Card>>(), hand.ToList());
		int bestPoints = best.DeadwoodPoints;
		var chosen = new List<List<Card>>();
		var used = new HashSet<Card>();

		void Search(int from)
		{
			var dead = hand.Where(c => !used.Contains(c)).ToList();
			int points = dead.Sum(Points);
			if (points < bestPoints || (points == bestPoints && dead.Count < best.Deadwood.Count))
			{
				bestPoints = points;
				best = new Arrangement(chosen.Select(m => (IReadOnlyList<Card>)m.ToList()).ToList(), dead);
			}
			if (points == 0)
				return;
			for (int i = from; i < candidates.Count; i++)
			{
				var meld = candidates[i];
				if (meld.Any(used.Contains))
					continue;
				chosen.Add(meld);
				foreach (var card in meld)
					used.Add(card);
				Search(i + 1);
				foreach (var card in meld)
					used.Remove(card);
				chosen.RemoveAt(chosen.Count - 1);
			}
		}
		Search(0);
		return best;
	}

	/// <summary>
	/// Lays the defender's deadwood off onto the knocker's melds wherever it fits (a set of three
	/// takes its fourth card; a run takes the next card at either end; with jokers, a card that
	/// fits where a joker stands goes on it), as many times as it can. Returns the cards laid off,
	/// and where each went.
	/// </summary>
	public static List<(Card Card, int Meld)> LayOff(List<List<Card>> knockerMelds, List<Card> deadwood, GinRules? rules = null)
	{
		rules ??= GinRules.Standard;
		var laid = new List<(Card, int)>();
		bool moved = true;
		while (moved)
		{
			moved = false;
			foreach (var card in deadwood.ToList())
				for (int m = 0; m < knockerMelds.Count; m++)
				{
					var meld = knockerMelds[m];
					var bigger = meld.Append(card).ToList();
					bool fits = (IsSet(meld, rules) && IsSet(bigger, rules)) || (IsRun(meld, rules) && IsRun(bigger, rules));
					// Onto a joker: the card takes the joker's place (the joker stays out of play).
					var joker = meld.FirstOrDefault(c => c.IsJoker);
					bool onJoker = !fits && !card.IsJoker && joker.IsJoker && IsMeld(meld.Where(c => c != joker).Append(card).ToList(), rules);
					if (fits || onJoker)
					{
						if (onJoker)
							meld.Remove(joker);
						meld.Add(card);
						deadwood.Remove(card);
						laid.Add((card, m));
						moved = true;
						break;
					}
				}
		}
		return laid;
	}
}
