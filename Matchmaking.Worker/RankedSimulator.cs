using MassTransit;
using MatchMaking.Shared;
using StackExchange.Redis;

namespace Matchmaking.Worker;

/// <summary>
/// "Aktif" ranked sistemi besleyen arka plan servisi. Belirli aralıklarla
/// leaderboard'daki <b>online</b> oyuncuları (güncel puanlarıyla) tekrar
/// kuyruğa sokar; böylece oyuncular sürekli maça girer ve leaderboard
/// kendiliğinden hareket eder.
///
/// Açık/kapalı durumu Redis'teki "ranked:simulator" bayrağından her turda
/// okunur — bu sayede arayüzdeki switch ile çalışırken açılıp kapatılabilir.
/// İlk değer Ranked:SimulatorEnabled (varsayılan true) yapılandırmasından gelir.
/// </summary>
public class RankedSimulator : BackgroundService
{
    private const int MaxPerTick = 100;             // tek turda en fazla kaç oyuncu
    private const string SimulatorKey = "ranked:simulator";
    private const string OnlineKey = "players:online";

    private readonly IConnectionMultiplexer _redis;
    private readonly IBus _bus;
    private readonly ILogger<RankedSimulator> _logger;
    private readonly bool _enabledDefault;
    private readonly int _intervalSeconds;

    public RankedSimulator(IConnectionMultiplexer redis, IBus bus, ILogger<RankedSimulator> logger, IConfiguration config)
    {
        _redis = redis;
        _bus = bus;
        _logger = logger;
        _enabledDefault = config.GetValue("Ranked:SimulatorEnabled", true);
        _intervalSeconds = config.GetValue("Ranked:IntervalSeconds", 3);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = _redis.GetDatabase();

        // Bayrak henüz yoksa yapılandırma varsayılanıyla başlat.
        if (!await db.KeyExistsAsync(SimulatorKey))
        {
            await db.StringSetAsync(SimulatorKey, _enabledDefault ? "1" : "0");
        }

        _logger.LogInformation("Ranked simülatör servisi başladı ({Sn} sn aralık).", _intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);

                // Sadece AÇIKÇA "0" ise atla. Bayrak yoksa (ör. Redis temizlenince)
                // varsayılan açık kabul edilir — API'deki GetSimulator ile tutarlı.
                if (await db.StringGetAsync(SimulatorKey) == "0")
                {
                    continue;
                }

                var players = await db.SortedSetRangeByRankWithScoresAsync("leaderboard");
                if (players.Length < 2)
                {
                    continue;
                }

                // Sadece online oyuncular maça sokulur.
                var online = (await db.SetMembersAsync(OnlineKey)).Select(v => v.ToString()).ToHashSet();

                var requeued = 0;
                foreach (var p in players)
                {
                    if (requeued >= MaxPerTick) break;
                    var userId = p.Element.ToString();
                    if (!online.Contains(userId)) continue;

                    await _bus.Publish(new MatchRequest
                    {
                        UserId = userId,
                        Score = (int)p.Score
                    }, stoppingToken);
                    requeued++;
                }
            }
            catch (OperationCanceledException)
            {
                break;   // uygulama kapanıyor
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ranked simülatör turunda hata; sonraki turda tekrar denenecek.");
            }
        }
    }
}
