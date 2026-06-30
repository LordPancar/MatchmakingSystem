using Microsoft.AspNetCore.SignalR;

namespace Matchmaking.Api.Hubs;

/// <summary>
/// Tarayıcı istemcilerin bağlandığı SignalR hub'ı. Sunucu, bir eşleşme
/// olduğunda buradan tüm bağlı istemcilere mesaj iter (push). İstemci
/// sürekli soru sormak (polling) yerine sadece bildirim gelince güncellenir.
/// </summary>
public class MatchmakingHub : Hub
{
}
