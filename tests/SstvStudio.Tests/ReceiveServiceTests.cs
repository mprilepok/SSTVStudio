// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;
using SstvStudio.Core.Decoding;
using SstvStudio.Core.Encoding;
using SstvStudio.Core.Imaging;
using SstvStudio.Core.Modes;
using SstvStudio.Services;

namespace SstvStudio.Tests;

public class ReceiveServiceTests
{
    /// <summary>Writes a WAV with 2 s of silence, one picture, and 1 s of silence.</summary>
    private static string MakeRecording(SstvMode mode, int sampleRate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sstv-test-{Guid.NewGuid():N}.wav");
        var options = new TransmitOptions(SendPreamble: true);
        var synth = new ToneSynthesizer(SstvEncoder.Encode(mode, RgbImage.Blank(mode.Width, mode.Height), options),
            SstvEncoder.DurationMs(mode, options), sampleRate, 0.5);
        using var writer = new WavFileWriter(File.Create(path), sampleRate);
        writer.Write(new float[sampleRate * 2]);
        var buffer = new float[4096];
        int n;
        while ((n = synth.Read(buffer)) > 0) writer.Write(buffer.AsSpan(0, n));
        writer.Write(new float[sampleRate]);
        return path;
    }

    [Fact]
    public async Task DeliversPictureAudioThatDecodesAgain()
    {
        const int rate = 12000;
        var mode = SstvModes.Robot36;
        var recording = MakeRecording(mode, rate);
        var clipPath = Path.ChangeExtension(recording, ".clip.wav");
        try
        {
            await using var service = new ReceiveService();
            var results = new List<(DecodedImage Image, ReceivedAudio? Audio)>();
            service.ImageCompleted += (img, audio) => results.Add((img, audio));

            await service.DecodeFileAsync(recording, DecodeOptions.Default);

            var (image, audio) = Assert.Single(results);
            Assert.Same(mode, image.Mode);
            Assert.NotNull(audio);
            Assert.Equal(rate, audio.SampleRate);
            // Pre-roll (~3 s, which covers preamble + VIS) plus the 36 s picture; not the whole 2 s lead-in + tail.
            var expected = SstvEncoder.DurationMs(mode, new TransmitOptions()) / 1000;
            Assert.InRange(audio.Duration.TotalSeconds, expected, expected + 3.5);

            // The saved clip must be decodable on its own.
            WavFileWriter.Write(clipPath, audio.Samples, audio.SampleRate);
            using var stream = File.OpenRead(clipPath);
            var (samples, clipRate) = WavReader.Read(stream);
            var decoder = new SstvDecoder(clipRate);
            DecodedImage? again = null;
            decoder.ImageCompleted += r => again = r;
            decoder.Process(samples);
            decoder.Process(new float[clipRate]);
            Assert.NotNull(again);
            Assert.Same(mode, again.Mode);
            Assert.True(again.Complete);
        }
        finally
        {
            File.Delete(recording);
            File.Delete(clipPath);
        }
    }
}
