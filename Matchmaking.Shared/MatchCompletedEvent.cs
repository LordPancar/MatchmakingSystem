using System;
using System.Collections.Generic;
using System.Text;

namespace Matchmaking.Shared;

public record MatchCompletedEvent
{
    public Guid MatchId { get; init; }
    public string Player1Id { get; init; } = String.Empty;
    public string Player2Id { get; init; } = String.Empty;
    public DateTime CompletedAtUtc { get; init; } = DateTime.UtcNow;
}
