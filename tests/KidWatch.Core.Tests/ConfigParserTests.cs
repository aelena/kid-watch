using KidWatch.Core.Configuration;
using Xunit;

namespace KidWatch.Core.Tests;

public class ConfigParserTests
{
    private const string Valid =
        """
        device: dev-laptop
        version: 2
        poll_minutes: 10
        timezone: Europe/Madrid
        apps:
          - name: Roblox
            match: ["RobloxPlayerBeta.exe"]
            daily_limit_minutes: 90
            warn_minutes: 10
            action: kill
            schedule:
              mon-fri: ["16:00-19:00"]
        urls:
          deny: ["*.bad.com"]
          schedule:
            - domain: "youtube.com"
              allow:
                sat-sun: ["10:00-20:00"]
        activity:
          sample_minutes: 2
          screenshots:
            enabled: false
            mode: random
            random_min_minutes: 10
            random_max_minutes: 40
        azure:
          logs_sas_ref: "logs"
        """;

    [Fact]
    public void Parses_a_valid_config()
    {
        var result = ConfigParser.Parse(Valid);
        Assert.True(result.IsOk, result.IsError ? result.Error : "");

        var c = result.Value;
        Assert.Equal("dev-laptop", c.Device);
        Assert.Equal(2, c.Version);
        Assert.Equal(10, c.PollMinutes);
        Assert.Single(c.Apps);
        Assert.Equal(EnforcementAction.Kill, c.Apps[0].Action);
        Assert.NotNull(c.Apps[0].Schedule);
        Assert.Single(c.Urls.Schedule);
        Assert.Equal(ScreenshotMode.Random, c.Activity.Screenshots.Mode);
        Assert.False(c.Activity.Screenshots.Enabled);
    }

    [Fact]
    public void Defaults_applied_for_missing_optionals()
    {
        var yaml =
            """
            device: minimal
            version: 1
            apps: []
            """;
        var c = ConfigParser.Parse(yaml).Value;
        Assert.Equal(15, c.PollMinutes);      // default
        Assert.Equal(5, c.UploadMinutes);     // default
        Assert.Equal("UTC", c.TimeZone);      // default
        Assert.Equal(2, c.Activity.SampleMinutes);
    }

    [Fact]
    public void Missing_device_is_an_error()
    {
        var result = ConfigParser.Parse("version: 1\napps: []");
        Assert.True(result.IsError);
        Assert.Contains("device", result.Error);
    }

    [Fact]
    public void Bad_action_is_an_error()
    {
        var yaml =
            """
            device: d
            version: 1
            apps:
              - name: X
                match: ["x.exe"]
                daily_limit_minutes: 10
                action: nuke
            """;
        var result = ConfigParser.Parse(yaml);
        Assert.True(result.IsError);
        Assert.Contains("action", result.Error);
    }

    [Fact]
    public void Garbage_yaml_fails_cleanly_without_throwing()
    {
        var result = ConfigParser.Parse(":\n  - [this is not valid");
        Assert.True(result.IsError);
    }
}
