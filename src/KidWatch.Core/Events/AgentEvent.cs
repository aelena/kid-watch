using System.Text.Json.Nodes;

namespace KidWatch.Core.Events;

/// <summary>
/// One structured log event, serialised as a single JSON line (the on-disk and
/// Azure append-blob format). Factory methods cover the v0 event types so call
/// sites stay consistent with the field names in specs.md section 6.
/// </summary>
public sealed class AgentEvent
{
    private readonly JsonObject _json;

    private AgentEvent(DateTimeOffset ts, string device, string type,
        params (string Key, JsonNode? Value)[] fields)
    {
        _json = new JsonObject
        {
            ["ts"] = ts.ToString("o"),
            ["dev"] = device,
            ["type"] = type,
        };
        foreach (var (key, value) in fields)
            _json[key] = value;
    }

    public string ToJsonLine() => _json.ToJsonString();

    // ---- factories (one per v0 event type) ----

    public static AgentEvent Agent(string device, string eventName, DateTimeOffset ts, int? configVersion = null) =>
        new(ts, device, "agent", ("event", eventName), ("config_version", configVersion));

    public static AgentEvent ConfigApplied(string device, int version, DateTimeOffset ts) =>
        new(ts, device, "config", ("event", "applied"), ("version", version));

    public static AgentEvent ConfigRejected(string device, string reason, DateTimeOffset ts) =>
        new(ts, device, "config", ("event", "rejected"), ("reason", reason));

    public static AgentEvent AppSample(string device, string app, string title, DateTimeOffset ts) =>
        new(ts, device, "app_sample", ("app", app), ("title", title));

    public static AgentEvent Budget(string device, string target, string eventName,
        int usedMinutes, int limitMinutes, DateTimeOffset ts) =>
        new(ts, device, "budget", ("target", target), ("event", eventName),
            ("used_min", usedMinutes), ("limit_min", limitMinutes));

    public static AgentEvent Dns(string device, string domain, string action, string reason, DateTimeOffset ts) =>
        new(ts, device, "dns", ("domain", domain), ("action", action), ("reason", reason));
}
