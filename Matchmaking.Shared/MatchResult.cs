
using System.Security.Cryptography.X509Certificates;

namespace Matchmaking.Shared;

public record MatchResult
{
    public Guid MatchId { get; init; } = Guid.NewGuid();
    public string Player1Id { get; init; } = string.Empty;
    public string Player2Id { get; init; } = string.Empty;
    public int ScoreDifference { get; init; }
}

  
