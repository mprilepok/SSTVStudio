// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using NAudio.Wave;

namespace SstvStudio.Services;

/// <summary>A live mono audio source feeding the receiver.</summary>
public interface IAudioInput : IDisposable
{
    /// <summary>Raised on a capture thread with each new block of samples (−1…1).</summary>
    event Action<float[]>? SamplesAvailable;
    event Action<Exception?>? Stopped;
    /// <summary>Wall-clock time since start minus audio delivered; grows only if audio is being lost.</summary>
    TimeSpan Shortfall { get; }
    void Start();
}

/// <summary>Captures mono 16-bit audio from a WinMM input device (e.g. the rig's USB codec) as floats.</summary>
public sealed class AudioInput : IAudioInput
{
    public const string DefaultDevice = "Default input";

    private readonly WaveInEvent _wave;
    private readonly int _sampleRate;
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private long _samplesReceived;

    public AudioInput(string deviceName, int sampleRate)
    {
        _sampleRate = sampleRate;
        _wave = new WaveInEvent
        {
            DeviceNumber = DeviceNumber(deviceName),
            WaveFormat = new WaveFormat(sampleRate, 16, 1),
            BufferMilliseconds = 50,
            NumberOfBuffers = 4,
        };
        _wave.DataAvailable += (_, e) =>
        {
            var samples = new float[e.BytesRecorded / 2];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
            Interlocked.Add(ref _samplesReceived, samples.Length);
            SamplesAvailable?.Invoke(samples);
        };
        _wave.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
    }

    public event Action<float[]>? SamplesAvailable;
    public event Action<Exception?>? Stopped;

    /// <summary>The sound card, or a simulated input when <see cref="SimulatedAudioInput.EnvironmentVariable"/> is set.</summary>
    public static IAudioInput Open(string deviceName, int sampleRate) =>
        Environment.GetEnvironmentVariable(SimulatedAudioInput.EnvironmentVariable) is { Length: > 0 } path
            ? new SimulatedAudioInput(path, sampleRate)
            : new AudioInput(deviceName, sampleRate);

    public static IReadOnlyList<string> GetDevices()
    {
        var devices = new List<string> { DefaultDevice };
        try
        {
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
                devices.Add(WaveInEvent.GetCapabilities(i).ProductName);
        }
        catch (Exception)
        {
            // WinMM unavailable (non-Windows): only WAV decoding will work.
        }
        return devices;
    }

    /// <summary>
    /// Wall-clock time since start minus the audio actually delivered. It stays at about the buffer length
    /// (≤ 0.25 s) while capture is healthy; anything more is audio that never reached us.
    /// </summary>
    public TimeSpan Shortfall =>
        _clock.Elapsed - TimeSpan.FromSeconds((double)Interlocked.Read(ref _samplesReceived) / _sampleRate);

    public void Start()
    {
        _wave.StartRecording();
        _clock.Restart();
    }

    public void Dispose()
    {
        try { _wave.StopRecording(); }
        catch (Exception) { /* already stopped or device gone */ }
        _wave.Dispose();
    }

    private static int DeviceNumber(string name)
    {
        var devices = GetDevices();
        for (var i = 1; i < devices.Count; i++)
            if (devices[i] == name) return i - 1;
        return -1; // WAVE_MAPPER = system default
    }
}
