using MassTransit;
using Matchmaking.Shared;
using Matchmaking.Worker;
using MatchMaking.Shared;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);
var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
var redisConnection = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = ConfigurationOptions.Parse(redisConnection);
    // Baglanti kurulamazsa hata firlatma; arka planda yeniden denemeye devam et.
    // (Deploy/restart/scale anlarinda gecici kopmalarda mesajin fault olmasini onler.)
    options.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddSingleton<RedisMatchmaker>();
builder.Services.AddHostedService<RankedSimulator>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<MatchRequestConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
        // Gecici hatalarda mesaji hemen fault'a dusurme: 500ms arayla 5 kez yeniden dene.
        cfg.UseMessageRetry(r => r.Interval(5, TimeSpan.FromMilliseconds(500)));
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
