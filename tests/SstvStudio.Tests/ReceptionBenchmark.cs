// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Collections.Concurrent;
using System.Text;
using SstvStudio.Core.Audio;
using SstvStudio.Core.Decoding;
using SstvStudio.Core.Encoding;
using SstvStudio.Core.Imaging;
using SstvStudio.Core.Modes;
using Xunit.Abstractions;

namespace SstvStudio.Tests;

/// <summary>Runs the benchmark on its own: its parallel decoding starved the rig tests' network timeouts.</summary>
[CollectionDefinition(nameof(ReceptionBenchmark), DisableParallelization = true)]
public class ReceptionBenchmarkCollection;

/// <summary>
/// Reception benchmark: test pictures through simulated HF conditions, scored by PSNR against the original.
/// Every decoder change must keep each score at or above its floor in <see cref="Floors"/>; raise a floor when a
/// change improves it. Run with <c>--logger "console;verbosity=detailed"</c> to see the table.
/// </summary>
[Collection(nameof(ReceptionBenchmark))]
public class ReceptionBenchmark(ITestOutputHelper output)
{
    private const int Rate = 12000;
    private const double SignalAmplitude = 0.5;
    private static readonly string[] Modes = ["Robot 36", "Martin M2", "Scottie S2"];

    /// <param name="SnrDb">Signal-to-noise ratio in a 3 kHz bandwidth (white Gaussian noise).</param>
    /// <param name="FadeDb">Depth of a slow fade (7 s period).</param>
    /// <param name="DriftHz">Tuning drift from the start to the end of the picture.</param>
    /// <param name="CarrierHz">A steady carrier at half the signal's amplitude.</param>
    public sealed record Condition(string Name, double SnrDb = double.PositiveInfinity, double FadeDb = 0,
        double DriftHz = 0, double ClockPpm = 0, double CarrierHz = 0);

    public static readonly Condition[] Conditions =
    [
        new("clean"),
        new("SNR 10 dB", 10),
        new("SNR 5 dB", 5),
        new("SNR 2 dB", 2),
        new("SNR 0 dB", 0),
        new("fade 10 dB, SNR 8", 8, FadeDb: 10),
        new("drift 40 Hz, SNR 10", 10, DriftHz: 40),
        new("clock +300 ppm, SNR 10", 10, ClockPpm: 300),
        new("carrier 1800 Hz, SNR 10", 10, CarrierHz: 1800),
    ];

    /// <param name="Failure">Why no picture came out: no header heard, a wrong mode, or a start that was abandoned.</param>
    public sealed record Score(string Mode, string Condition, bool RightMode, double Filled, double Psnr, string? Failure = null);

    /// <summary>
    /// Lowest acceptable PSNR (dB) per condition, over all modes: about 1 dB below the worst mode on 2026-10-20.
    /// Null marks a known gap: reported, not yet required. No VIS header is found at 0 dB with these seeds, or deep in
    /// a fade. (Until the self-referencing VIS detector, 2 dB was a gap too.)
    /// </summary>
    private static readonly Dictionary<string, double?> Floors = new()
    {
        ["clean"] = 22.5,
        ["SNR 10 dB"] = 14.5,
        ["SNR 5 dB"] = 10.5,
        ["SNR 2 dB"] = 8.0,
        ["SNR 0 dB"] = null,
        ["fade 10 dB, SNR 8"] = null,
        ["drift 40 Hz, SNR 10"] = 14.0,
        ["clock +300 ppm, SNR 10"] = 14.5,
        ["carrier 1800 Hz, SNR 10"] = 11.0,
    };

    [Fact]
    public void ScoresStayAboveFloors()
    {
        var scores = new ConcurrentBag<Score>();
        // Fixed seeds, so every run (and every change) is scored on exactly the same noise.
        var cases = Modes.SelectMany((m, i) => Conditions.Select((c, j) => (Mode: SstvModes.Find(m)!, Condition: c, Seed: 100 * i + j)));
        Parallel.ForEach(cases, c => scores.Add(Run(c.Mode, c.Condition, c.Seed)));

        var table = new StringBuilder("| Condition | " + string.Join(" | ", Modes) + " |\n|---|" + string.Concat(Modes.Select(_ => "---|")) + "\n");
        foreach (var condition in Conditions)
        {
            table.Append($"| {condition.Name} |");
            foreach (var mode in Modes)
            {
                var s = scores.Single(x => x.Mode == mode && x.Condition == condition.Name);
                table.Append(s.RightMode ? $" {s.Psnr:0.0} dB{(s.Filled < 0.99 ? $" ({s.Filled:P0} lines)" : "")} |" : $" {s.Failure} |");
            }
            table.Append('\n');
        }
        output.WriteLine(table.ToString());

        foreach (var s in scores)
        {
            if (Floors[s.Condition] is not { } floor) continue;
            Assert.True(s.RightMode, $"{s.Mode}, {s.Condition}: {s.Failure}");
            Assert.True(s.Psnr >= floor, $"{s.Mode}, {s.Condition}: {s.Psnr:0.0} dB < floor {floor:0.0} dB");
        }
    }

