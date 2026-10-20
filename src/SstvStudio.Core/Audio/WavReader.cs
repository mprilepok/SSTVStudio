// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Buffers.Binary;

namespace SstvStudio.Core.Audio;

public static class WavReader
{
    /// <summary>Reads a PCM (8/16/24/32-bit) or IEEE-float WAV file; multi-channel audio uses the first channel.</summary>
    public static (float[] Samples, int SampleRate) Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a RIFF/WAV file.");
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAV file.");

        int format = 0, channels = 0, sampleRate = 0, bits = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                format = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                if (format == 0xFFFE && size >= 26) // WAVE_FORMAT_EXTENSIBLE: real format is in the sub-format GUID
                {
                    reader.ReadBytes(8);
                    format = reader.ReadInt16();
                    reader.ReadBytes(size - 26);
                }
                else
                {
                    reader.ReadBytes(size - 16);
                }
            }
            else if (id == "data")
            {
                if (channels == 0) throw new InvalidDataException("WAV data before format chunk.");
                var data = reader.ReadBytes(Math.Min(size, (int)(stream.Length - stream.Position)));
                return (Decode(data, format, channels, bits), sampleRate);
            }
            else
            {
                reader.ReadBytes(size + (size & 1));
            }
        }
        throw new InvalidDataException("WAV file has no data chunk.");
    }

    private static float[] Decode(byte[] data, int format, int channels, int bits)
    {
        var bytes = bits / 8;
        var frame = bytes * channels;
        var samples = new float[data.Length / frame];
        for (var i = 0; i < samples.Length; i++)
        {
            var s = data.AsSpan(i * frame, bytes);
            samples[i] = (format, bits) switch
            {
                (3, 32) => BinaryPrimitives.ReadSingleLittleEndian(s),
                (1, 8) => (s[0] - 128) / 128f,
                (1, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                (1, 24) => ((s[0] << 8 | s[1] << 16 | s[2] << 24) >> 8) / 8388608f,
                (1, 32) => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
                _ => throw new InvalidDataException($"Unsupported WAV format {format} / {bits}-bit."),
            };
        }
        return samples;
    }
}
