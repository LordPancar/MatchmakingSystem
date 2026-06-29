using MassTransit;
using Matchmaking.Shared;
using MatchMaking.Shared;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Collections;

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
        await _publishEndpoint.Publish(request);
        return Accepted(new { message = "Kuyruğa alındı.", requestId = request.RequestId });
    }

    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard()
    {
        var db = _redis.GetDatabase();
        var entries = await db.SortedSetRangeByRankWithScoresAsync("leaderboard", order: Order.Descending);

        var result = entries.Select(e => new
        {
            UserId = e.Element.ToString(),
            Score = e.Score
        });

        return Ok(result);
    }


}

