using KidWatch.Agent;
using KidWatch.Agent.Azure;
using KidWatch.Agent.Configuration;
using KidWatch.Agent.Counters;
using KidWatch.Agent.Enforcement;
using KidWatch.Agent.Logging;
using KidWatch.Agent.Notifications;
using KidWatch.Agent.Processes;
using KidWatch.Agent.Workers;
using KidWatch.Core.Time;

var builder = Host.CreateApplicationBuilder(args);

// Run as a Windows service in production; runs as a plain console app in dev.
builder.Services.AddWindowsService(o => o.ServiceName = "pc-kid-watch");

// Bind the "Agent" section once and share the instance everywhere.
var options = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new AgentOptions();
Directory.CreateDirectory(options.DataDir);
builder.Services.AddSingleton(options);

builder.Services.AddSingleton<IConfigStore, ConfigStore>();
builder.Services.AddSingleton<ICounterStore, SqliteCounterStore>();
builder.Services.AddSingleton<IEventSink, JsonlEventSink>();

// Config comes from Azure (download <device>/config.yaml) or a local file (dev).
if (options.UseAzure)
{
    builder.Services.AddSingleton<AzureStorage>();
    builder.Services.AddSingleton<IConfigSource, AzureBlobConfigSource>();
    builder.Services.AddHostedService<AzureAppendUploader>();
}
else
{
    builder.Services.AddSingleton<IConfigSource, LocalFileConfigSource>();
}
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IProcessProbe, WindowsProcessProbe>();
builder.Services.AddSingleton<INotifier, LoggingNotifier>();

// The time engine ticks every 15s (explicit interval, not DI-resolved).
builder.Services.AddSingleton(sp => new TimeEngine(
    sp.GetRequiredService<IConfigStore>(),
    sp.GetRequiredService<ICounterStore>(),
    sp.GetRequiredService<IProcessProbe>(),
    sp.GetRequiredService<IEventSink>(),
    sp.GetRequiredService<INotifier>(),
    sp.GetRequiredService<IClock>(),
    sp.GetRequiredService<ILogger<TimeEngine>>(),
    TimeSpan.FromSeconds(15)));

builder.Services.AddHostedService<ConfigPoller>();
builder.Services.AddHostedService<HeartbeatWorker>();
builder.Services.AddHostedService<TimeEngineWorker>();

builder.Build().Run();
