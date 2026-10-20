// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Audio;

public static class WavWriter
{
    /// <summary>Renders the synthesizer to a 16-bit PCM mono WAV file.</summary>
    public static async Task WriteAsync(ToneSynthesizer synth, Stream output, CancellationToken ct = default)
    {
        using var pcm = new MemoryStream();
        var floats = new float[8192];
        var bytes = new byte[floats.Length * 2];
        int read;
        while ((read = synth.Read(floats)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i < read; i++)
            {
                var s = (short)Math.Clamp(floats[i] * short.MaxValue, short.MinValue, short.MaxValue);
                bytes[2 * i] = (byte)s;
                bytes[2 * i + 1] = (byte)(s >> 8);
            }
            pcm.Write(bytes, 0, read * 2);
        }

        await output.WriteAsync(Header(synth.SampleRate, (int)pcm.Length), ct);
        pcm.Position = 0;
        await pcm.CopyToAsync(output, ct);
    }

    internal static byte[] Header(int sampleRate, int dataBytes)
    {
        using var ms = new MemoryStream(44);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);            // fmt chunk size
        w.Write((short)1);      // PCM
        w.Write((short)1);      // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2); // byte rate
        w.Write((short)2);      // block align
        w.Write((short)16);     // bits per sample
        w.Write("data"u8);
        w.Write(dataBytes);
        w.Flush();
        return ms.ToArray();
    }
}
