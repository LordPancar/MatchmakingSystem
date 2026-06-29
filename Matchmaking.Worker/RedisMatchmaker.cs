using Matchmaking.Shared;
using MatchMaking.Shared;
using StackExchange.Redis;

namespace Matchmaking.Worker;

/// <summary>
/// Paylaşımlı (Redis tabanlı) eşleştirici. Bekleyen oyuncular Redis Sorted Set'te
/// (skora göre sıralı) tutulur. Eşleştirme mantığı tek bir Lua script içinde,
/// Redis tarafında atomik olarak çalışır; bu sayede birden fazla Worker (consumer)
/// aynı anda çalışsa bile race condition ya da mükerrer eşleşme oluşmaz.
/// </summary>
public class RedisMatchmaker
{
    private const string QueueKey = "matchmaking:queue";
    private const int MaxScoreDifference = 100;

    // Atomik "eşleştir ya da kuyruğa ekle" script'i.
    // KEYS[1] = kuyruk (sorted set)
    // ARGV[1] = userId, ARGV[2] = score, ARGV[3] = izin verilen max skor farkı
    // Dönüş: eşleşme varsa { rakipId, rakipSkor }, yoksa nil.
    private const string MatchScript = @"
local userId = ARGV[1]
local score = tonumber(ARGV[2])
local maxDiff = tonumber(ARGV[3])
local lo = score - maxDiff
local hi = score + maxDiff

local candidates = redis.call('ZRANGEBYSCORE', KEYS[1], lo, hi, 'WITHSCORES')

local bestId = nil
local bestScore = nil
local bestDiff = nil
for i = 1, #candidates, 2 do
    local cid = candidates[i]
    local cscore = tonumber(candidates[i + 1])
    if cid ~= userId then
        local d = math.abs(cscore - score)
        if bestDiff == nil or d < bestDiff then
            bestDiff = d
            bestId = cid
            bestScore = cscore
        end
    end
end

if bestId ~= nil then
    redis.call('ZREM', KEYS[1], bestId)
    return { bestId, tostring(bestScore) }
end

redis.call('ZADD', KEYS[1], score, userId)
return nil
";

    private readonly IConnectionMultiplexer _redis;

    public RedisMatchmaker(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    /// <summary>
    /// Gelen isteği atomik olarak ya bekleyen bir oyuncuyla eşleştirir ya da kuyruğa ekler.
    /// Eşleşme oluştuysa <see cref="MatchResult"/>, oluşmadıysa null döner.
    /// </summary>
    public async Task<MatchResult?> TryMatchAsync(MatchRequest request)
    {
        var db = _redis.GetDatabase();

        var result = await db.ScriptEvaluateAsync(
            MatchScript,
            new RedisKey[] { QueueKey },
            new RedisValue[] { request.UserId, request.Score, MaxScoreDifference });

        if (result.IsNull)
        {
            return null;
        }

        var arr = (RedisValue[])result!;
        var opponentId = (string)arr[0]!;
        var opponentScore = (int)arr[1];

        return new MatchResult
        {
            Player1Id = opponentId,
            Player2Id = request.UserId,
            Score1 = opponentScore,
            Score2 = request.Score,
            ScoreDifference = Math.Abs(opponentScore - request.Score)
        };
    }
}
