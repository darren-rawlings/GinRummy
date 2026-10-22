namespace GameTemplate.Core;

/// <summary>
/// The game's rules go in Core: plain C# with no Godot types, so they're unit tested without the
/// engine (tests/GameTemplate.Tests). This placeholder counts taps until a target; replace it.
/// </summary>
public sealed class Counter
{
	public Counter(int target) => Target = target;

	public int Target { get; }
	public int Count { get; private set; }
	public bool IsDone => Count >= Target;

	public void Tap()
	{
		if (!IsDone)
			Count++;
	}
}
