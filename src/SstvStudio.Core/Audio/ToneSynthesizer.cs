// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Audio;

/// <summary>
/// Streams a tone sequence as mono float samples with a phase-continuous oscillator.
/// Segment boundaries are tracked as cumulative fractional sample positions so per-pixel
/// rounding never accumulates into slant. <c>clockPpm</c> compensates sound-card clock error.
/// </summary>
public sealed class ToneSynthesizer
{
    private const double RampMs = 5;

    private readonly IEnumerator<Tone> _tones;
    private readonly double _effectiveRate;
    private readonly long _rampSamples;
    private double _amplitude;
    private double _phase;
    private double _phaseIncrement;
    private bool _silent;
    private double _segmentEndMs;
    private double _segmentEndSample;
    private long _position;

    public ToneSynthesizer(IEnumerable<Tone> tones, double totalDurationMs, int sampleRate,
        double amplitude = 0.8, double clockPpm = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _tones = tones.GetEnumerator();
        SampleRate = sampleRate;
        _effectiveRate = sampleRate * (1 + clockPpm / 1_000_000.0);
        Amplitude = amplitude;
        TotalSamples = (long)Math.Ceiling(totalDurationMs * _effectiveRate / 1000.0);
        _rampSamples = (long)(RampMs * sampleRate / 1000.0);
    }

    public int SampleRate { get; }
    public long TotalSamples { get; }
    public long Position => Interlocked.Read(ref _position);
    public bool IsFinished { get; private set; }
    public double Progress => TotalSamples == 0 ? 1 : Math.Min(1, (double)Position / TotalSamples);

    /// <summary>Output level 0–1. May be changed while playing.</summary>
    public double Amplitude
    {
        get => Volatile.Read(ref _amplitude);
        set => Volatile.Write(ref _amplitude, Math.Clamp(value, 0, 1));
    }

    /// <summary>Fills <paramref name="buffer"/>; returns fewer samples than requested only at the end.</summary>
    public int Read(Span<float> buffer)
    {
        var amplitude = Amplitude;
        var n = 0;
        while (n < buffer.Length)
        {
            while (_position >= _segmentEndSample)
            {
                if (IsFinished || !_tones.MoveNext())
                {
                    IsFinished = true;
                    return n;
                }
                var tone = _tones.Current;
                _segmentEndMs += tone.DurationMs;
                _segmentEndSample = _segmentEndMs * _effectiveRate / 1000.0;
                _silent = tone.FrequencyHz <= 0;
                _phaseIncrement = 2 * Math.PI * tone.FrequencyHz / _effectiveRate;
            }

            var envelope = Math.Min(1.0, Math.Min((_position + 1.0) / _rampSamples, (TotalSamples - _position) / (double)_rampSamples));
            buffer[n++] = _silent ? 0f : (float)(Math.Sin(_phase) * amplitude * Math.Max(0, envelope));

            if (!_silent)
            {
                _phase += _phaseIncrement;
                if (_phase >= 2 * Math.PI) _phase -= 2 * Math.PI;
            }
            Interlocked.Increment(ref _position);
        }
        return n;
    }
}
