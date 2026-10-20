// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Audio;

/// <summary>
/// Streams 16-bit PCM mono audio to a seekable stream. The RIFF/data sizes are written as zero and
/// patched on <see cref="Dispose"/>, so recordings of unknown length never need to be held in memory.
/// </summary>
public sealed class WavFileWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private byte[] _buffer = [];
    private long _dataBytes;
    private bool _disposed;

    public WavFileWriter(Stream stream, int sampleRate, bool leaveOpen = false)
    {
        if (!stream.CanSeek) throw new ArgumentException("WAV recording needs a seekable stream.", nameof(stream));
        _stream = stream;
        _leaveOpen = leaveOpen;
        SampleRate = sampleRate;
        _stream.Write(WavWriter.Header(sampleRate, 0));
    }

    public int SampleRate { get; }
    public long BytesWritten => 44 + _dataBytes;
    public TimeSpan Duration => TimeSpan.FromSeconds(_dataBytes / 2.0 / SampleRate);

    public void Write(ReadOnlySpan<float> samples)
    {
        EnsureBuffer(samples.Length);
        for (var i = 0; i < samples.Length; i++)
        {
            var s = (short)Math.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            _buffer[2 * i] = (byte)s;
            _buffer[2 * i + 1] = (byte)(s >> 8);
        }
        _stream.Write(_buffer, 0, samples.Length * 2);
        _dataBytes += samples.Length * 2;
    }

    public void Write(ReadOnlySpan<short> samples)
    {
        EnsureBuffer(samples.Length);
        for (var i = 0; i < samples.Length; i++)
        {
            _buffer[2 * i] = (byte)samples[i];
            _buffer[2 * i + 1] = (byte)(samples[i] >> 8);
        }
        _stream.Write(_buffer, 0, samples.Length * 2);
        _dataBytes += samples.Length * 2;
    }

    /// <summary>Writes a complete file in one go.</summary>
    public static void Write(string path, ReadOnlySpan<short> samples, int sampleRate)
    {
        using var writer = new WavFileWriter(File.Create(path), sampleRate);
        writer.Write(samples);
    }

    private void EnsureBuffer(int samples)
    {
        if (_buffer.Length < samples * 2) _buffer = new byte[samples * 2];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var end = _stream.Position;
        _stream.Position = 0;
        _stream.Write(WavWriter.Header(SampleRate, (int)Math.Min(_dataBytes, int.MaxValue - 36)));
        _stream.Position = end;
        _stream.Flush();
        if (!_leaveOpen) _stream.Dispose();
    }
}
