// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;
using SstvStudio.Core.Decoding;
using SstvStudio.Core.Encoding;
using SstvStudio.Core.Imaging;
using SstvStudio.Core.Modes;

namespace SstvStudio.Tests;

public class ReceptionAidTests
{
    private static RgbImage TestImage(SstvMode mode)
    {
        var img = new RgbImage(mode.Width, mode.Height);
        for (var y = 0; y < mode.Height; y++)
        for (var x = 0; x < mode.Width; x++)
            img[x, y] = ((byte)(255 * x / mode.Width), (byte)(255 * y / mode.Height), (byte)(255 - 255 * x / mode.Width));
        return img;
    }

    /// <summary>1 s noise, the transmission (optionally without VIS, optionally retuned), 1 s noise.</summary>
    private static float[] Audio(SstvMode mode, int rate, double offsetHz = 0, bool withHeader = true, double noise = 0.01, int seed = 2)
    {
        var options = new TransmitOptions(SendPreamble: withHeader);
        IEnumerable<Tone> tones = withHeader
            ? SstvEncoder.Encode(mode, TestImage(mode), options)
            : mode.EncodeImage(TestImage(mode));
        var duration = withHeader ? SstvEncoder.DurationMs(mode, options) : mode.ImageDurationMs;
        tones = tones.Select(t => t.FrequencyHz > 0 ? t with { FrequencyHz = t.FrequencyHz + offsetHz } : t);
        var synth = new ToneSynthesizer(tones, duration, rate, 0.5);

        var rng = new Random(seed);
        var audio = new float[(int)((duration / 1000 + 2) * rate)];
        synth.Read(audio.AsSpan(rate));
        for (var i = 0; i < audio.Length; i++) audio[i] += (float)((rng.NextDouble() * 2 - 1) * noise);
        return audio;
    }

    private static List<DecodedImage> Decode(SstvDecoder decoder, float[] audio)
    {
        var results = new List<DecodedImage>();
        decoder.ImageCompleted += results.Add;
        for (var i = 0; i < audio.Length; i += 4096)
            decoder.Process(audio.AsSpan(i, Math.Min(4096, audio.Length - i)));
        decoder.Stop();
        return results;
    }

    private static double MeanError(RgbImage a, RgbImage b, int shiftX = 0)
    {
        double sum = 0;
        long n = 0;
        for (var y = 2; y < a.Height - 2; y++)
        for (var x = 12; x < a.Width - 12; x++)
        {
            var (r1, g1, b1) = a[x - shiftX, y];
            var (r2, g2, b2) = b[x, y];
            sum += Math.Abs(r1 - r2) + Math.Abs(g1 - g2) + Math.Abs(b1 - b2);
            n += 3;
        }
        return sum / n;
    }

    [Theory]
    [InlineData("Robot 36", 200)]
    [InlineData("Robot 36", -200)]
    [InlineData("Martin M1", 150)]
    [InlineData("Scottie S1", -220)]
    public void DecodesMistunedSignalAndReportsOffset(string modeName, int offsetHz)
    {
        var mode = SstvModes.Find(modeName)!;

        var result = Assert.Single(Decode(new SstvDecoder(12000), Audio(mode, 12000, offsetHz)));

        Assert.Same(mode, result.Mode);
        Assert.True(result.Complete);
        Assert.InRange(result.TuningOffsetHz, offsetHz - 15, offsetHz + 15);
        Assert.InRange(MeanError(TestImage(mode), result.Image), 0, 8);
    }

    [Fact]
    public void ManualStartDecodesPictureWithoutHeader()
    {
        var mode = SstvModes.MartinM1;
        var decoder = new SstvDecoder(12000);
        decoder.StartManual(mode); // before the 1 s of noise: it must wait for the first pulse

        var result = Assert.Single(Decode(decoder, Audio(mode, 12000, withHeader: false)));

        Assert.True(result.Complete);
        Assert.InRange(MeanError(TestImage(mode), result.Image), 0, 8);
    }

    [Fact]
    public void ManualStartRecoversPicturesTooWeakForHeaderDetection()
    {
        // At this level the header is rarely detected (measured: 1 of 6), but the picture is still there.
        var mode = SstvModes.Robot36;
        int viaHeader = 0, viaManual = 0;
        for (var seed = 1; seed <= 6; seed++)
        {
            var audio = Audio(mode, 48000, noise: 1.5, seed: seed);
            if (Decode(new SstvDecoder(48000), audio) is [{ Complete: true }]) viaHeader++;

            var decoder = new SstvDecoder(48000);
            decoder.StartManual(mode);
            if (Decode(decoder, audio) is [{ Complete: true }]) viaManual++;
        }

        // Recovery at this level is statistical (measured 5–6 of 6), the header path 0–1 of 6.
        Assert.InRange(viaHeader, 0, 2);
        Assert.InRange(viaManual, 5, 6);
    }

    [Fact]
    public void ManualStartOnNoiseGivesUp()
    {
        var decoder = new SstvDecoder(12000);
        var aborted = false;
        decoder.ImageAborted += () => aborted = true;
        decoder.StartManual(SstvModes.Robot36);
        var rng = new Random(9);
        var noise = new float[12000 * 10];
        for (var i = 0; i < noise.Length; i++) noise[i] = (float)(rng.NextDouble() * 2 - 1) * 0.3f;
        decoder.Process(noise);

        Assert.True(aborted);
        Assert.Equal(DecoderState.Searching, decoder.State);
    }

    [Fact]
    public void ShiftMovesPictureRight()
    {
        var mode = SstvModes.MartinM1;
        var pixelMs = 146.432 / mode.Width;
        var decoder = new SstvDecoder(12000) { ShiftMs = 10 * pixelMs };

        var result = Assert.Single(Decode(decoder, Audio(mode, 12000)));

        Assert.InRange(MeanError(TestImage(mode), result.Image, shiftX: 10), 0, 8);
        Assert.True(MeanError(TestImage(mode), result.Image) > 5); // and it really did move
    }

    [Fact]
    public void SpectrumPeaksAtToneFrequency()
    {
        var analyzer = new SpectrumAnalyzer(48000);
        var synth = new ToneSynthesizer([new Tone(1900, 500)], 500, 48000, 0.5);
        var audio = new float[24000];
        synth.Read(audio);
        analyzer.Push(audio);

        float[]? last = null;
        while (analyzer.TryTakeRow() is { } row) last = row;
        Assert.NotNull(last);
        var peak = Array.IndexOf(last, last.Max());
        Assert.InRange(SpectrumAnalyzer.ColumnHz(peak), 1885, 1915);
    }

    [Fact]
    public void NoiseFilterReducesErrorOnWeakSignal()
    {
        var mode = SstvModes.MartinM1;
        var result = Assert.Single(Decode(new SstvDecoder(48000), Audio(mode, 48000, noise: 0.9)));
        var original = TestImage(mode);

        var raw = MeanError(original, result.Image);
        var low = MeanError(original, ImageFilters.Denoise(result.Image, NoiseFilter.Low));
        var high = MeanError(original, ImageFilters.Denoise(result.Image, NoiseFilter.High));

        Assert.True(low < raw * 0.8, $"low {low:0.0} vs raw {raw:0.0}");
        Assert.True(high < low, $"high {high:0.0} vs low {low:0.0}");
    }
}
