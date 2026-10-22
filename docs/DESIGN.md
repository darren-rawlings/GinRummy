# Gin Rummy: design

A Renegade card game on the card kit ([renegade-cards](https://github.com/darren-rawlings/renegade-cards)).

## The game
Gin rummy for two, you against Rosie (the Bicycle rules), a match to 100. Ten cards each; the
up-card starts the discard pile. First turn: the non-dealer, then the dealer, may take the
up-card; if both pass, the non-dealer draws from the stock. Each turn: draw from the stock or the
discard pile, then discard (not the card just taken). Melds are sets of 3–4 of a rank or runs of
3+ in a suit (aces low). With 10 or less deadwood you may knock; with none it's gin (+25, no
laying off). The defender lays off onto the knocker's melds; equal or lower deadwood is an
undercut (+25 to the defender). Two cards left in the stock: the hand is a draw. The match:
first to 100, then 25 a box (hand won) and 100 for the game. Rules in `src/Core` (tested,
including whole computer matches).

## Feel
- Your fan is kept arranged: melds together, deadwood last, and your deadwood count is shown.
- Tap the stock or the discard to draw, tap a card to throw it; Knock (or Gin!) appears when you
  can. At the end both hands are spread face up in melds and the lay-offs slide across.

## Computer levels
Easy (takes only meld cards, knocks at once), Medium (takes what cuts deadwood), Hard (also
remembers what you take and pass, avoids feeding you, holds out for gin while the stock is deep).

## Scope for version 1
Everything above. Later: choosing your own meld arrangement, Oklahoma gin.
