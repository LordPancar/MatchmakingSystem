using System.Text.Json;
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

        // --- Maçı oyna: yazı-tura (50/50) ile kazananı belirle ---
        bool player1Wins = Random.Shared.Next(2) == 0;
        var (winnerId, winnerScore) = player1Wins
            ? (match.Player1Id, match.Score1)
            : (match.Player2Id, match.Score2);
        var (loserId, loserScore) = player1Wins
            ? (match.Player2Id, match.Score2)
            : (match.Player1Id, match.Score1);

        // --- Elo ile yeni puanları hesapla ve leaderboard'a yaz ---
        var (winnerNew, loserNew) = EloCalculator.Apply(winnerScore, loserScore);

        var db = _redis.GetDatabase();
        await db.SortedSetAddAsync("leaderboard", winnerId, winnerNew);
        await db.SortedSetAddAsync("leaderboard", loserId, loserNew);

        _logger.LogInformation("Sonuç: {Winner} kazandı ({wOld}->{wNew}), {Loser} kaybetti ({lOld}->{lNew})",
            winnerId, winnerScore, winnerNew, loserId, loserScore, loserNew);

        // Maç geçmişine yaz (en yeni başta) ve son 50 ile sınırla.
        var record = new MatchRecord
        {
            MatchId = match.MatchId,
            WinnerId = winnerId,
            LoserId = loserId,
            WinnerScore = winnerNew,
            LoserScore = loserNew,
            CompletedAtUtc = DateTime.UtcNow
        };
        await db.ListLeftPushAsync("matchmaking:history", JsonSerializer.Serialize(record));
        await db.ListTrimAsync("matchmaking:history", 0, 49);

        // Leaderboard güncellendikten SONRA event yayınla — böylece SignalR push
        // tetiklendiğinde arayüz güncel puanları okur (sıralama doğru olur).
        await context.Publish(new MatchCompletedEvent
        {
            MatchId = match.MatchId,
            Player1Id = match.Player1Id,
            Player2Id = match.Player2Id,
            WinnerId = winnerId,
            LoserId = loserId
        });

        _logger.LogInformation("MatchCompletedEvent yayınlandı: {MatchId}", match.MatchId);
    }
}
