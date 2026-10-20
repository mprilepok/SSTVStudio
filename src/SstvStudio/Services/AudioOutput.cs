// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Runtime.InteropServices;
using NAudio.Wave;
using SstvStudio.Core.Audio;

namespace SstvStudio.Services;

/// <summary>Plays a <see cref="ToneSynthesizer"/> on a WinMM output device (e.g. the rig's USB codec).</summary>
public static class AudioOutput
{
    public const string DefaultDevice = "Default output";

    public static IReadOnlyList<string> GetDevices()
    {
        var devices = new List<string> { DefaultDevice };
        try
        {
            var count = waveOutGetNumDevs();
            for (var i = 0; i < count; i++)
                devices.Add(waveOutGetDevCaps((nint)i, out var caps, Marshal.SizeOf<WaveOutCaps>()) == 0 ? caps.ProductName : $"Device {i}");
        }
        catch (Exception)
        {
            // WinMM unavailable (non-Windows): only WAV export will work.
        }
        return devices;
    }

    public static async Task PlayAsync(ToneSynthesizer synth, string deviceName, CancellationToken ct)
    {
        using var output = new WaveOutEvent
        {
            DeviceNumber = DeviceNumber(deviceName),
            DesiredLatency = 200,
            NumberOfBuffers = 3,
        };
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is not null) done.TrySetException(e.Exception);
            else done.TrySetResult();
        };

        output.Init(new SynthSampleProvider(synth), convertTo16Bit: true);
        output.Play();
        await using (ct.Register(output.Stop))
            await done.Task;
        ct.ThrowIfCancellationRequested();
    }

    private static int DeviceNumber(string name)
    {
        var devices = GetDevices();
        for (var i = 1; i < devices.Count; i++)
            if (devices[i] == name) return i - 1;
        return -1; // WAVE_MAPPER = system default
    }

    // NAudio 2.x only exposes input-device enumeration in NAudio.WinMM, so query WinMM directly.
    [DllImport("winmm.dll")]
    private static extern int waveOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int waveOutGetDevCaps(nint deviceId, out WaveOutCaps caps, int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WaveOutCaps
    {
        public short ManufacturerId;
        public short ProductId;
        public int DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ProductName;
        public int Formats;
        public short Channels;
        public short Reserved;
        public int Support;
    }

    private sealed class SynthSampleProvider(ToneSynthesizer synth) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(synth.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count) => synth.Read(buffer.AsSpan(offset, count));
    }
}
