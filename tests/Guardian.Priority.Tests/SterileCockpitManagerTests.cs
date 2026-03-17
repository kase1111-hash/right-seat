using Guardian.Common;
using Guardian.Core;
using Guardian.Priority;
using Xunit;

namespace Guardian.Priority.Tests;

public class SterileCockpitManagerTests
{
    private static GuardianConfig DefaultConfig => new() { SterileCockpitEnabled = true };

    [Fact]
    public void TakeoffPhase_ActivatesSterile()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);

        mgr.Update(FlightPhase.Takeoff);

        Assert.True(mgr.IsSterile);
    }

    [Fact]
    public void CruisePhase_NotSterile()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);

        mgr.Update(FlightPhase.Cruise);

        Assert.False(mgr.IsSterile);
    }

    [Fact]
    public void ApproachPhase_ActivatesSterile()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);

        mgr.Update(FlightPhase.Approach);

        Assert.True(mgr.IsSterile);
    }

    [Fact]
    public void LandingPhase_ActivatesSterile()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);

        mgr.Update(FlightPhase.Landing);

        Assert.True(mgr.IsSterile);
    }

    [Fact]
    public void SterileToNonSterile_ReturnsTrue()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);

        mgr.Update(FlightPhase.Takeoff);
        Assert.True(mgr.IsSterile);

        bool justExited = mgr.Update(FlightPhase.Climb);
        Assert.True(justExited);
        Assert.False(mgr.IsSterile);
    }

    [Fact]
    public void NonSterileToNonSterile_ReturnsFalse()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);

        mgr.Update(FlightPhase.Cruise);
        bool justExited = mgr.Update(FlightPhase.Climb);

        Assert.False(justExited);
    }

    [Fact]
    public void ShouldSuppress_CriticalNeverSuppressed()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);
        mgr.Update(FlightPhase.Takeoff);

        var critical = new Alert
        {
            RuleId = "R001",
            Severity = AlertSeverity.Critical,
            TextKey = "TEST",
        };

        Assert.False(mgr.ShouldSuppress(critical));
    }

    [Fact]
    public void ShouldSuppress_WarningSuppressedDuringSterile()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);
        mgr.Update(FlightPhase.Takeoff);

        var warning = new Alert
        {
            RuleId = "R001",
            Severity = AlertSeverity.Warning,
            TextKey = "TEST",
        };

        Assert.True(mgr.ShouldSuppress(warning));
    }

    [Fact]
    public void ShouldSuppress_WarningNotSuppressedOutsideSterile()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);
        mgr.Update(FlightPhase.Cruise);

        var warning = new Alert
        {
            RuleId = "R001",
            Severity = AlertSeverity.Warning,
            TextKey = "TEST",
        };

        Assert.False(mgr.ShouldSuppress(warning));
    }

    [Fact]
    public void Disabled_NeverSterile()
    {
        var config = new GuardianConfig { SterileCockpitEnabled = false };
        var mgr = new SterileCockpitManager(config);

        mgr.Update(FlightPhase.Takeoff);

        Assert.False(mgr.IsSterile);
    }

    [Fact]
    public void ManualOverride_ActivatesSterile()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);

        mgr.SetManualOverride(true);
        mgr.Update(FlightPhase.Cruise);

        Assert.True(mgr.IsSterile);
    }

    [Fact]
    public void ManualOverride_Toggle()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);

        mgr.ToggleManual();
        mgr.Update(FlightPhase.Cruise);
        Assert.True(mgr.IsSterile);

        mgr.ToggleManual();
        mgr.Update(FlightPhase.Cruise);
        Assert.False(mgr.IsSterile);
    }

    [Fact]
    public void OnSterileStateChanged_Fires()
    {
        var mgr = new SterileCockpitManager(DefaultConfig);
        var events = new List<bool>();
        mgr.OnSterileStateChanged += state => events.Add(state);

        mgr.Update(FlightPhase.Takeoff);
        mgr.Update(FlightPhase.Climb);

        Assert.Equal(2, events.Count);
        Assert.True(events[0]);  // activated
        Assert.False(events[1]); // deactivated
    }
}
