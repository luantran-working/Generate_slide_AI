using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Core;
using Serilog.Sinks.Discord;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "IIT API", Version = "v1" });
    c.OperationFilter<AI_API.ConfigHeaderKey.AddHeaderParameterOperationFilter>();
});
builder.Services.AddLogging();
builder.Services.AddSerilog();
builder.Services.AddHttpClient();

var discordId = builder.Configuration["Discord:WebhookId"];
var webToken = builder.Configuration["Discord:WebhookToken"];
if (string.IsNullOrEmpty(discordId) || string.IsNullOrEmpty(webToken))
{
    throw new ArgumentException("DiscordId and WebToken must be provided in the configuration.");
}

Log.Logger = new LoggerConfiguration().MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.Discord(
        webhookId: ulong.Parse(discordId),
        webhookToken: webToken,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error)
    .CreateLogger();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins",
        builder => builder.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

var app = builder.Build();

 app.UseSwagger();
 app.UseSwaggerUI();


app.UseSerilogRequestLogging();

app.MapGet("/test-discord-error", () =>
{
    Log.Error("This is a test error message sent to Discord.");
    throw new Exception("This error will be sent to Discord.");
});
app.UseCors("AllowAllOrigins");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
