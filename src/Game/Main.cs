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

	protected override string Title => "Gin Rummy";
	protected override string Hint => "Draw, then tap a card to discard it";
	protected override bool SortYourHand => false;
	protected override string NewGameText => _match.IsOver ? "New match" : "Next hand";
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => !_match.IsOver && (_match.Hands > 0 || _hand is { Phase: not GinPhase.Over });

	private Vector3 StockAt => Table.Centre + new Vector3(-0.45f, 0, 0.05f);
	private Vector3 DiscardAt => Table.Centre + new Vector3(0.45f, 0, 0.05f);

	protected override void LevelChanged(Difficulty difficulty) => _ai = new GinAi(1, difficulty, Random);

	/// <summary>The points a match is played to (Settings › Play to).</summary>
	private int Target => Settings.Extra.TryGetValue("target", out var t) && int.TryParse(t, out int n) && n > 0 ? n : 100;

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
			if (!Busy && _hand is { Phase: GinPhase.Discard, Current: 0 } hand && hand.BestDiscard(0) is { Deadwood: <= GinHand.KnockLimit } best)
				Run(Knock(0, best.Discard));
		};
	}

	protected override async Task NewRound()
	{
		int round = Round;
		_pass.Visible = _knock.Visible = false;
		if (_match.IsOver || _match.Hands == 0)
			_match = new GinMatch(Target);
		_ai = new GinAi(1, Settings.Difficulty, Random);
		_nodes.Clear();
		_hand = null;
		SetScore();
		var order = Deck.Shuffled(Random);
		var nodes = await ShuffleOnTable(order);
		if (round != Round)
			return;
		for (int i = 0; i < order.Count; i++)
			_nodes[order[i]] = nodes[i];
		var hand = new GinHand(order, _match.NextDealer);
		_hand = hand;

		// Ten each, from the non-dealer; then the up-card beside the stock.
		await DealRound(nodes.Take(20).ToList(), Enumerable.Range(0, 20).Select(i => (1 - hand.Dealer + i) % 2).ToList(), spacing: 0.1f);
		if (round != Round)
			return;
		var stock = hand.Stock.Select(c => _nodes[c]).ToList();
		Tween? last = null;
		for (int i = 0; i < stock.Count; i++)
			last = CardMotion.Slide(stock[i], Dealer.InStack(StockAt, stock.Count - 1 - i), 0.2f);
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
			return dead == 0 ? "You have gin!" : dead <= GinHand.KnockLimit ? $"You can knock with {dead}." : $"Deadwood {hand.Best(0).DeadwoodPoints}.";
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
		if (hand.Current == 1 || Demo)
		{
			Busy = true;
			_pass.Visible = _knock.Visible = false;
			await Pause(0.8f);
			if (round != Round)
				return;
			await ComputerMove(hand.Current);
			return;
		}
		Busy = false;
		_pass.Visible = hand.Phase == GinPhase.Offer;
		_knock.Visible = false;
		switch (hand.Phase)
		{
			case GinPhase.Offer:
				Hud.SetStatus($"Take the up-card (tap it), or pass. {DeadwoodNote()}", Bright);
				break;
			case GinPhase.Draw:
				Hud.SetStatus($"Your turn: tap the stock or the discard pile. {DeadwoodNote()}", Bright);
				break;
			case GinPhase.Discard:
				var (dead, _) = hand.BestDiscard(0);
				_knock.Visible = dead <= GinHand.KnockLimit;
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
			Hud.SetStatus(p == 0 ? "You take the up-card." : "Rosie takes the up-card.", Muted);
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
			Say(p, p == 1 ? "Pass." : null);
			Hud.SetStatus(p == 0 ? "You pass." : "Rosie passes.", Muted);
			hand.Offer(take: false);
			await Pause(0.6f);
			SayNone();
		}
		await Next();
	}

	private async Task DrawCard(int p, bool fromPile)
	{
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
		Hud.SetStatus(fromPile ? (p == 0 ? "You take the discard." : $"Rosie takes the {Describe(card)}.") : (p == 0 ? "You draw from the stock." : "Rosie draws from the stock."), Muted);
		await IntoFan(p, new[] { _nodes[card] });
		if (p == 0)
			Arrange();
		await Next();
	}

	private async Task Throw(int p, Card card)
	{
		var hand = _hand!;
		Busy = true;
		_knock.Visible = false;
		hand.Discard(card);
		var target = FaceUpAt(DiscardAt + Vector3.Up * (0.004f * hand.DiscardPile.Count), ((float)Random.NextDouble() - 0.5f) * 0.3f);
		await PlayToTable(p, _nodes[card], target);
		if (p == 0)
			Arrange();
		Hud.SetStatus(p == 0 ? $"You discard the {Describe(card)}." : $"Rosie discards the {Describe(card)}.", Muted);
		await Next();
	}

	private async Task Knock(int p, Card discard)
	{
		var hand = _hand!;
		Busy = true;
		_knock.Visible = false;
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
		_pass.Visible = _knock.Visible = false;
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
		Sparkle.Burst(this, (result.Winner == 0 ? Table.Centre + new Vector3(0, 0.2f, 0.9f) : Table.Centre + new Vector3(0, 0.2f, -1.0f)));
		SetScore();
		if (_match.IsOver)
		{
			int w = _match.Winner;
			Finish($"{how} {(w == 0 ? "You win the match" : "Rosie wins the match")}, {_match.Total(w)} to {_match.Total(1 - w)}!", w,
				hand.Hand(w).Select(c => _nodes[c]), w == 0 ? Table.Centre + new Vector3(0, 0, 0.9f) : Table.Centre + new Vector3(0, 0, -1f));
		}
		else
		{
			Finish(how, -1);
		}
	}

	/// <summary>Where a card in a laid-out meld goes: a row in front of each player, the melds side by side.</summary>
	private Transform3D SpreadSlot(int p, int group, int index, int groups = 4)
	{
		float z = p == 0 ? 0.95f : -1.15f;
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
