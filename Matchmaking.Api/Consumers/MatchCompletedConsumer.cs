using MassTransit;
using Matchmaking.Api.Hubs;
using Matchmaking.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Matchmaking.Api.Consumers;

/// <summary>
/// Worker'ın yayınladığı <see cref="MatchCompletedEvent"/>'i dinler ve bağlı
/// tüm tarayıcılara SignalR üzerinden "matchCompleted" sinyali gönderir.
/// İstemci bu sinyali alınca veriyi tazeler — böylece sürekli polling gerekmez.
///
/// Not: Bu, "neden MatchCompletedEvent yayınlıyoruz?" sorusunun cevabı —
/// olay (event), eşleştirmeyi yapan Worker ile arayüzü besleyen API'yi
/// birbirine bağlamadan haberdar ediyor.
/// </summary>
public class MatchCompletedConsumer : IConsumer<MatchCompletedEvent>
{
    private readonly IHubContext<MatchmakingHub> _hub;

    public MatchCompletedConsumer(IHubContext<MatchmakingHub> hub)
    {
        _hub = hub;
    }

    public async Task Consume(ConsumeContext<MatchCompletedEvent> context)
    {
        var e = context.Message;
        await _hub.Clients.All.SendAsync("matchCompleted", new
        {
            matchId = e.MatchId,
            player1Id = e.Player1Id,
            player2Id = e.Player2Id,
            completedAtUtc = e.CompletedAtUtc
        });
    }
}
