// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Services;

/// <summary>Growable 16-bit audio buffer that can drop all but the newest samples while idle.</summary>
internal sealed class AudioClipBuffer
{
    private short[] _data = new short[1 << 16];

    public int Count { get; private set; }

    public void Append(ReadOnlySpan<float> samples)
    {
        if (Count + samples.Length > _data.Length)
            Array.Resize(ref _data, Math.Max(_data.Length * 2, Count + samples.Length));
        for (var i = 0; i < samples.Length; i++)
            _data[Count + i] = (short)Math.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
        Count += samples.Length;
    }

    /// <summary>Keeps roughly the newest <paramref name="samples"/>; trims lazily so the copy is amortized.</summary>
    public void KeepLast(int samples)
    {
        if (Count <= samples * 2) return;
        Array.Copy(_data, Count - samples, _data, 0, samples);
        Count = samples;
    }

    public short[] CopyFrom(int start) => _data[Math.Clamp(start, 0, Count)..Count];

    public void Clear() => Count = 0;
}
