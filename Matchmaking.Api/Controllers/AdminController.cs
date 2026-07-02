using Matchmaking.Api.Data;
using Matchmaking.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Matchmaking.Api.Controllers;

// Yalnızca admin rolü erişebilir (JWT'de Role=Admin claim'i olmalı).
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private const int StartingRating = 1000;

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly PasswordHasher<User> _hasher = new();

    public AdminController(AppDbContext db, IConnectionMultiplexer redis)
    {
        _db = db;
        _redis = redis;
    }

    public record CreateUserRequest(string Username, string Password, bool IsAdmin);
    public record RoleRequest(bool IsAdmin);

    // Veritabanındaki tüm kayıtlı hesaplar (parola hash'i döndürülmez).
    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        var users = await _db.Users
            .OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.Username, u.IsAdmin, u.CreatedAtUtc })
            .ToListAsync();

        return Ok(users);
    }

    // Yeni hesap oluştur (istersen admin olarak). "Admin ekle" bununla yapılır.
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
        {
            return BadRequest(new { message = "Kullanıcı adı ve parola gerekli." });
        }
        if (await _db.Users.AnyAsync(u => u.Username == body.Username))
        {
            return Conflict(new { message = "Bu kullanıcı adı alınmış." });
        }

        var user = new User { Username = body.Username, IsAdmin = body.IsAdmin };
        user.PasswordHash = _hasher.HashPassword(user, body.Password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await _redis.GetDatabase().SortedSetAddAsync("leaderboard", body.Username, StartingRating);

        return Ok(new { user.Username, user.IsAdmin });
    }

    // Mevcut bir kullanıcıyı admin yap / adminliğini al (promote/demote).
    [HttpPut("users/{username}/role")]
    public async Task<IActionResult> SetRole(string username, [FromBody] RoleRequest body)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null)
        {
            return NotFound(new { message = $"'{username}' bulunamadı." });
        }

        // Son admini düşürerek kilitlenmeyi önle.
        if (user.IsAdmin && !body.IsAdmin && await _db.Users.CountAsync(u => u.IsAdmin) <= 1)
        {
            return BadRequest(new { message = "Son admin düşürülemez." });
        }

        user.IsAdmin = body.IsAdmin;
        await _db.SaveChangesAsync();
        return Ok(new { user.Username, user.IsAdmin });
    }

    // Bir hesabı tamamen sil (DB + Redis temizliği).
    [HttpDelete("users/{username}")]
    public async Task<IActionResult> DeleteUser(string username)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null)
        {
            return NotFound(new { message = $"'{username}' bulunamadı." });
        }
        if (user.IsAdmin && await _db.Users.CountAsync(u => u.IsAdmin) <= 1)
        {
            return BadRequest(new { message = "Son admin silinemez." });
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        var db = _redis.GetDatabase();
        await db.SortedSetRemoveAsync("leaderboard", username);
        await db.SortedSetRemoveAsync("matchmaking:queue", username);
        await db.HashDeleteAsync("matchmaking:joined", username);
        await db.SetRemoveAsync("players:online", username);

        return Ok(new { message = "Hesap silindi.", username });
    }
}
