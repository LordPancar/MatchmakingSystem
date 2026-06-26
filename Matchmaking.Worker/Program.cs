using MassTransit;
using Matchmaking.Shared;
using Matchmaking.Worker;
using MatchMaking.Shared;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<MatchmakingEngine>();
builder.Services.AddSingleton<IConnectionMultiplexer>(
    sp => ConnectionMultiplexer.Connect("localhost:6379"));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<MatchRequestConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
