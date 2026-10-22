using System;
using System.Collections.Generic;
using System.Linq;
using GinRummy.Core;
using Renegade.Cards;
using Renegade.Core;
using Xunit;

namespace GinRummy.Tests;

public class MeldTests
{
	private static Card C(int rank, Suit suit) => new(rank, suit);

	[Fact]
	public void SetsAndRunsAreMeldsAndAcesAreLow()
	{
		Assert.True(Gin.IsSet(new[] { C(7, Suit.Clubs), C(7, Suit.Hearts), C(7, Suit.Spades) }));
		Assert.True(Gin.IsRun(new[] { C(1, Suit.Hearts), C(2, Suit.Hearts), C(3, Suit.Hearts) }));
		Assert.False(Gin.IsRun(new[] { C(12, Suit.Hearts), C(13, Suit.Hearts), C(1, Suit.Hearts) }));
		Assert.False(Gin.IsRun(new[] { C(4, Suit.Hearts), C(5, Suit.Clubs), C(6, Suit.Hearts) }));
		Assert.Equal(10, Gin.Points(C(Card.King, Suit.Spades)));
		Assert.Equal(1, Gin.Points(C(Card.Ace, Suit.Spades)));
	}

	[Fact]
	public void TheBestArrangementPicksTheMeldsThatLeaveLeastDeadwood()
	{
		// 7♥ belongs either to the sevens or to the 5-6-7♥ run; the best uses it where it saves most.
		var hand = new[]
		{
			C(7, Suit.Hearts), C(7, Suit.Clubs), C(7, Suit.Spades),
			C(5, Suit.Hearts), C(6, Suit.Hearts),
			C(Card.King, Suit.Clubs), C(Card.King, Suit.Diamonds), C(Card.King, Suit.Spades),
			C(2, Suit.Clubs), C(Card.Queen, Suit.Diamonds),
		};
		var best = Gin.Best(hand);
		Assert.Equal(2 + 10 + 5 + 6, best.DeadwoodPoints);
		var other = Gin.Best(hand.Where(c => c != C(7, Suit.Clubs)).Append(C(4, Suit.Hearts)).ToList());
		// With 4♥ instead of 7♣: the run 4-5-6-7♥ and the kings; deadwood 7♠ + 2 + 10.
		Assert.Equal(7 + 2 + 10, other.DeadwoodPoints);
	}

	[Fact]
	public void DeadwoodLaysOffOnTheKnockersMelds()
	{
		var melds = new List<List<Card>>
		{
			new() { C(4, Suit.Spades), C(5, Suit.Spades), C(6, Suit.Spades) },
			new() { C(9, Suit.Clubs), C(9, Suit.Hearts), C(9, Suit.Diamonds) },
		};
		var dead = new List<Card> { C(7, Suit.Spades), C(8, Suit.Spades), C(9, Suit.Spades), C(2, Suit.Hearts) };
		var laid = Gin.LayOff(melds, dead);
		Assert.Equal(3, laid.Count);
		Assert.Equal(new[] { C(2, Suit.Hearts) }, dead);
	}
}

public class HandTests
{
	[Fact]
	public void TenEachAnUpCardAndTheStock()
	{
		var hand = new GinHand(Deck.Shuffled(new Random(1)), dealer: 0);
		Assert.Equal(10, hand.Hand(0).Count);
		Assert.Equal(10, hand.Hand(1).Count);
		Assert.Single(hand.DiscardPile);
		Assert.Equal(31, hand.Stock.Count);
		Assert.Equal(1, hand.Current);
		Assert.Equal(GinPhase.Offer, hand.Phase);
	}

	[Fact]
	public void BothPassingTheUpCardLetsTheNonDealerDraw()
	{
		var hand = new GinHand(Deck.Shuffled(new Random(2)), dealer: 0);
		hand.Offer(take: false);
		Assert.Equal(0, hand.Current);
		hand.Offer(take: false);
		Assert.Equal(1, hand.Current);
		Assert.Equal(GinPhase.Draw, hand.Phase);
	}

	[Fact]
	public void YouCantThrowBackTheCardYouJustTook()
	{
		var hand = new GinHand(Deck.Shuffled(new Random(3)), dealer: 0);
		hand.Offer(take: true);
		var taken = hand.TakenFromPile!.Value;
		Assert.Throws<InvalidOperationException>(() => hand.Discard(taken));
		hand.Discard(hand.Hand(1).First(c => c != taken));
		Assert.Equal(0, hand.Current);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	public void ComputerHandsFinishAndScoreSensibly(int seed)
	{
		var random = new Random(seed);
		var match = new GinMatch();
		var ais = new[] { new GinAi(0, Difficulty.Medium, random), new GinAi(1, Difficulty.Hard, random) };
		int hands = 0;
		while (!match.IsOver && hands++ < 60)
		{
			var hand = new GinHand(Deck.Shuffled(random), match.NextDealer);
			int turns = 0;
			while (hand.Phase != GinPhase.Over && turns++ < 200)
			{
				var ai = ais[hand.Current];
				if (hand.Phase == GinPhase.Offer)
					hand.Offer(ai.WantsDiscard(hand));
				else if (hand.Phase == GinPhase.Draw)
				{
					if (ai.WantsDiscard(hand))
						hand.DrawDiscard();
					else
						hand.DrawStock();
				}
				else if (ai.KnockWith(hand) is { } knock)
					hand.Knock(knock);
				else
					hand.Discard(ai.ChooseDiscard(hand));
				int cards = hand.Hand(0).Count + hand.Hand(1).Count + hand.Stock.Count + hand.DiscardPile.Count;
				Assert.Equal(52, cards);
			}
			Assert.Equal(GinPhase.Over, hand.Phase);
			var result = hand.Result!;
			if (!result.Drawn)
			{
				Assert.True(result.Points > 0 || result.Undercut);
				Assert.True(result.KnockerHand.DeadwoodPoints <= GinHand.KnockLimit);
				if (result.Gin)
					Assert.Equal(0, result.KnockerHand.DeadwoodPoints);
			}
			match.Record(hand);
		}
		Assert.True(match.IsOver);
		Assert.True(match.Total(match.Winner) > match.Total(1 - match.Winner));
	}
}
