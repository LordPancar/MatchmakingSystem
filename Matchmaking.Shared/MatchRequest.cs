namespace MatchMaking.Shared;

public record MatchRequest
{
    public Guid RequestId { get; init; } = Guid.NewGuid();
    public string UserId { get; init; } = String.Empty;
    public int Score { get; init; }
    public DateTime RequestedAtUtc { get; init; } = DateTime.UtcNow;
}