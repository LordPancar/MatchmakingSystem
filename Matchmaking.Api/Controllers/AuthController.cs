using Matchmaking.Api.Data;
using Matchmaking.Api.Models;
using Matchmaking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Matchmaking.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const int StartingRating = 1000;

    private readonly AppDbContext _db;
    private readonly TokenService _tokens;
    private readonly IConnectionMultiplexer _redis;
    private readonly PasswordHasher<User> _hasher = new();

    public AuthController(AppDbContext db,TokenService tokens, IConnectionMultiplexer redis)
    {
        _db = db;
        _tokens = tokens;
        _redis = redis;
    }

    public record AuthRequest(string Username, string Password);

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] AuthRequest body)
    {
        
        if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
        {
            return BadRequest(new { message = "Kullanıcı adı ve parola gerekli." });
        }

        if (await _db.Users.AnyAsync(u => u.Username == body.Username))
        {
            return Conflict(new { message = "Bu kullanıcı alınmış" });
        }

        var user = new User { Username = body.Username };
        user.PasswordHash = _hasher.HashPassword(user, body.Password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var redis = _redis.GetDatabase();
        await redis.SortedSetAddAsync("leaderboard", body.Username, StartingRating);
        await redis.SetAddAsync("players:online", body.Username);

        return Ok(new { token = _tokens.CreateToken(body.Username), username = body.Username });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AuthRequest body)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == body.Username);
        if (user is null)
        {
            return Unauthorized(new { message = "Kullanıcı adı veya parola hatalı." });
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, body.Password);
        if(result == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Kullanıcı adı veya parola hatalı." });
        }

        await _redis.GetDatabase().SetAddAsync("players:online", user.Username);

        return Ok(new { token = _tokens.CreateToken(body.Username), username = body.Username });

    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(username))
            await _redis.GetDatabase().SetRemoveAsync("players:online", username);
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => Ok(new { username = User.Identity?.Name });
}
