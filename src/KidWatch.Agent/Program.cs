using KidWatch.Agent;
using KidWatch.Agent.Configuration;
using KidWatch.Agent.Counters;
using KidWatch.Agent.Logging;
using KidWatch.Agent.Workers;

var builder = Host.CreateApplicationBuilder(args);

// Run as a Windows service in production; runs as a plain console app in dev.
builder.Services.AddWindowsService(o => o.ServiceName = "pc-kid-watch");

// Bind the "Agent" section once and share the instance everywhere.
var options = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new AgentOptions();
Directory.CreateDirectory(options.DataDir);
builder.Services.AddSingleton(options);

builder.Services.AddSingleton<IConfigStore, ConfigStore>();
builder.Services.AddSingleton<IConfigSource, LocalFileConfigSource>();
builder.Services.AddSingleton<ICounterStore, SqliteCounterStore>();
builder.Services.AddSingleton<IEventSink, JsonlEventSink>();

builder.Services.AddHostedService<ConfigPoller>();
builder.Services.AddHostedService<HeartbeatWorker>();

builder.Build().Run();
