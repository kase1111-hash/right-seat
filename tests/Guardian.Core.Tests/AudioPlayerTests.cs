using Guardian.Common;
using Xunit;

namespace Guardian.Core.Tests;

public class AudioPlayerTests
{
    [Fact]
    public void ChimeSynthesizer_ProducesValidWavHeader()
    {
        var wav = ChimeSynthesizer.Chime(new[] { (880.0, 0.2), (660.0, 0.2) });

        // RIFF/WAVE container
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", System.Text.Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));

        // RIFF size field = file length - 8
        int riffSize = BitConverter.ToInt32(wav, 4);
        Assert.Equal(wav.Length - 8, riffSize);

        // data chunk size = 0.4s * 22050 Hz * 2 bytes
        int dataSize = BitConverter.ToInt32(wav, 40);
        Assert.Equal(wav.Length - 44, dataSize);
        Assert.Equal((int)(0.4 * 22050) * 2, dataSize);
    }

    [Fact]
    public void ChimeSynthesizer_EnvelopeStartsAndEndsNearZero()
    {
        var wav = ChimeSynthesizer.Chime(new[] { (700.0, 0.2) }, amplitude: 0.8);

        short firstSample = BitConverter.ToInt16(wav, 44);
        short lastSample = BitConverter.ToInt16(wav, wav.Length - 2);

        // Attack/decay envelope prevents clicks
        Assert.InRange(Math.Abs((int)firstSample), 0, 500);
        Assert.InRange(Math.Abs((int)lastSample), 0, 500);
    }

    [Fact]
    public void AudioPlayer_PlayToneAndSpeak_DoNotThrowOnAnyPlatform()
    {
        using var player = new AudioPlayer();
        player.PlayTone("warning_chime");
        player.PlayTone("critical_alarm");
        player.PlayTone("advisory_tone");
        player.PlayTone("nonexistent_tone");
        player.Speak("Fuel imbalance detected");
    }
}
