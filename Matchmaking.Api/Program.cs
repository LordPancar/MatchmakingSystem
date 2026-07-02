using MassTransit;
using Matchmaking.Api.Consumers;
using Matchmaking.Api.Data;
using Matchmaking.Api.Hubs;
using Matchmaking.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
var redisConnection = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";

var pgConnection = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=matchmaking;Username=matchmaking;Password=matchmaking";
var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev-only-change-me-super-secret-key-0123456789abcdef";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "matchmaking";
var adminUsername = builder.Configuration["Admin:Username"] ?? "admin";
var adminPassword = builder.Configuration["Admin:Password"] ?? "admin123";

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(pgConnection));
builder.Services.AddScoped<TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtIssuer,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = ConfigurationOptions.Parse(redisConnection);
    // Baglanti kurulamazsa hata firlatma; arka planda yeniden denemeye devam et.
    options.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(options);
});

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<MatchCompletedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    for (var i = 0; i < 15; i++)
    {
        try { db.Database.EnsureCreated(); break; }
        catch { await Task.Delay(2000); }
    }

    // Admin hesabı yoksa yapılandırmadan oluştur (varsayılan admin/admin123).
    if (!db.Users.Any(u => u.IsAdmin) && !db.Users.Any(u => u.Username == adminUsername))
    {
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<Matchmaking.Api.Models.User>();
        var admin = new Matchmaking.Api.Models.User { Username = adminUsername, IsAdmin = true };
        admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
        db.Users.Add(admin);
        db.SaveChanges();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// wwwroot altindaki arayuzu (index.html) sun - API ile ayni origin, CORS gerekmez
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();   // ÖNCE kimlik
app.UseAuthorization();    // SONRA yetki
app.MapControllers();
app.MapHub<MatchmakingHub>("/hub/matchmaking");

app.Run();