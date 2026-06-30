namespace Matchmaking.Shared;

/// <summary>
/// Basit Elo puan hesabı. Maç bitince kazananın ve kaybedenin yeni puanını verir.
/// Güçlü rakibi yenmek çok kazandırır; zayıfa kaybetmek çok kaybettirir.
/// Saf (yan etkisiz) bir fonksiyon olduğu için kolayca birim test edilebilir.
/// </summary>
public static class EloCalculator
{
    public const int KFactor = 32;

    /// <returns>(kazananınYeniPuanı, kaybedeninYeniPuanı)</returns>
    public static (int winnerNew, int loserNew) Apply(int winnerScore, int loserScore)
    {
        // Beklenen kazanma olasılıkları (0..1)
        double expectedWinner = 1.0 / (1.0 + Math.Pow(10, (loserScore - winnerScore) / 400.0));
        double expectedLoser = 1.0 - expectedWinner;

        // Gerçek sonuç: kazanan 1, kaybeden 0
        int winnerNew = (int)Math.Round(winnerScore + KFactor * (1 - expectedWinner));
        int loserNew = (int)Math.Round(loserScore + KFactor * (0 - expectedLoser));

        // Puan negatif olmasın (API validasyonu da skoru >= 0 bekliyor)
        return (Math.Max(0, winnerNew), Math.Max(0, loserNew));
    }
}
