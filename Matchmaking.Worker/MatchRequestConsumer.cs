
using MassTransit;
using Matchmaking.Shared;
using MatchMaking.Shared;
using StackExchange.Redis;

namespace Matchmaking.Worker;

public class MatchRequestConsumer : IConsumer<MatchRequest>
{
    private readonly ILogger<MatchRequestConsumer> _logger;
    private readonly MatchmakingEngine _engine;
    private readonly IConnectionMultiplexer _redis;
    public MatchRequestConsumer(ILogger<MatchRequestConsumer> logger, MatchmakingEngine engine, IConnectionMultiplexer redis)
    {
        _logger = logger;
        _engine = engine;
        _redis = redis;
    }

    public async Task Consume(ConsumeContext<MatchRequest> context)
    {
        var request = context.Message;
        _logger.LogInformation("Mesaj alındı: {UserId}, Skor: {Score}", request.UserId, request.Score);

        _engine.AddToQueue(request);
        var matches = _engine.TryMatch();

        var db = _redis.GetDatabase();

        foreach (var match in matches)
        {
            _logger.LogInformation("Eşleşme bulundu: {P1} vs {P2} (fark: {diff})", match.Player1Id, match.Player2Id, match.ScoreDifference);

            await db.SortedSetAddAsync("leaderboard", match.Player1Id, match.Score1);
            await db.SortedSetAddAsync("leaderboard", match.Player2Id, match.Score2);

            await context.Publish(new MatchCompletedEvent
            {
                MatchId = match.MatchId,
                Player1Id = match.Player1Id,
                Player2Id = match.Player2Id
            });

            _logger.LogInformation("MatchCompletedEvent yayınlandı: {MatchId}", match.MatchId);

        }

    }
}


