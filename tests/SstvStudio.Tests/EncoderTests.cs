// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;
using SstvStudio.Core.Encoding;
using SstvStudio.Core.Imaging;
using SstvStudio.Core.Modes;

namespace SstvStudio.Tests;

public class EncoderTests
{
    public static TheoryData<string, double> ModeDurations => new()
    {
        { "Martin M1", 114.290 },
        { "Martin M2", 58.060 },
        { "Scottie S1", 109.633 },
        { "Scottie S2", 71.098 },
        { "Scottie DX", 268.886 },
        { "Robot 36", 36.000 },
        { "Robot 72", 72.000 },
        { "PD 50", 49.684 },
        { "PD 90", 89.989 },
        { "PD 120", 126.103 },
        { "PD 160", 160.883 },
        { "PD 180", 187.051 },
        { "PD 240", 248.000 },
        { "PD 290", 288.682 },
        { "Wraase SC2-180", 182.022 },
    };

    [Theory]
    [MemberData(nameof(ModeDurations))]
    public void ImageDuration_MatchesSpecification(string modeName, double expectedSeconds)
    {
        var mode = SstvModes.Find(modeName)!;
        Assert.Equal(expectedSeconds, mode.ImageDurationMs / 1000, 2);
    }

    [Fact]
    public void AllModes_HaveUniqueVisCodes()
    {
        Assert.Equal(SstvModes.All.Count, SstvModes.All.Select(m => m.VisCode).Distinct().Count());
    }

    [Theory]
    [InlineData(44, new[] { 0, 0, 1, 1, 0, 1, 0 }, 1)] // Martin M1: 0x2C, three ones -> odd -> parity 1
    [InlineData(8, new[] { 0, 0, 0, 1, 0, 0, 0 }, 1)]  // Robot 36
    [InlineData(60, new[] { 0, 0, 1, 1, 1, 1, 0 }, 0)] // Scottie S1: four ones -> parity 0
    public void VisHeader_EncodesBitsLsbFirstWithEvenParity(byte vis, int[] bits, int parity)
    {
        var tones = SstvEncoder.VisHeader(vis).ToList();
        Assert.Equal(13, tones.Count);
        Assert.Equal(910, tones.Sum(t => t.DurationMs), 6);
        Assert.Equal(new Tone(1900, 300), tones[0]);
        Assert.Equal(new Tone(1200, 10), tones[1]);
        Assert.Equal(new Tone(1200, 30), tones[3]);
        for (var i = 0; i < 7; i++)
            Assert.Equal(bits[i] == 1 ? 1100 : 1300, tones[4 + i].FrequencyHz);
        Assert.Equal(parity == 1 ? 1100 : 1300, tones[11].FrequencyHz);
        Assert.Equal(new Tone(1200, 30), tones[^1]);
    }

    [Fact]
    public void Encode_RejectsWrongImageSize()
    {
        Assert.Throws<ArgumentException>(() =>
            SstvEncoder.Encode(SstvModes.MartinM1, RgbImage.Blank(320, 240), new TransmitOptions()));
    }

    [Fact]
    public void ColorSpace_MapsWhiteAndBlackToVideoRange()
    {
        Assert.Equal(235, ColorSpace.Y(255, 255, 255));
        Assert.Equal(16, ColorSpace.Y(0, 0, 0));
        Assert.Equal(128, ColorSpace.RY(128, 128, 128));
        Assert.Equal(128, ColorSpace.BY(128, 128, 128));
    }

    [Fact]
    public void Synthesizer_ProducesExactSampleCountWithoutDrift()
    {
        var mode = SstvModes.Robot36;
        var options = new TransmitOptions(SendPreamble: true, CwId: "OM6XX");
        var totalMs = SstvEncoder.DurationMs(mode, options);
        var synth = new ToneSynthesizer(SstvEncoder.Encode(mode, RgbImage.Blank(320, 240), options), totalMs, 48000);

        var buffer = new float[4096];
        long count = 0;
        int read;
        while ((read = synth.Read(buffer)) > 0) count += read;

        Assert.True(synth.IsFinished);
        Assert.InRange(count, synth.TotalSamples - 1, synth.TotalSamples);
        Assert.Equal(totalMs * 48, count, 2.0);
    }

    [Fact]
    public void Synthesizer_ClockCorrectionStretchesOutput()
    {
        var tones = new[] { new Tone(1500, 1000) };
        var synth = new ToneSynthesizer(tones, 1000, 48000, clockPpm: 1000);
        var buffer = new float[100_000];
        Assert.Equal(48048, synth.Read(buffer));
    }

    [Fact]
    public void Synthesizer_ToneHasExpectedFrequency()
    {
        // 1200 Hz at 48 kHz: 40 samples per cycle -> 75 rising zero crossings in 3000 samples.
        var synth = new ToneSynthesizer([new Tone(1200, 100)], 100, 48000, amplitude: 1);
        var buffer = new float[4800];
        synth.Read(buffer);
        var crossings = 0;
        for (var i = 1000; i < 4000; i++)
            if (buffer[i - 1] < 0 && buffer[i] >= 0) crossings++;
        Assert.InRange(crossings, 74, 76);
    }

    [Fact]
    public void Morse_UsesParisTiming()
    {
        // "E" = one dot, at 20 wpm = 60 ms.
        var tones = Morse.Encode("E", 20, 800).ToList();
        Assert.Single(tones);
        Assert.Equal(60, tones[0].DurationMs, 6);
        // "PARIS " is 50 units; without trailing word gap it is 43 units.
        Assert.Equal(43 * 60, Morse.Encode("PARIS", 20, 800).Sum(t => t.DurationMs), 6);
    }

    [Fact]
    public async Task WavWriter_WritesValidHeader()
    {
        var synth = new ToneSynthesizer([new Tone(1900, 100)], 100, 8000);
        using var ms = new MemoryStream();
        await WavWriter.WriteAsync(synth, ms);
        var bytes = ms.ToArray();
        Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
        Assert.Equal(44 + 800 * 2, bytes.Length);
        Assert.Equal(8000, BitConverter.ToInt32(bytes, 24));
    }
}
