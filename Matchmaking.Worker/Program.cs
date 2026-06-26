using MassTransit;
using Matchmaking.Shared;
using Matchmaking.Worker;
using MatchMaking.Shared;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);
var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
var redisConnection = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";

builder.Services.AddSingleton<MatchmakingEngine>();
builder.Services.AddSingleton<IConnectionMultiplexer>(
    sp => ConnectionMultiplexer.Connect(redisConnection));

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
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
