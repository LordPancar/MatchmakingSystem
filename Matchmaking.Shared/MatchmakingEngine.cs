

using MatchMaking.Shared;

namespace Matchmaking.Shared;

    public class MatchmakingEngine
    {
    private readonly List<MatchRequest> _queue = new();
    private const int MaxScoreDifference = 100;

    public void AddToQueue(MatchRequest request) => _queue.Add(request);

    public List<MatchResult> TryMatch()
    {
        var results = new List<MatchResult>();
        var sorted = _queue.OrderBy(r => r.Score).ToList();

        for(int i = 0;i < sorted.Count - 1;i++)
        {
            var a = sorted[i];
            var b = sorted[i + 1];
            var diff = Math.Abs(a.Score - b.Score);

            if ( diff <= MaxScoreDifference)
            {
                results.Add(new MatchResult
                {
                    Player1Id = a.UserId,
                    Player2Id = b.UserId,
                    Score1 = a.Score,
                    Score2 = b.Score,
                    ScoreDifference = diff
                });
                _queue.Remove(a);
                _queue.Remove(b);

            }
        }
        return results;
    }
    }

