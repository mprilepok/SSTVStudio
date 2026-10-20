using SstvStudio.Core.Audio;
using SstvStudio.Core.Decoding;
using SstvStudio.Core.Encoding;
using SstvStudio.Core.Imaging;
using SstvStudio.Core.Modes;
using Xunit.Abstractions;

namespace SstvStudio.Tests;

public class TempVisProbe(ITestOutputHelper output)
{
    private const int Rate = 12000;

    /// <summary>1 s noise, preamble + VIS + 1.5 s of picture, at the given SNR (3 kHz), optional fade depth at the VIS.</summary>
    private static float[] Header(SstvMode mode, double snrDb, int seed, double gainAtVis = 1)
    {
        var options = new TransmitOptions(SendPreamble: true);
        var headerMs = SstvEncoder.DurationMs(mode, options) - mode.ImageDurationMs;
        var ms = headerMs + 1500;
        var synth = new ToneSynthesizer(SstvEncoder.Encode(mode, RgbImage.Blank(mode.Width, mode.Height), options), ms, Rate, 0.5);
        var audio = new float[(int)((ms / 1000 + 1) * Rate)];
        synth.Read(audio.AsSpan(Rate));
        var sigma = Math.Sqrt(0.125 / Math.Pow(10, snrDb / 10) * (Rate / 2.0) / 3000);
        var rng = new Random(seed);
        for (var i = 0; i < audio.Length; i++)
        {
            var g = i >= Rate ? gainAtVis : 1;
            audio[i] = (float)(audio[i] * g + sigma * Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble()));
        }
        return audio;
    }

    private static List<SstvMode> Starts(float[] audio)
    {
        var decoder = new SstvDecoder(Rate);
        var started = new List<SstvMode>();
        decoder.ImageStarted += started.Add;
        for (var i = 0; i < audio.Length; i += 4096) decoder.Process(audio.AsSpan(i, Math.Min(4096, audio.Length - i)));
        return started;
    }

    [Fact]
    public void HeaderDetectionAndFalseTriggers()
    {
        var modes = new[] { SstvModes.MartinM1, SstvModes.ScottieS1, SstvModes.Robot36, SstvModes.Pd120 };
        var t = typeof(SstvDecoder).Assembly.GetType("SstvStudio.Core.Decoding.VisDetector")!;
        void Set(string f, object v) => t.GetField(f, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, v);
        foreach (var kv in (Environment.GetEnvironmentVariable("VIS_SET") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var (name, value) = (kv.Split('=')[0], kv.Split('=')[1]);
            Set(name, name == "UseMedian" ? bool.Parse(value) : double.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
            output.WriteLine($"set {name}={value}");
        }
        foreach (var snr in new[] { 6.0, 4, 3, 2, 1, 0, -1, -2 })
        {
            var runs = modes.SelectMany(m => Enumerable.Range(0, 25).Select(s => (Mode: m, Seed: s))).AsParallel()
                .Select(r => Starts(Header(r.Mode, snr, 1000 + r.Seed)).Contains(r.Mode)).ToList();
            output.WriteLine($"SNR {snr,3} dB: header found {runs.Count(x => x),3}/{runs.Count}");
        }

        // False triggers: 40 minutes of band noise (8 parallel 5-minute streams).
        var falseStarts = Enumerable.Range(0, 8).AsParallel().Sum(seed =>
        {
            var rng = new Random(500 + seed);
            var decoder = new SstvDecoder(Rate);
            var n = 0;
            decoder.ImageStarted += _ => n++;
            var buffer = new float[Rate];
            for (var s = 0; s < 300; s++)
            {
                for (var i = 0; i < buffer.Length; i++)
                    buffer[i] = (float)(0.3 * Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble()));
                decoder.Process(buffer);
            }
            return n;
        });
        output.WriteLine($"false header detections in 40 min of noise: {falseStarts}");
    }
}
