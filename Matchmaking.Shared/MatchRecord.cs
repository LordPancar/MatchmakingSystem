namespace Matchmaking.Shared;

/// <summary>
/// Tamamlanmış bir maçın geçmiş kaydı. Redis'te bir listede (matchmaking:history)
/// JSON olarak tutulur; arayüzdeki "Son Maçlar" bölümünü besler.
/// </summary>
public record MatchRecord
{
    public Guid MatchId { get; init; }
    public string WinnerId { get; init; } = string.Empty;
    public string LoserId { get; init; } = string.Empty;
    public int WinnerScore { get; init; }   // maç sonrası yeni puan
    public int LoserScore { get; init; }    // maç sonrası yeni puan
    public DateTime CompletedAtUtc { get; init; } = DateTime.UtcNow;
}
