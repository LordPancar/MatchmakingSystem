using MassTransit;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
var redisConnection = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IConnectionMultiplexer>(
    sp => ConnectionMultiplexer.Connect(redisConnection));

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();
app.MapControllers();

app.Run();