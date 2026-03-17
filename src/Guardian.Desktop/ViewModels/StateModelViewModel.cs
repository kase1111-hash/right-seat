using CommunityToolkit.Mvvm.ComponentModel;
using Guardian.Core;

namespace Guardian.Desktop.ViewModels;

/// <summary>
/// ViewModel for the state model diagnostic panel.
/// Shows current flight phase, aircraft profile, and configuration state.
/// </summary>
public partial class StateModelViewModel : ObservableObject
{
    [ObservableProperty] private string _currentPhase = "Ground";
    [ObservableProperty] private string _phaseClass = "value-normal";
    [ObservableProperty] private string _aircraftId = "---";
    [ObservableProperty] private string _aircraftName = "No aircraft detected";
    [ObservableProperty] private string _engineType = "---";
    [ObservableProperty] private int _engineCount;
    [ObservableProperty] private string _fuelConfig = "---";

    public void UpdatePhase(FlightPhase phase)
    {
        CurrentPhase = phase.ToString();
        PhaseClass = phase.IsSterile() ? "value-caution" : "value-normal";
    }

    public void UpdateProfile(AircraftProfile profile)
    {
        AircraftId = profile.AircraftId;
        AircraftName = profile.DisplayName;
        EngineType = profile.EngineType;
        EngineCount = profile.EngineCount;
        FuelConfig = $"{profile.Fuel.TankCount} tanks, {profile.Fuel.TotalCapacityGal:F0} gal capacity";
    }
}
