// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;

namespace SstvStudio.Services;

/// <summary>
/// Developer aid: plays a WAV file as if it were live input, paced by the wall clock in 50 ms chunks and looped
/// with 3 s of silence. Enabled by setting the environment variable <see cref="EnvironmentVariable"/> to the
/// file's path; nothing else in the app changes.
/// </summary>
public sealed class SimulatedAudioInput : IAudioInput
{
    public const string EnvironmentVariable = "SSTVSTUDIO_SIMULATED_INPUT";

    private readonly float[] _samples;
    private readonly int _sampleRate;
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private readonly CancellationTokenSource _cts = new();
    private long _sent;

    public SimulatedAudioInput(string path, int sampleRate)
    {
        using var stream = File.OpenRead(path);
        var (samples, rate) = WavReader.Read(stream);
        if (rate != sampleRate)
            throw new InvalidOperationException($"Simulated input {Path.GetFileName(path)} is {rate} Hz; settings use {sampleRate} Hz.");
        _samples = [.. samples, .. new float[sampleRate * 3]];
        _sampleRate = sampleRate;
    }

    public event Action<float[]>? SamplesAvailable;
    public event Action<Exception?>? Stopped;

    public TimeSpan Shortfall => _clock.Elapsed - TimeSpan.FromSeconds((double)Interlocked.Read(ref _sent) / _sampleRate);

    public void Start()
    {
        _clock.Restart();
        var thread = new Thread(Run) { IsBackground = true, Name = "Simulated audio input" };
        thread.Start();
    }

    private void Run()
    {
        var chunk = _sampleRate / 20;
        var position = 0;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // Like a sound card: deliver whatever is due by the clock, in whole 50 ms buffers.
                while ((long)(_clock.Elapsed.TotalSeconds * _sampleRate) - Interlocked.Read(ref _sent) >= chunk)
                {
                    var buffer = new float[chunk];
                    for (var i = 0; i < chunk; i++)
                    {
                        buffer[i] = _samples[position];
                        position = (position + 1) % _samples.Length;
                    }
                    Interlocked.Add(ref _sent, chunk);
                    SamplesAvailable?.Invoke(buffer);
                }
                Thread.Sleep(10);
            }
            Stopped?.Invoke(null);
        }
        catch (Exception ex)
        {
            Stopped?.Invoke(ex);
        }
    }

    public void Dispose() => _cts.Cancel();
}
