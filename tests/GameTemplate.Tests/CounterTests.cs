using GameTemplate.Core;
using Renegade.Core;
using Xunit;

namespace GameTemplate.Tests;

public class CounterTests
{
	[Fact]
	public void CountsUpToTheTargetAndStops()
	{
		var counter = new Counter(3);
		for (int i = 0; i < 5; i++)
			counter.Tap();
		Assert.Equal(3, counter.Count);
		Assert.True(counter.IsDone);
	}

	[Fact]
	public void TheSharedCoreIsAvailableToTests()
	{
		var score = new Scoreboard();
		score.Win(first: true);
		Assert.Equal(1, score.First);
	}
}
