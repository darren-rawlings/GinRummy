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
/// Melds in gin rummy: a set is three or four cards of one rank; a run is three or more cards of
/// one suit in sequence (ace is low only: A-2-3, never Q-K-A). Deadwood counts aces 1, numbers at
/// face value, and jacks, queens and kings 10.
/// </summary>
public static class Gin
{
	public static int Points(Card card) => Math.Min(card.Rank, 10);

	public static bool IsSet(IReadOnlyCollection<Card> cards) => cards.Count is 3 or 4 && cards.Select(c => c.Rank).Distinct().Count() == 1;

	public static bool IsRun(IReadOnlyCollection<Card> cards)
	{
		if (cards.Count < 3 || cards.Select(c => c.Suit).Distinct().Count() != 1)
			return false;
		var ranks = cards.Select(c => c.Rank).OrderBy(r => r).ToList();
		return ranks.Zip(ranks.Skip(1)).All(p => p.Second == p.First + 1);
	}

	public static bool IsMeld(IReadOnlyCollection<Card> cards) => IsSet(cards) || IsRun(cards);

	/// <summary>Every meld that can be made from some of these cards.</summary>
	public static List<List<Card>> Candidates(IReadOnlyList<Card> hand)
	{
		var found = new List<List<Card>>();
		foreach (var group in hand.GroupBy(c => c.Rank).Where(g => g.Count() >= 3))
		{
			var cards = group.ToList();
			if (cards.Count == 4)
			{
				found.Add(cards);
				for (int skip = 0; skip < 4; skip++)
					found.Add(cards.Where((_, i) => i != skip).ToList());
			}
			else
			{
				found.Add(cards);
			}
		}
		foreach (var suit in hand.GroupBy(c => c.Suit))
		{
			var cards = suit.OrderBy(c => c.Rank).ToList();
			for (int start = 0; start < cards.Count; start++)
			{
				var run = new List<Card> { cards[start] };
				for (int next = start + 1; next < cards.Count && cards[next].Rank == run[^1].Rank + 1; next++)
				{
					run.Add(cards[next]);
					if (run.Count >= 3)
						found.Add(run.ToList());
				}
			}
		}
		return found;
	}

	/// <summary>The arrangement with the least deadwood (ties: the fewest cards in deadwood).</summary>
	public static Arrangement Best(IReadOnlyList<Card> hand)
	{
		var candidates = Candidates(hand);
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
	/// takes its fourth card; a run takes the next card at either end), as many times as it can.
	/// Returns the cards laid off, and where each went.
	/// </summary>
	public static List<(Card Card, int Meld)> LayOff(List<List<Card>> knockerMelds, List<Card> deadwood)
	{
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
					if ((IsSet(meld) && IsSet(bigger)) || (IsRun(meld) && IsRun(bigger)))
					{
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
