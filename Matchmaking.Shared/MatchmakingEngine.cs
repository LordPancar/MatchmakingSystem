

using MatchMaking.Shared;

namespace Matchmaking.Shared;

    public class MatchmakingEngine
    {
    private readonly List<MatchRequest> _queue = new();
    private const int MaxScoreDifference = 100;

    public void AddToQueue(MatchRequest request) => _queue.Add(request);

    public List<MatchResult> TryMatch()
    {
        var results = new
    }
    }

