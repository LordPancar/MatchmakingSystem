
using MassTransit;
using Matchmaking.Shared;
using MatchMaking.Shared;

namespace Matchmaking.Worker;

public class MatchRequestConsumer : IConsumer<MatchRequest>
{
    private readonly ILogger<MatchRequestConsumer> _logger;
    private readonly MatchmakingEngine _engine;
    public MatchRequestConsumer(ILogger<MatchRequestConsumer> logger, MatchmakingEngine engine)
    {
        _logger = logger;
        _engine = engine;
    }

    public async Task Consume(ConsumeContext<MatchRequest> context)
    {
        var request = context.Message;
        _logger.LogInformation("Mesaj alındı: {UserId}, Skor: {Score}", request.UserId, request.Score);

        _engine.AddToQueue(request);
        var matches = _engine.TryMatch();

        foreach (var match in matches)
        {
            _logger.LogInformation("Eşleşme bulundu: {P1} vs {P2} (fark: {diff})", match.Player1Id, match.Player2Id, match.ScoreDifference);

        }

    }
}


