using Guardian.Core;
using Xunit;

namespace Guardian.Core.Tests;

public class AlertTests
{
    [Fact]
    public void FormatText_NoTemplate_ReturnsKeyWithParams()
    {
        var alert = new Alert
        {
            RuleId = "R001",
            Severity = AlertSeverity.Warning,
            TextKey = "R001_BOTH_ENGINES_SAME_TANK",
            TextParameters = new Dictionary<string, string>
            {
                ["tank"] = "left",
                ["time_min"] = "47",
                ["unused_gal"] = "18",
            },
        };

        var text = alert.FormatText();

        Assert.Contains("R001_BOTH_ENGINES_SAME_TANK", text);
        Assert.Contains("tank=left", text);
        Assert.Contains("time_min=47", text);
    }

    [Fact]
    public void FormatText_WithTemplate_SubstitutesParameters()
    {
        var alert = new Alert
        {
            RuleId = "R001",
            Severity = AlertSeverity.Warning,
            TextKey = "R001_BOTH_ENGINES_SAME_TANK",
            TextParameters = new Dictionary<string, string>
            {
                ["tank"] = "left",
                ["time_min"] = "47",
                ["unused_gal"] = "18",
            },
        };

        string? TemplateLookup(string key) => key switch
        {
            "R001_BOTH_ENGINES_SAME_TANK" =>
                "Both engines drawing from {tank} tank. {unused_gal} gal unused. Est {time_min} min remaining.",
            _ => null,
        };

        var text = alert.FormatText(TemplateLookup);

        Assert.Equal("Both engines drawing from left tank. 18 gal unused. Est 47 min remaining.", text);
    }

    [Fact]
    public void FormatText_NoParams_ReturnsKeyOnly()
    {
        var alert = new Alert
        {
            RuleId = "R008",
            Severity = AlertSeverity.Warning,
            TextKey = "R008_VACUUM_LOW",
        };

        Assert.Equal("R008_VACUUM_LOW", alert.FormatText());
    }

    [Fact]
    public void ToString_IncludesSeverityAndRuleId()
    {
        var alert = new Alert
        {
            RuleId = "R003",
            Severity = AlertSeverity.Critical,
            TextKey = "R003_CHT_REDLINE",
        };

        var str = alert.ToString();

        Assert.Contains("[Critical]", str);
        Assert.Contains("R003", str);
    }
}
