// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;
using SstvStudio.Core.Decoding;
using SstvStudio.Core.Encoding;
using SstvStudio.Core.Imaging;
using SstvStudio.Core.Modes;

namespace SstvStudio.Tests;

public class DecoderTests
{
    /// <summary>Smooth test picture (gradients) so chroma subsampling does not dominate the error.</summary>
    private static RgbImage TestImage(SstvMode mode)
    {
        var img = new RgbImage(mode.Width, mode.Height);
        for (var y = 0; y < mode.Height; y++)
        for (var x = 0; x < mode.Width; x++)
            img[x, y] = ((byte)(255 * x / mode.Width), (byte)(255 * y / mode.Height), (byte)(255 - 255 * x / mode.Width));
        return img;
    }

    private static List<DecodedImage> RoundTrip(SstvMode mode, RgbImage image, int sampleRate,
        double clockPpm = 0, double noise = 0, int seed = 1)
    {
        var options = new TransmitOptions(SendPreamble: true);
        var synth = new ToneSynthesizer(SstvEncoder.Encode(mode, image, options),
            SstvEncoder.DurationMs(mode, options), sampleRate, 0.5, clockPpm);

        var rng = new Random(seed);
        var decoder = new SstvDecoder(sampleRate);
        var results = new List<DecodedImage>();
        decoder.ImageCompleted += results.Add;

        var buffer = new float[4096];
        void Feed(int count, bool signal)
        {
            var n = signal ? synth.Read(buffer.AsSpan(0, count)) : count;
            if (!signal) Array.Clear(buffer, 0, n);
            for (var i = 0; i < n; i++) buffer[i] += (float)((rng.NextDouble() * 2 - 1) * noise);
            decoder.Process(buffer.AsSpan(0, n));
        }

        for (var i = 0; i < sampleRate / 4096; i++) Feed(4096, signal: false); // ~1 s of noise first
        while (!synth.IsFinished) Feed(4096, signal: true);
        for (var i = 0; i < sampleRate / 4096; i++) Feed(4096, signal: false);
        return results;
    }

    /// <summary>Mean absolute per-channel error, ignoring a 2-pixel border.</summary>
    private static double MeanError(RgbImage expected, RgbImage actual)
    {
        double sum = 0;
        long n = 0;
        for (var y = 2; y < expected.Height - 2; y++)
        for (var x = 2; x < expected.Width - 2; x++)
        {
            var (r1, g1, b1) = expected[x, y];
            var (r2, g2, b2) = actual[x, y];
            sum += Math.Abs(r1 - r2) + Math.Abs(g1 - g2) + Math.Abs(b1 - b2);
            n += 3;
        }
        return sum / n;
    }

    public static TheoryData<string> AllModes => [.. SstvModes.All.Select(m => m.Name)];

    [Theory]
    [MemberData(nameof(AllModes))]
    public void DecodesEveryMode(string modeName)
    {
        var mode = SstvModes.Find(modeName)!;
        var image = TestImage(mode);

        var result = Assert.Single(RoundTrip(mode, image, sampleRate: 12000));

        Assert.Same(mode, result.Mode);
        Assert.True(result.Complete);
        Assert.InRange(MeanError(image, result.Image), 0, 6);
    }

    [Fact]
    public void CorrectsSlantFromClockError()
    {
        var mode = SstvModes.MartinM1;
        var image = TestImage(mode);

        // 600 ppm is ~70 ms (≈50 pixels) of drift over the picture if uncorrected.
        var result = Assert.Single(RoundTrip(mode, image, sampleRate: 12000, clockPpm: 600));

        Assert.InRange(MeanError(image, result.Image), 0, 6);
    }

    /// <summary>
    /// Regression (real off-air Martin M1 with lines of 450.8 ms instead of 446.4 ms): the sender's sound card
    /// can be off by a percent or more. The decoder must track that from the first lines on, not lose sync.
    /// </summary>
    [Theory]
    [InlineData("Martin M1", 9800)]
    [InlineData("Martin M1", -9800)]
    [InlineData("Martin M1", 15000)]
    [InlineData("Robot 36", 15000)]
    [InlineData("Robot 36", -15000)]
    [InlineData("Scottie S1", -12000)]
    [InlineData("PD 120", 12000)]
    public void TracksLargeSenderClockError(string modeName, int ppm)
    {
        var mode = SstvModes.Find(modeName)!;
        var image = TestImage(mode);

        var result = Assert.Single(RoundTrip(mode, image, sampleRate: 12000, clockPpm: ppm));

        Assert.True(result.Complete);
        Assert.InRange(MeanError(image, result.Image), 0, 8);
    }

