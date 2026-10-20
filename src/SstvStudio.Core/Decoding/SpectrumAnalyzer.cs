// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Numerics;

namespace SstvStudio.Core.Decoding;

/// <summary>
/// Turns audio into spectrum rows for a waterfall: Hann-windowed FFT every ~<see cref="HopMs"/> ms, magnitudes
/// in dB resampled to <see cref="Columns"/> columns spanning <see cref="MinHz"/>–<see cref="MaxHz"/>.
/// Rows are queued for a UI thread to take; <see cref="Push"/> is called by one producer.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const double MinHz = 1000;
    public const double MaxHz = 2500;
    public const int Columns = 300; // 5 Hz per column
    private const double HopMs = 80;

    private readonly int _fftSize;
    private readonly double _binHz;
    private readonly int _hop;
    private readonly float[] _window;
    private readonly float[] _ring;
    private readonly Complex[] _fft;
    private readonly System.Collections.Concurrent.ConcurrentQueue<float[]> _rows = new();
    private int _ringPos;
    private int _sinceLast;

    public SpectrumAnalyzer(int sampleRate)
    {
        // ~5 Hz bins: 8192 points at 44.1/48 kHz, fewer at lower rates.
        _fftSize = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(256, sampleRate / 6));
        _binHz = (double)sampleRate / _fftSize;
        _hop = (int)(sampleRate * HopMs / 1000);
        _window = new float[_fftSize];
        for (var i = 0; i < _fftSize; i++) _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (_fftSize - 1)));
        _ring = new float[_fftSize];
        _fft = new Complex[_fftSize];
    }

    /// <summary>Frequency (Hz) at the centre of a column.</summary>
    public static double ColumnHz(int column) => MinHz + (MaxHz - MinHz) * (column + 0.5) / Columns;

    /// <summary>Column for a frequency, or -1 if outside the display range.</summary>
    public static int ColumnOf(double hz) =>
        hz < MinHz || hz >= MaxHz ? -1 : (int)((hz - MinHz) / (MaxHz - MinHz) * Columns);

    public void Push(ReadOnlySpan<float> samples)
    {
        foreach (var x in samples)
        {
            _ring[_ringPos] = x;
            _ringPos = (_ringPos + 1) % _fftSize;
            if (++_sinceLast < _hop) continue;
            _sinceLast = 0;
            _rows.Enqueue(ComputeRow());
            while (_rows.Count > 64 && _rows.TryDequeue(out _)) { } // nobody is reading: keep it bounded
        }
    }

    /// <summary>Takes the oldest pending row (dB, roughly −120…0), or null when none is waiting.</summary>
    public float[]? TryTakeRow() => _rows.TryDequeue(out var row) ? row : null;

    private float[] ComputeRow()
    {
        for (var i = 0; i < _fftSize; i++)
            _fft[i] = new Complex(_ring[(_ringPos + i) % _fftSize] * _window[i], 0);
        Fft(_fft);

        var row = new float[Columns];
        for (var c = 0; c < Columns; c++)
        {
            // Peak of the bins that fall into this column (≥ 1 bin).
            var lo = (int)((MinHz + (MaxHz - MinHz) * c / Columns) / _binHz);
            var hi = Math.Max(lo, (int)((MinHz + (MaxHz - MinHz) * (c + 1) / Columns) / _binHz) - 1);
            double peak = 0;
            for (var b = lo; b <= hi && b < _fftSize / 2; b++) peak = Math.Max(peak, _fft[b].Magnitude);
            row[c] = (float)(20 * Math.Log10(peak * 4 / _fftSize + 1e-9));
        }
        return row;
    }

    /// <summary>In-place iterative radix-2 FFT.</summary>
    internal static void Fft(Complex[] data)
    {
        var n = data.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var w = Complex.FromPolarCoordinates(1, -2 * Math.PI / len);
            for (var i = 0; i < n; i += len)
            {
                var wk = Complex.One;
                for (var k = 0; k < len / 2; k++)
                {
                    var u = data[i + k];
                    var v = data[i + k + len / 2] * wk;
                    data[i + k] = u + v;
                    data[i + k + len / 2] = u - v;
                    wk *= w;
                }
            }
        }
    }
}
