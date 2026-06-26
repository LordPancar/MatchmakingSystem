using MassTransit;
using Matchmaking.Shared;
using Matchmaking.Worker;
using MatchMaking.Shared;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<MatchmakingEngine>();

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
