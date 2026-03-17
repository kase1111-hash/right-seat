using Guardian.Common;
using Guardian.Core;
using Serilog;

namespace Guardian.Priority;

/// <summary>
/// Stub audio alert service. Provides the interface for audio feedback
/// without actual audio playback (will be implemented in the desktop app phase).
///
/// - CRITICAL: repeating alarm tone at configurable interval
/// - WARNING: single chime
/// - ADVISORY: subtle notification sound (optional)
/// - Respects config.AudioEnabled
/// </summary>
public sealed class AudioAlertService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<AudioAlertService>();

    private readonly GuardianConfig _config;
    private DateTime _lastCriticalTone = DateTime.MinValue;

    /// <summary>Raised when an audio tone should be played. String = tone identifier.</summary>
    public event Action<string, AlertSeverity>? OnPlayTone;

    /// <summary>Raised when TTS text should be spoken.</summary>
    public event Action<string>? OnSpeak;

    public AudioAlertService(GuardianConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Plays the appropriate audio tone for an alert severity.
    /// </summary>
    public void PlayAlert(Alert alert, DateTime now)
    {
        if (!_config.AudioEnabled) return;

        switch (alert.Severity)
        {
            case AlertSeverity.Critical:
                // Repeating alarm — check interval
                if ((now - _lastCriticalTone).TotalSeconds >= _config.CriticalRepeatIntervalSec)
                {
                    _lastCriticalTone = now;
                    Log.Information("Audio: CRITICAL alarm for {RuleId}", alert.RuleId);
                    OnPlayTone?.Invoke("critical_alarm", AlertSeverity.Critical);
                }
                break;

            case AlertSeverity.Warning:
                Log.Information("Audio: WARNING chime for {RuleId}", alert.RuleId);
                OnPlayTone?.Invoke("warning_chime", AlertSeverity.Warning);
                break;

            case AlertSeverity.Advisory:
                Log.Debug("Audio: advisory tone for {RuleId}", alert.RuleId);
                OnPlayTone?.Invoke("advisory_tone", AlertSeverity.Advisory);
                break;
        }

        // TTS
        if (_config.TtsEnabled && alert.Severity >= AlertSeverity.Warning)
        {
            var text = alert.FormatText();
            Log.Debug("TTS: {Text}", text);
            OnSpeak?.Invoke(text);
        }
    }

    /// <summary>Silences the current critical alarm (pilot acknowledgement).</summary>
    public void SilenceCriticalAlarm()
    {
        _lastCriticalTone = DateTime.MaxValue; // Prevent further repeats until next critical
        Log.Information("Critical alarm silenced by pilot");
    }

    /// <summary>Resets the critical alarm timer (for new critical alerts).</summary>
    public void ResetCriticalAlarm()
    {
        _lastCriticalTone = DateTime.MinValue;
    }
}