    [Fact]
    public void DecodesNoisySignalAt48kHz()
    {
        var mode = SstvModes.Robot36;
        var image = TestImage(mode);

        // Signal amplitude 0.5 vs uniform noise ±0.5: ≈ 12 dB SNR in a 2.5 kHz SSB channel. The picture is
        // grainy (mean error ~30/255) but must still be found, synced and decoded completely.
        var result = Assert.Single(RoundTrip(mode, image, sampleRate: 48000, noise: 0.5));

        Assert.True(result.Complete);
        Assert.InRange(MeanError(image, result.Image), 0, 35);
    }

    /// <summary>Audio of one transmission at 12 kHz, optionally cut off after <paramref name="fraction"/> of it.</summary>
    private static float[] Transmission(SstvMode mode, double fraction = 1)
    {
        var options = new TransmitOptions(SendPreamble: true);
        var total = SstvEncoder.DurationMs(mode, options);
        var synth = new ToneSynthesizer(SstvEncoder.Encode(mode, TestImage(mode), options), total, 12000, 0.5);
        var samples = new float[(int)(total * 12 * fraction)];
        synth.Read(samples);
        return samples;
    }

    private static float[] Noise(double seconds, double level, int seed = 3)
    {
        var rng = new Random(seed);
        var samples = new float[(int)(seconds * 12000)];
        for (var i = 0; i < samples.Length; i++) samples[i] = (float)((rng.NextDouble() * 2 - 1) * level);
        return samples;
    }

    private static List<DecodedImage> DecodeStream(params float[][] parts)
    {
        var decoder = new SstvDecoder(12000);
        var results = new List<DecodedImage>();
        decoder.ImageCompleted += results.Add;
        foreach (var part in parts.Append(Noise(2, 0.01)))
            for (var i = 0; i < part.Length; i += 4096)
                decoder.Process(part.AsSpan(i, Math.Min(4096, part.Length - i)));
        return results;
    }

    /// <summary>Fraction of rows that are not entirely black.</summary>
    private static double FilledRows(RgbImage image)
    {
        var filled = 0;
        for (var y = 0; y < image.Height; y++)
            if (image.Data.AsSpan(y * image.Width * 3, image.Width * 3).ContainsAnyExcept((byte)0)) filled++;
        return (double)filled / image.Height;
    }

    [Fact]
    public void ReceivesBackToBackPictures()
    {
        var results = DecodeStream(Transmission(SstvModes.Robot36), Noise(0.5, 0.01), Transmission(SstvModes.MartinM2));

        Assert.Equal(2, results.Count);
        Assert.Same(SstvModes.Robot36, results[0].Mode);
        Assert.Same(SstvModes.MartinM2, results[1].Mode);
        Assert.All(results, r => Assert.True(r.Complete));
    }

    [Fact]
    public void NewHeaderInterruptsPictureAndStartsNext()
    {
        // Martin M1 stops 40 % in and a Robot 36 starts right away.
        var results = DecodeStream(Transmission(SstvModes.MartinM1, 0.4), Transmission(SstvModes.Robot36));

        Assert.Equal(2, results.Count);
        Assert.Same(SstvModes.MartinM1, results[0].Mode);
        Assert.False(results[0].Complete);
        Assert.InRange(FilledRows(results[0].Image), 0.3, 0.42);
        Assert.Same(SstvModes.Robot36, results[1].Mode);
        Assert.True(results[1].Complete);
    }

    [Fact]
    public void LostSignalEndsPictureAndReceivesNext()
    {
        // Martin M1 fades out 40 % in; 20 s of band noise later a Robot 36 arrives.
        var decoder = new SstvDecoder(12000);
        var results = new List<DecodedImage>();
        decoder.ImageCompleted += results.Add;
        var resultsBeforeNext = -1;
        foreach (var (part, isNext) in new[]
                 {
                     (Transmission(SstvModes.MartinM1, 0.4), false), (Noise(20, 0.05), false),
                     (Transmission(SstvModes.Robot36), true), (Noise(2, 0.01), false),
                 })
        {
            if (isNext) resultsBeforeNext = results.Count;
            for (var i = 0; i < part.Length; i += 4096)
                decoder.Process(part.AsSpan(i, Math.Min(4096, part.Length - i)));
        }

        // The partial Martin picture is delivered during the noise, not held until its nominal end.
        Assert.Equal(1, resultsBeforeNext);
        Assert.Equal(2, results.Count);
        Assert.False(results[0].Complete);
        Assert.InRange(FilledRows(results[0].Image), 0.3, 0.42);
        Assert.Same(SstvModes.Robot36, results[1].Mode);
        Assert.True(results[1].Complete);
    }

