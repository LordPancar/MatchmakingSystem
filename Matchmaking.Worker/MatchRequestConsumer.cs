using MassTransit;
using Matchmaking.Shared;
using MatchMaking.Shared;
using StackExchange.Redis;

namespace Matchmaking.Worker;

public class MatchRequestConsumer : IConsumer<MatchRequest>
{
    private readonly ILogger<MatchRequestConsumer> _logger;
    private readonly RedisMatchmaker _matchmaker;
    private readonly IConnectionMultiplexer _redis;

    public MatchRequestConsumer(ILogger<MatchRequestConsumer> logger, RedisMatchmaker matchmaker, IConnectionMultiplexer redis)
    {
        _logger = logger;
        _matchmaker = matchmaker;
        _redis = redis;
    }

    public async Task Consume(ConsumeContext<MatchRequest> context)
    {
        var request = context.Message;
        _logger.LogInformation("Mesaj alındı: {UserId}, Skor: {Score}", request.UserId, request.Score);

        
        var match = await _matchmaker.TryMatchAsync(request);

        if (match is null)
        {
            _logger.LogInformation("{UserId} kuyruğa eklendi, rakip bekleniyor.", request.UserId);
            return;
        }

        _logger.LogInformation("Eşleşme bulundu: {P1} vs {P2} (fark: {diff})", match.Player1Id, match.Player2Id, match.ScoreDifference);

        // Bu eşleşmenin tek sahibi bu Worker — leaderboard ve event güvenle yazılır.
        var db = _redis.GetDatabase();
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
