using MassTransit;
using Matchmaking.Shared;
using MatchMaking.Shared;
using Microsoft.AspNetCore.Mvc;
using System.Collections;

namespace Matchmaking.Api.Controllers;

[ApiController]
[Route("api/matchmaking")]
public class MatchmakingController : ControllerBase
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MatchmakingController(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint; 
    }

    [HttpPost("queue")]
    public async Task<IActionResult> QueueRequest([FromBody] MatchRequest request)
    {
        await _publishEndpoint.Publish(request);
        return Accepted(new { message = "Kuyruğa alındı.", requestId = request.RequestId });
    }


}