    /// <summary>Decodes one mode under one condition; also used by regression tests that pin a failing seed.</summary>
    internal static Score Run(SstvMode mode, Condition condition, int seed)
    {
        var image = TestImage(mode);
        var audio = Audio(mode, image, condition, seed);
        var decoder = new SstvDecoder(Rate);
        var results = new List<DecodedImage>();
        var started = new List<string>();
        var aborted = 0;
        decoder.ImageCompleted += results.Add;
        decoder.ImageStarted += m => started.Add(m.Name);
        decoder.ImageAborted += () => aborted++;
        for (var i = 0; i < audio.Length; i += 4096)
            decoder.Process(audio.AsSpan(i, Math.Min(4096, audio.Length - i)));
        decoder.Stop();

        var best = results.Where(r => r.Mode == mode).MaxBy(r => FilledRows(r.Image));
        if (best is not null)
            return new Score(mode.Name, condition.Name, true, FilledRows(best.Image), Psnr(image, best.Image));
        var failure = started.Count == 0 ? "no header"
            : !started.Contains(mode.Name) ? $"header read as {string.Join(", ", started)}"
            : $"started, abandoned ×{aborted}";
        return new Score(mode.Name, condition.Name, false, 0, 0, failure);
    }

    /// <summary>1 s of channel noise, the transmission through the simulated channel, 1 s of noise.</summary>
    internal static float[] Audio(SstvMode mode, RgbImage image, Condition c, int seed)
    {
        var options = new TransmitOptions(SendPreamble: true);
        var duration = SstvEncoder.DurationMs(mode, options);
        var elapsed = 0.0;
        var tones = SstvEncoder.Encode(mode, image, options).Select(t =>
        {
            var drift = c.DriftHz * elapsed / duration;
            elapsed += t.DurationMs;
            return t.FrequencyHz > 0 ? t with { FrequencyHz = t.FrequencyHz + drift } : t;
        });
        var synth = new ToneSynthesizer(tones, duration, Rate, SignalAmplitude, c.ClockPpm);
        var audio = new float[(int)((duration / 1000 + 2) * Rate)];
        synth.Read(audio.AsSpan(Rate));

        // Noise power in 3 kHz = SNR below the signal's power (A²/2); white noise spreads over Rate/2.
        var sigma = double.IsPositiveInfinity(c.SnrDb)
            ? 0
            : Math.Sqrt(SignalAmplitude * SignalAmplitude / 2 / Math.Pow(10, c.SnrDb / 10) * (Rate / 2.0) / 3000);
        var rng = new Random(seed);
        for (var i = 0; i < audio.Length; i++)
        {
            var t = (double)i / Rate;
            var gain = c.FadeDb > 0 ? Math.Pow(10, -c.FadeDb / 20 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * t / 7))) : 1;
            var carrier = c.CarrierHz > 0 ? SignalAmplitude / 2 * Math.Sin(2 * Math.PI * c.CarrierHz * t) : 0;
            audio[i] = (float)(audio[i] * gain + carrier + sigma * Gaussian(rng));
        }
        return audio;
    }

    private static double Gaussian(Random rng) =>
        Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());

    /// <summary>Gradients plus a fine checkerboard band, so both smooth areas and detail count.</summary>
    internal static RgbImage TestImage(SstvMode mode)
    {
        var img = new RgbImage(mode.Width, mode.Height);
        for (var y = 0; y < mode.Height; y++)
        for (var x = 0; x < mode.Width; x++)
        {
            img[x, y] = y > mode.Height * 2 / 5 && y < mode.Height * 3 / 5
                ? ((x / 8 + y / 8) % 2 == 0 ? ((byte)230, (byte)230, (byte)230) : ((byte)25, (byte)25, (byte)25))
                : ((byte)(255 * x / mode.Width), (byte)(255 * y / mode.Height), (byte)(255 - 255 * x / mode.Width));
        }
        return img;
    }

    /// <summary>Fraction of rows that aren't blank (black) — lost lines come out blank.</summary>
    private static double FilledRows(RgbImage image)
    {
        var filled = 0;
        for (var y = 0; y < image.Height; y++)
        {
            var any = false;
            for (var x = 0; x < image.Width && !any; x++)
                any = image[x, y] != (0, 0, 0);
            if (any) filled++;
        }
        return (double)filled / image.Height;
    }

    /// <summary>Peak signal-to-noise ratio over all pixels and channels, ignoring a 2-pixel border.</summary>
    private static double Psnr(RgbImage expected, RgbImage actual)
    {
        double sum = 0;
        long n = 0;
        for (var y = 2; y < expected.Height - 2; y++)
        for (var x = 2; x < expected.Width - 2; x++)
        {
            var (r1, g1, b1) = expected[x, y];
            var (r2, g2, b2) = actual[x, y];
            sum += (r1 - r2) * (r1 - r2) + (g1 - g2) * (g1 - g2) + (b1 - b2) * (b1 - b2);
            n += 3;
        }
        return 10 * Math.Log10(255.0 * 255.0 / Math.Max(sum / n, 1e-9));
    }
}