    /// <summary>
    /// Regression (real off-air Robot 36 cut at 30 %): a weak signal (≈ 6 dB in-band SNR) that also fades by
    /// 8 dB for 8 s, typical HF QSB, must be received to the end and not mistaken for a lost signal.
    /// </summary>
    [Theory]
    [InlineData("Robot 36")]
    [InlineData("Martin M1")]
    public void KeepsReceivingWeakFadingPicture(string modeName)
    {
        const int rate = 48000;
        var mode = SstvModes.Find(modeName)!;
        var options = new TransmitOptions(SendPreamble: true);
        var total = SstvEncoder.DurationMs(mode, options);
        var synth = new ToneSynthesizer(SstvEncoder.Encode(mode, TestImage(mode), options), total, rate, 0.5);
        var audio = new float[(int)(total * rate / 1000) + rate];
        synth.Read(audio);

        var rng = new Random(11);
        var fadeStart = (int)(total * rate / 1000 * 0.4);
        for (var i = 0; i < audio.Length; i++)
        {
            if (i >= fadeStart && i < fadeStart + 8 * rate) audio[i] *= 0.4f; // -8 dB
            audio[i] += (float)((rng.NextDouble() * 2 - 1) * 1.0);
        }

        var decoder = new SstvDecoder(rate);
        var results = new List<DecodedImage>();
        decoder.ImageCompleted += results.Add;
        for (var i = 0; i < audio.Length; i += 4096)
            decoder.Process(audio.AsSpan(i, Math.Min(4096, audio.Length - i)));

        var result = Assert.Single(results);
        Assert.Same(mode, result.Mode);
        Assert.True(result.Complete);
    }

    /// <summary>
    /// Regression (≈ 2 % of Robot 36 pictures at 10 dB SNR were dropped): Robot's VIS stop bit runs straight into
    /// the first 1200 Hz sync, and a 1 ms noise blip inside that long tone looked like the end of a sync pulse.
    /// The timing followed that one pulse ~30 ms early, the next ones were missed, and the picture was
    /// abandoned. Now a lone pulse never moves the timing; two must agree first. These are the benchmark's
    /// noise seeds that triggered it.
    /// </summary>
    [Theory]
    [InlineData("SNR 10 dB", 10.0, 0.0, 14)]
    [InlineData("clock +300 ppm, SNR 10", 10.0, 300.0, 7)]
    public void FirstSyncIsNotFooledByNoiseInTheVisStopBit(string name, double snrDb, double clockPpm, int seed)
    {
        var score = ReceptionBenchmark.Run(SstvModes.Robot36, new ReceptionBenchmark.Condition(name, snrDb, ClockPpm: clockPpm), seed);
        Assert.True(score.RightMode, score.Failure);
        Assert.True(score.Filled > 0.99);
    }

