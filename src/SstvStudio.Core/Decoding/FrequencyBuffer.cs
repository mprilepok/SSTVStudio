// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Decoding;

/// <summary>Append-only frequency stream addressed by absolute sample index; old samples can be dropped.</summary>
internal sealed class FrequencyBuffer
{
    private float[] _data = new float[1 << 16];
    private int _count;

    /// <summary>Absolute index of the oldest sample still held.</summary>
    public long Start { get; private set; }
    /// <summary>Absolute index one past the newest sample.</summary>
    public long End => Start + _count;

    public long Append(float value)
    {
        if (_count == _data.Length) Array.Resize(ref _data, _data.Length * 2);
        _data[_count++] = value;
        return End - 1;
    }

    /// <summary>Sample at an absolute index, clamped to the range held.</summary>
    public float this[long index] => _count == 0 ? 0 : _data[Math.Clamp(index - Start, 0, _count - 1)];

    /// <summary>Drops old samples so that roughly the newest <paramref name="keep"/> remain.</summary>
    public void TrimTo(int keep)
    {
        if (_count <= keep * 2) return;
        var drop = _count - keep;
        Array.Copy(_data, drop, _data, 0, keep);
        _count = keep;
        Start += drop;
    }
}
