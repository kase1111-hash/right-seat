using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guardian.Common;
using Guardian.Core;
using Guardian.Desktop.Services;
using Guardian.Detection;
using Guardian.Priority;

namespace Guardian.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly GuardianEngineService _engine;

    public TelemetryPanelViewModel Telemetry { get; }
    public AlertFeedViewModel AlertFeed { get; }
    public RuleStatusViewModel RuleStatus { get; }
    public StateModelViewModel StateModel { get; }

    [ObservableProperty] private string _connectionState = "Disconnected";
    [ObservableProperty] private string _connectionStateClass = "status-disconnected";
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isSterileCockpit;
    [ObservableProperty] private string _windowTitle = "Flight Guardian";

    public MainWindowViewModel(GuardianEngineService engine, GuardianConfig config)
    {
        _engine = engine;

        Telemetry = new TelemetryPanelViewModel(engine);
        AlertFeed = new AlertFeedViewModel();
        RuleStatus = new RuleStatusViewModel(engine);
        StateModel = new StateModelViewModel();

        // Wire engine events → UI (marshalled to UI thread)
        engine.OnConnectionStateChanged += state =>
            Dispatcher.UIThread.Post(() => UpdateConnectionState(state));

        engine.OnPhaseChanged += (old, @new) =>
            Dispatcher.UIThread.Post(() => StateModel.UpdatePhase(@new));

        engine.OnAlertDelivered += delivered =>
            Dispatcher.UIThread.Post(() => AlertFeed.AddDeliveredAlert(delivered));

        engine.OnInfoLogged += info =>
            Dispatcher.UIThread.Post(() => AlertFeed.AddInfoAlert(info));

        engine.OnRuleStateChanged += (ruleId, state) =>
            Dispatcher.UIThread.Post(() => RuleStatus.UpdateRuleState(ruleId, state));

        engine.OnProfileMatched += profile =>
            Dispatcher.UIThread.Post(() => StateModel.UpdateProfile(profile));

        engine.OnTelemetryUpdated += snapshot =>
            Dispatcher.UIThread.Post(() => Telemetry.UpdateFromSnapshot(snapshot));

        engine.Pipeline.SterileCockpit.OnSterileStateChanged += sterile =>
            Dispatcher.UIThread.Post(() => IsSterileCockpit = sterile);
    }

    private void UpdateConnectionState(string state)
    {
        ConnectionState = state;
        ConnectionStateClass = state switch
        {
            "Connected" => "status-connected",
            "Connecting" => "status-connecting",
            _ => "status-disconnected",
        };
        WindowTitle = state == "Connected"
            ? $"Flight Guardian — {_engine.ActiveProfile?.DisplayName ?? "Unknown Aircraft"}"
            : "Flight Guardian — Disconnected";
    }

    [RelayCommand]
    private void ToggleSterileCockpit()
    {
        _engine.Pipeline.SterileCockpit.ToggleManual();
    }

    [RelayCommand]
    private void ToggleRecording()
    {
        if (_engine.IsRecording)
        {
            _engine.StopRecording();
            IsRecording = false;
        }
        else
        {
            var path = Path.Combine(
                _engine.Config.OutputDirectory,
                $"recording_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _engine.StartRecording(path);
            IsRecording = true;
        }
    }

    [RelayCommand]
    private void SilenceCriticalAlarm()
    {
        _engine.Pipeline.Audio.SilenceCriticalAlarm();
    }

    [RelayCommand]
    private void ClearAlertHistory()
    {
        AlertFeed.Clear();
    }
}
