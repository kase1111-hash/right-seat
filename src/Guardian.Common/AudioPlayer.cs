using System.Runtime.InteropServices;
using Serilog;

namespace Guardian.Common;

/// <summary>
/// Plays Guardian's alert tones and voice callouts.
///
/// Tones are synthesized in-memory as 16-bit PCM WAV (no asset files to
/// ship or lose) and played through winmm PlaySound on Windows. Voice
/// callouts use the Windows speech synthesizer via System.Speech. On
/// non-Windows platforms both are silent no-ops so replay tooling and
/// development environments run clean.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<AudioPlayer>();

    private readonly Dictionary<string, byte[]> _tones;
    private object? _synthesizer; // System.Speech.Synthesis.SpeechSynthesizer, Windows only

    public AudioPlayer()
    {
        _tones = new Dictionary<string, byte[]>
        {
            // Advisory: single soft mid chime
            ["advisory_tone"] = ChimeSynthesizer.Chime(new[] { (660.0, 0.18) }, amplitude: 0.35),
            // Warning: classic two-tone ding-dong
            ["warning_chime"] = ChimeSynthesizer.Chime(new[] { (880.0, 0.22), (660.0, 0.28) }, amplitude: 0.55),
            // Critical: urgent alternating triple beep
            ["critical_alarm"] = ChimeSynthesizer.Chime(
                new[] { (950.0, 0.16), (700.0, 0.16), (950.0, 0.16), (700.0, 0.16), (950.0, 0.24) },
                amplitude: 0.8),
        };
    }

    /// <summary>Plays a named tone asynchronously. Unknown ids are ignored.</summary>
    public void PlayTone(string toneId)
    {
        if (!_tones.TryGetValue(toneId, out var wav))
        {
            Log.Debug("Unknown tone id: {ToneId}", toneId);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                WinMm.PlaySound(wav, IntPtr.Zero,
                    WinMm.SND_MEMORY | WinMm.SND_ASYNC | WinMm.SND_NODEFAULT);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Tone playback failed");
            }
        }
        else
        {
            Log.Debug("Audio suppressed (non-Windows): {ToneId}", toneId);
        }
    }

    /// <summary>Speaks alert text via Windows TTS. No-op elsewhere.</summary>
    public void Speak(string text)
    {
        if (!OperatingSystem.IsWindows())
        {
            Log.Debug("TTS suppressed (non-Windows): {Text}", text);
            return;
        }

        try
        {
            SpeakWindows(text);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TTS failed");
        }
    }

    private void SpeakWindows(string text)
    {
        // System.Speech is Windows-only; keep the reference out of the
        // startup path so non-Windows never touches the type.
        _synthesizer ??= CreateSynthesizer();
        if (_synthesizer is System.Speech.Synthesis.SpeechSynthesizer synth)
        {
            synth.SpeakAsyncCancelAll();
            synth.SpeakAsync(text);
        }
    }

    private static object? CreateSynthesizer()
    {
        try
        {
            var synth = new System.Speech.Synthesis.SpeechSynthesizer();
            synth.SetOutputToDefaultAudioDevice();
            synth.Rate = 1; // slightly brisk, cockpit-callout style
            return synth;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Speech synthesizer unavailable — voice callouts disabled");
            return null;
        }
    }

    public void Dispose()
    {
        if (_synthesizer is IDisposable d)
            d.Dispose();
        _synthesizer = null;
    }

    private static class WinMm
    {
        public const uint SND_ASYNC = 0x0001;
        public const uint SND_NODEFAULT = 0x0002;
        public const uint SND_MEMORY = 0x0004;

        [DllImport("winmm.dll", SetLastError = true)]
        public static extern bool PlaySound(byte[] sound, IntPtr hmod, uint flags);
    }
}

/// <summary>
/// Synthesizes short chime sequences as complete in-memory WAV files
/// (16-bit PCM mono, 22.05 kHz) with attack/decay envelopes so tones
/// start and stop without clicks.
/// </summary>
public static class ChimeSynthesizer
{
    private const int SampleRate = 22050;

    /// <summary>
    /// Builds a WAV byte array from a sequence of (frequencyHz, durationSec)
    /// notes played back to back.
    /// </summary>
    public static byte[] Chime((double Frequency, double Duration)[] notes, double amplitude = 0.5)
    {
        int totalSamples = notes.Sum(n => (int)(n.Duration * SampleRate));
        var samples = new short[totalSamples];

        int offset = 0;
        foreach (var (frequency, duration) in notes)
        {
            int count = (int)(duration * SampleRate);
            int attack = Math.Min(count / 8, SampleRate / 100);   // ≤10ms
            int decay = Math.Min(count / 3, SampleRate / 8);      // ≤125ms

            for (int i = 0; i < count; i++)
            {
                double envelope = 1.0;
                if (i < attack) envelope = i / (double)attack;
                else if (i > count - decay) envelope = (count - i) / (double)decay;

                double value = Math.Sin(2 * Math.PI * frequency * i / SampleRate);
                samples[offset + i] = (short)(value * envelope * amplitude * short.MaxValue);
            }

            offset += count;
        }

        return WrapWav(samples);
    }

    private static byte[] WrapWav(short[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);                 // fmt chunk size
        w.Write((short)1);           // PCM
        w.Write((short)1);           // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);     // byte rate
        w.Write((short)2);           // block align
        w.Write((short)16);          // bits per sample
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples)
            w.Write(s);

        w.Flush();
        return ms.ToArray();
    }
}
