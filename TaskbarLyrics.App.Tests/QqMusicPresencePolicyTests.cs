using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class QqMusicPresencePolicyTests
{
    [Fact]
    public void NeverSeeingThePlayerDoesNotExit()
    {
        var policy = new QqMusicPresencePolicy();
        policy.Seed(false);

        for (var index = 0; index < 10; index++)
        {
            Assert.False(policy.Evaluate(false));
        }
    }

    [Fact]
    public void MissingPlayerExitsAfterTheConfiguredNumberOfMisses()
    {
        var policy = new QqMusicPresencePolicy();
        policy.Seed(true);

        for (var index = 1; index < QqMusicPresencePolicy.ExitMissThreshold; index++)
        {
            Assert.False(policy.Evaluate(false));
        }

        Assert.True(policy.Evaluate(false));
    }

    [Fact]
    public void PlayerReappearingResetsTheMissCount()
    {
        var policy = new QqMusicPresencePolicy();
        policy.Seed(true);

        Assert.False(policy.Evaluate(false));
        Assert.False(policy.Evaluate(false));
        Assert.False(policy.Evaluate(true));
        Assert.False(policy.Evaluate(false));
        Assert.False(policy.Evaluate(false));
        Assert.True(policy.Evaluate(false));
    }

    [Fact]
    public void SeedingARunningPlayerKeepsTheApplicationAlive()
    {
        var policy = new QqMusicPresencePolicy();
        policy.Seed(true);

        Assert.False(policy.Evaluate(true));
        Assert.False(policy.Evaluate(false));
        Assert.False(policy.Evaluate(false));
        Assert.True(policy.Evaluate(false));
    }
}
