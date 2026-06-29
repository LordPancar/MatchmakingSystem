using Matchmaking.Shared;
using MatchMaking.Shared;
using Xunit;

namespace Matchmaking.Tests;

public class MatchmakingEngineTests
{
    [Fact]
    public void TryMatch_TwoUsersWithCloseScores_ShouldMatch()
    {
        
        var engine = new MatchmakingEngine();
        engine.AddToQueue(new MatchRequest { UserId = "ali", Score = 1500 });
        engine.AddToQueue(new MatchRequest { UserId = "veli", Score = 1530 });

        
        var results = engine.TryMatch();

        
        Assert.Single(results);
        Assert.Equal("ali", results[0].Player1Id);
        Assert.Equal("veli", results[0].Player2Id);
    }

    [Fact]
    public void TryMatch_TwoUsersWithFarScores_ShouldNotMatch()
    {
        var engine = new MatchmakingEngine();
        engine.AddToQueue(new MatchRequest { UserId = "ali", Score = 1500 });
        engine.AddToQueue(new MatchRequest { UserId = "ayse", Score = 2200 });

        var results = engine.TryMatch();

        Assert.Empty(results);
    }

    [Fact]
    public void TryMatch_SameUserTwice_ShouldNotMatchWithSelf()
    {
        var engine = new MatchmakingEngine();
        engine.AddToQueue(new MatchRequest { UserId = "ali", Score = 1500 });
        engine.AddToQueue(new MatchRequest { UserId = "ali", Score = 1500 });

        var results = engine.TryMatch();

        Assert.Empty(results);
    }

    [Fact]
    public void TryMatch_EmptyQueue_ShouldReturnEmptyList()
    {
        var engine = new MatchmakingEngine();

        var results = engine.TryMatch();

        Assert.Empty(results);
    }

    [Fact]
    public void TryMatch_MatchedUsers_ShouldBeRemovedFromQueue()
    {
        var engine = new MatchmakingEngine();
        engine.AddToQueue(new MatchRequest { UserId = "ali", Score = 1500 });
        engine.AddToQueue(new MatchRequest { UserId = "veli", Score = 1530 });

        engine.TryMatch();
        var secondCall = engine.TryMatch(); 

        Assert.Empty(secondCall);
    }

    [Theory]
    [InlineData(1500, 1550, true)]   
    [InlineData(1500, 1600, true)]   
    [InlineData(1500, 1601, false)]  
    public void TryMatch_ScoreDifferenceBoundary_ShouldRespectThreshold(int score1, int score2, bool shouldMatch)
    {
        var engine = new MatchmakingEngine();
        engine.AddToQueue(new MatchRequest { UserId = "p1", Score = score1 });
        engine.AddToQueue(new MatchRequest { UserId = "p2", Score = score2 });

        var results = engine.TryMatch();

        Assert.Equal(shouldMatch, results.Count == 1);
    }
}