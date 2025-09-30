using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.Discord;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

var webhookId = ulong.Parse(builder.Configuration["Discord:WebhookId"])!;
var webhookToken = builder.Configuration["Discord:WebhookToken"];

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.Discord(
        webhookId: webhookId,
        webhookToken: webhookToken,
        restrictedToMinimumLevel: LogEventLevel.Error)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseSerilogRequestLogging();

app.MapGet("/test-discord-error", () => 
{
    Log.Error("Đây là lỗi tao gửi để giải trí");
    throw new Exception("Lỗi này sẽ được gửi đến Discord");
}
);

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=GenerateSlide}/{action=GenerateSlideView}/{id?}");

app.Run();
