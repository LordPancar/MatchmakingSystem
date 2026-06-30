using MassTransit;
using Matchmaking.Shared;
using MatchMaking.Shared;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Collections;
using System.Text.Json;

namespace Matchmaking.Api.Controllers;

[ApiController]
[Route("api/matchmaking")]
public class MatchmakingController : ControllerBase
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IConnectionMultiplexer _redis;

    public MatchmakingController(IPublishEndpoint publishEndpoint, IConnectionMultiplexer redis)
    {
        _publishEndpoint = publishEndpoint;
        _redis = redis;
    }

    [HttpPost("queue")]
    public async Task<IActionResult> QueueRequest([FromBody] MatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { message = "UserId boş olamaz." });
        }

        if (request.Score < 0)
        {
            return BadRequest(new { message = "Score negatif olamaz." });
        }

        // Kuyruğa giren oyuncu "online" (aktif) sayılır — simülatör bunları oynatır.
        var db = _redis.GetDatabase();
        await db.SetAddAsync("players:online", request.UserId);

        await _publishEndpoint.Publish(request);
        return Accepted(new { message = "Kuyruğa alındı.", requestId = request.RequestId });
    }

    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard()
    {
        var db = _redis.GetDatabase();
        var entries = await db.SortedSetRangeByRankWithScoresAsync("leaderboard", order: Order.Descending);
        var online = (await db.SetMembersAsync("players:online")).Select(v => v.ToString()).ToHashSet();

        var result = entries.Select(e =>
        {
            var userId = e.Element.ToString();
            return new
            {
                UserId = userId,
                Score = e.Score,
                Online = online.Contains(userId)
            };
        });

        return Ok(result);
    }

    [HttpGet("waiting")]
    public async Task<IActionResult> GetWaiting()
    {
        var db = _redis.GetDatabase();
        // Bekleyen oyuncular (skora göre) + giriş zamanları (hash)
        var entries = await db.SortedSetRangeByScoreWithScoresAsync("matchmaking:queue");
        var joined = await db.HashGetAllAsync("matchmaking:joined");
        var joinedMap = joined.ToDictionary(h => h.Name.ToString(), h => (long)h.Value);

        var result = entries.Select(e =>
        {
            var userId = e.Element.ToString();
            DateTime? joinedAtUtc = joinedMap.TryGetValue(userId, out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
                : null;
            return new
            {
                UserId = userId,
                Score = e.Score,
                JoinedAtUtc = joinedAtUtc
            };
        });

        return Ok(result);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory()
    {
        var db = _redis.GetDatabase();
        // Son maçlar (en yeni başta), Worker tarafından JSON olarak yazıldı.
        var items = await db.ListRangeAsync("matchmaking:history", 0, 49);
        var result = items.Select(v => JsonSerializer.Deserialize<MatchRecord>((string)v!));
        return Ok(result);
    }

    // Bir oyuncunun leaderboard puanını günceller. Basit/hızlı bir işlem olduğu
    // için kuyruğa gerek yok; doğrudan Redis'e yazılır (ZADD atomiktir).
    [HttpPut("player/{userId}")]
    public async Task<IActionResult> UpdatePlayer(string userId, [FromBody] UpdateScoreRequest body)
    {
        if (body.Score < 0)
        {
            return BadRequest(new { message = "Score negatif olamaz." });
        }

        var db = _redis.GetDatabase();
        // Sadece var olan oyuncuyu güncelle (yoksa 404)
        if (!(await db.SortedSetScoreAsync("leaderboard", userId)).HasValue)
        {
            return NotFound(new { message = $"'{userId}' leaderboard'da yok." });
        }

        await db.SortedSetAddAsync("leaderboard", userId, body.Score);
        return Ok(new { message = "Güncellendi.", userId, score = body.Score });
    }

    // Bir oyuncuyu tüm yapılardan siler: leaderboard, bekleyen kuyruk ve giriş zamanları.
    [HttpDelete("player/{userId}")]
    public async Task<IActionResult> DeletePlayer(string userId)
    {
        var db = _redis.GetDatabase();
        var removed = await db.SortedSetRemoveAsync("leaderboard", userId);
        await db.SortedSetRemoveAsync("matchmaking:queue", userId);
        await db.HashDeleteAsync("matchmaking:joined", userId);
        await db.SetRemoveAsync("players:online", userId);

        return removed
            ? Ok(new { message = "Silindi.", userId })
            : NotFound(new { message = $"'{userId}' bulunamadı." });
    }

    // Ranked simülatörün açık/kapalı durumunu döndürür.
    [HttpGet("simulator")]
    public async Task<IActionResult> GetSimulator()
    {
        var db = _redis.GetDatabase();
        var flag = await db.StringGetAsync("ranked:simulator");
        // Anahtar yoksa veya "1" ise açık kabul edilir.
        return Ok(new { enabled = flag != "0" });
    }

    // Ranked simülatörü açar/kapatır (Redis'teki ortak bayrağı yazar; Worker bunu her turda okur).
    [HttpPost("simulator")]
    public async Task<IActionResult> SetSimulator([FromBody] ToggleRequest body)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync("ranked:simulator", body.Enabled ? "1" : "0");
        return Ok(new { enabled = body.Enabled });
    }

    // Bir oyuncuyu online/offline yapar. Offline oyuncu leaderboard'da kalır
    // (puanı korunur) ama simülatör onu maça sokmaz.
    [HttpPost("player/{userId}/online")]
    public async Task<IActionResult> SetOnline(string userId, [FromBody] ToggleRequest body)
    {
        var db = _redis.GetDatabase();
        if (body.Enabled)
        {
            await db.SetAddAsync("players:online", userId);
        }
        else
        {
            await db.SetRemoveAsync("players:online", userId);
        }
        return Ok(new { userId, online = body.Enabled });
    }
}

public record UpdateScoreRequest
{
    public int Score { get; init; }
}

public record ToggleRequest
{
    public bool Enabled { get; init; }
}