    /// <summary>
    /// Regression, real off-air: a weak Robot 36 on 14.230 MHz (FT-991, 2026-10-20) whose header the old detector
    /// missed. Noise squeezed its tones towards mid-band (leader read 1781 Hz, start/stop bits ~1250 Hz), so they
    /// failed the fixed-frequency checks. 6 s around the header, resampled to 12 kHz.
    /// </summary>
    [Fact]
    public void DecodesRealWeakHeader()
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "robot36-weak-header-14230.wav"));
        var (samples, rate) = WavReader.Read(file);
        var decoder = new SstvDecoder(rate);
        var started = new List<SstvMode>();
        decoder.ImageStarted += started.Add;
        for (var i = 0; i < samples.Length; i += 4096)
            decoder.Process(samples.AsSpan(i, Math.Min(4096, samples.Length - i)));

        Assert.Equal([SstvModes.Robot36], started);
        Assert.InRange(decoder.TuningOffsetHz, -60, 60);
    }

    /// <summary>1 s of noise, then the preamble, VIS and 1.5 s of picture, at an SNR in 3 kHz.</summary>
    private static float[] HeaderInNoise(SstvMode mode, double snrDb, int seed)
    {
        const int rate = 12000;
        var options = new TransmitOptions(SendPreamble: true);
        var ms = SstvEncoder.DurationMs(mode, options) - mode.ImageDurationMs + 1500;
        var synth = new ToneSynthesizer(SstvEncoder.Encode(mode, TestImage(mode), options), ms, rate, 0.5);
        var audio = new float[(int)((ms / 1000 + 1) * rate)];
        synth.Read(audio.AsSpan(rate));
        var sigma = Math.Sqrt(0.125 / Math.Pow(10, snrDb / 10) * (rate / 2.0) / 3000);
        var rng = new Random(seed);
        for (var i = 0; i < audio.Length; i++)
            audio[i] += (float)(sigma * Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble()));
        return audio;
    }

    /// <summary>The fixed-window detector found 10 % of headers at 2 dB SNR; this one ~87 %.</summary>
    [Fact]
    public void FindsWeakHeaders()
    {
        var found = Enumerable.Range(0, 20).AsParallel().Count(seed =>
        {
            var audio = HeaderInNoise(SstvModes.MartinM1, snrDb: 2, seed: 2000 + seed);
            var decoder = new SstvDecoder(12000);
            var ok = false;
            decoder.ImageStarted += m => ok |= m == SstvModes.MartinM1;
            for (var i = 0; i < audio.Length; i += 4096) decoder.Process(audio.AsSpan(i, Math.Min(4096, audio.Length - i)));
            return ok;
        });
        Assert.InRange(found, 14, 20);
    }

    /// <summary>A more sensitive detector must not start pictures out of thin air: 5 minutes of noise, 4 levels.</summary>
    [Fact]
    public void NoFalseStartsInFiveMinutesOfNoise()
    {
        var starts = new[] { 0.05, 0.2, 0.5, 1.0 }.AsParallel().Sum(level =>
        {
            var rng = new Random((int)(level * 1000));
            var decoder = new SstvDecoder(12000);
            var n = 0;
            decoder.ImageStarted += _ => n++;
            var buffer = new float[12000];
            for (var s = 0; s < 75; s++)
            {
                for (var i = 0; i < buffer.Length; i++)
                    buffer[i] = (float)(level * Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble()));
                decoder.Process(buffer);
            }
            return n;
        });
        Assert.Equal(0, starts);
    }

    [Fact]
    public void IgnoresNoiseWithoutVis()
    {
        var decoder = new SstvDecoder(12000);
        var started = false;
        decoder.ImageStarted += _ => started = true;
        var rng = new Random(7);
        var buffer = new float[12000];
        for (var s = 0; s < 30; s++)
        {
            for (var i = 0; i < buffer.Length; i++) buffer[i] = (float)(rng.NextDouble() * 2 - 1);
            decoder.Process(buffer);
        }
        Assert.False(started);
    }

    [Fact]
    public void WavFileWriter_StreamsAndPatchesHeader()
    {
        using var ms = new MemoryStream();
        using (var writer = new WavFileWriter(ms, 11025, leaveOpen: true))
        {
            writer.Write([0.5f, -0.5f, 0f]);
            writer.Write(new short[] { 1000, -1000 });
            Assert.Equal(44 + 10, writer.BytesWritten);
        }
        ms.Position = 0;

        var (samples, rate) = WavReader.Read(ms);

        Assert.Equal(11025, rate);
        Assert.Equal(5, samples.Length);
        Assert.Equal(0.5f, samples[0], 0.001f);
        Assert.Equal(-1000 / 32768f, samples[4], 0.0001f);
    }

    [Fact]
    public async Task WavReader_ReadsWhatWavWriterWrote()
    {
        var synth = new ToneSynthesizer([new Tone(1900, 50)], 50, 8000, amplitude: 0.5);
        using var ms = new MemoryStream();
        await WavWriter.WriteAsync(synth, ms);
        ms.Position = 0;

        var (samples, rate) = WavReader.Read(ms);

        Assert.Equal(8000, rate);
        Assert.Equal(400, samples.Length);
        Assert.InRange(samples.Max(), 0.45f, 0.51f);
    }
}
