using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Guardian.Desktop.Services;
using Guardian.Detection;

namespace Guardian.Desktop.ViewModels;

/// <summary>
/// ViewModel for the rule status diagnostic panel.
/// Shows all registered rules with their current state.
/// </summary>
public partial class RuleStatusViewModel : ObservableObject
{
    private readonly GuardianEngineService _engine;

    public ObservableCollection<RuleEntryViewModel> Rules { get; } = new();

    [ObservableProperty] private int _enabledCount;
    [ObservableProperty] private int _disabledCount;
    [ObservableProperty] private int _triggeredCount;

    public RuleStatusViewModel(GuardianEngineService engine)
    {
        _engine = engine;
        RefreshRuleList();
    }

    public void RefreshRuleList()
    {
        Rules.Clear();
        foreach (var (ruleId, name, state) in _engine.GetRuleStates())
        {
            Rules.Add(new RuleEntryViewModel
            {
                RuleId = ruleId,
                Name = name,
                State = state,
                StateDisplay = FormatState(state),
                StateClass = GetStateClass(state),
            });
        }
        UpdateCounts();
    }

    public void UpdateRuleState(string ruleId, RuleState newState)
    {
        var entry = Rules.FirstOrDefault(r => r.RuleId == ruleId);
        if (entry is not null)
        {
            entry.State = newState;
            entry.StateDisplay = FormatState(newState);
            entry.StateClass = GetStateClass(newState);
        }
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        EnabledCount = Rules.Count(r => r.State == RuleState.Enabled);
        DisabledCount = Rules.Count(r => r.State != RuleState.Enabled);
    }

    private static string FormatState(RuleState state) => state switch
    {
        RuleState.Enabled => "Active",
        RuleState.DisabledByConfig => "Disabled (config)",
        RuleState.DisabledCrashed => "Disabled (error)",
        RuleState.DisabledMissingSimVars => "Disabled (missing data)",
        _ => state.ToString(),
    };

    private static string GetStateClass(RuleState state) => state switch
    {
        RuleState.Enabled => "value-normal",
        RuleState.DisabledCrashed => "value-danger",
        _ => "value-caution",
    };
}

public partial class RuleEntryViewModel : ObservableObject
{
    [ObservableProperty] private string _ruleId = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private RuleState _state;
    [ObservableProperty] private string _stateDisplay = "";
    [ObservableProperty] private string _stateClass = "";
}
