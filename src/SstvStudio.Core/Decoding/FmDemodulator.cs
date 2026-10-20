// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Numerics;

namespace SstvStudio.Core.Decoding;

/// <summary>
/// Converts audio to an instantaneous-frequency stream. Mixes the SSTV band (1100–2300 Hz) down to baseband
/// around its centre (1700 Hz), low-pass filters to ±800 Hz to keep the noise bandwidth small, decimates to
/// ~12 kHz, then applies a phase-difference discriminator.
/// </summary>
public sealed class FmDemodulator
{
    public const double CenterHz = 1700;
    private const double CutoffHz = 800;
    private const double TargetRate = 12000;

    private readonly int _decimation;
    private readonly float[] _taps;
    // Delay lines are stored twice over so the newest N samples are always one contiguous span.
    private readonly float[] _re;
    private readonly float[] _im;
    private readonly double _oscStep;
    private int _pos;
    private int _phase;
    private double _oscPhase;
    private double _prevRe = 1;
    private double _prevIm;

    public FmDemodulator(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        _decimation = Math.Max(1, (int)Math.Round(sampleRate / TargetRate));
        OutputRate = (double)sampleRate / _decimation;
        _taps = DesignLowpass((sampleRate / 200) | 1, CutoffHz / sampleRate);
        _re = new float[_taps.Length * 2];
        _im = new float[_taps.Length * 2];
        _oscStep = 2 * Math.PI * CenterHz / sampleRate;
    }

    /// <summary>Rate of the frequency stream, in samples per second.</summary>
    public double OutputRate { get; }

    /// <summary>Upper bound on how many frequency samples <paramref name="inputLength"/> audio samples can produce.</summary>
    public int MaxOutput(int inputLength) => inputLength / _decimation + 1;

    /// <summary>Demodulates <paramref name="input"/>; returns the number of frequency samples written (Hz).</summary>
    public int Process(ReadOnlySpan<float> input, Span<float> output)
    {
        var n = _taps.Length;
        var written = 0;
        foreach (var x in input)
        {
            var (sin, cos) = Math.SinCos(_oscPhase);
            _oscPhase += _oscStep;
            if (_oscPhase > 2 * Math.PI) _oscPhase -= 2 * Math.PI;

            _re[_pos] = _re[_pos + n] = (float)(x * cos);
            _im[_pos] = _im[_pos + n] = (float)(-x * sin);
            _pos = (_pos + 1) % n;

            if (++_phase < _decimation) continue;
            _phase = 0;

            double re = Dot(_taps, _re.AsSpan(_pos, n));
            double im = Dot(_taps, _im.AsSpan(_pos, n));
            // arg(z · conj(prev)) is the phase advance since the previous output sample.
            var dRe = re * _prevRe + im * _prevIm;
            var dIm = im * _prevRe - re * _prevIm;
            _prevRe = re;
            _prevIm = im;
            output[written++] = (float)(CenterHz + Math.Atan2(dIm, dRe) * OutputRate / (2 * Math.PI));
        }
        return written;
    }

    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var acc = Vector<float>.Zero;
        var i = 0;
        for (; i <= a.Length - Vector<float>.Count; i += Vector<float>.Count)
            acc += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);
        var sum = Vector.Sum(acc);
        for (; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    /// <summary>Hamming-windowed sinc low-pass with unity DC gain; <paramref name="cutoff"/> in cycles/sample.</summary>
    private static float[] DesignLowpass(int length, double cutoff)
    {
        var taps = new float[length];
        var m = (length - 1) / 2.0;
        double sum = 0;
        for (var i = 0; i < length; i++)
        {
            var t = i - m;
            var sinc = t == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * t) / (Math.PI * t);
            var window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (length - 1));
            taps[i] = (float)(sinc * window);
            sum += taps[i];
        }
        for (var i = 0; i < length; i++) taps[i] = (float)(taps[i] / sum);
        return taps;
    }
}
