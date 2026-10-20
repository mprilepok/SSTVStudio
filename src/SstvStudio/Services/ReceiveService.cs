// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Threading.Channels;
using SstvStudio.Core.Audio;
using SstvStudio.Core.Decoding;
using SstvStudio.Core.Modes;

namespace SstvStudio.Services;

/// <summary>Audio of one received picture, from shortly before its VIS header to the end of the picture.</summary>
public sealed record ReceivedAudio(short[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
    public float[] ToFloats() => Array.ConvertAll(Samples, s => s / 32768f);
}

/// <summary>
/// How to decode a recording: <paramref name="Mode"/> null means "as the VIS header says"; otherwise that mode
/// is used (timed from the header if there is one, else by a manual start). Shift and slant adjust the picture.
/// </summary>
public sealed record DecodeOptions(SstvMode? Mode = null, double ShiftMs = 0, double SlantPpm = 0)
{
    public static DecodeOptions Default { get; } = new();

    public void ApplyTo(SstvDecoder decoder)
    {
        decoder.ForcedMode = Mode;
        decoder.ShiftMs = ShiftMs;
        decoder.SlantPpm = SlantPpm;
    }
}

/// <summary>
/// Runs the SSTV decoder on a background worker, fed either by live audio capture or by a WAV file.
/// Besides decoding, it keeps the audio of the picture in progress (delivered with
/// <see cref="ImageCompleted"/>) and can record everything heard to a WAV file.
/// All decoder, clip and recorder state is touched only on the worker; events are raised there too.
/// </summary>
public sealed class ReceiveService : IAsyncDisposable
{
    private const int FileChunk = 4096;
    // Enough to include the MMSSTV preamble (0.8 s) and VIS header (0.9 s) before the picture.
    private const double PrerollSeconds = 3;

    private readonly Channel<Action> _work = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;
    private readonly AudioClipBuffer _clip = new();
    private IAudioInput? _input;
    private SstvDecoder? _liveDecoder;
    private int _liveRate;
    private int _clipRate;
    private int _clipStart = -1; // index in _clip where the current picture's audio starts; -1 when idle
    private WavFileWriter? _recording;
    private bool _muted;

    public ReceiveService() => _worker = Task.Run(RunWorkerAsync);

    public event Action<SstvMode>? ImageStarted;
    public event Action<DecodedImage, ReceivedAudio?>? ImageCompleted;
    public event Action? ImageAborted;
    public event Action<Exception?>? InputStopped;

    /// <summary>Decoder currently being fed (live or file), for level, progress and snapshots.</summary>
    public SstvDecoder? ActiveDecoder { get; private set; }
    /// <summary>Spectrum of the live input, for the waterfall (rows are taken by the UI).</summary>
    public SpectrumAnalyzer? Spectrum { get; private set; }
    public bool IsListening => _input is not null;
    public bool IsDecodingFile { get; private set; }
    public bool IsRecording { get; private set; }
    /// <summary>Audio lost before it reached us since listening started (see <see cref="IAudioInput.Shortfall"/>).</summary>
    public TimeSpan? InputShortfall => _input?.Shortfall;
    /// <summary>Work items waiting for the decoder; each live item is ~50 ms of audio.</summary>
    public int Backlog => _work.Reader.CanCount ? _work.Reader.Count : 0;
    /// <summary>Length of the running recording (read from the UI; approximate is fine).</summary>
    public TimeSpan RecordingDuration => _recording?.Duration ?? TimeSpan.Zero;
    public long RecordingBytes => _recording?.BytesWritten ?? 0;

    /// <summary>While muted (during our own TX) input is discarded and a picture in progress is abandoned.</summary>
    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (value && _liveDecoder is { } decoder) _work.Writer.TryWrite(decoder.Stop);
        }
    }

    public void StartListening(string device, int sampleRate)
    {
        if (_input is not null) return;
        if (_liveDecoder is null || _liveRate != sampleRate)
        {
            _liveDecoder = CreateDecoder(sampleRate);
            _liveRate = sampleRate;
        }
        ActiveDecoder = _liveDecoder;

        var decoder = _liveDecoder;
        var spectrum = Spectrum = new SpectrumAnalyzer(sampleRate);
        _work.Writer.TryWrite(() => ResetClip(sampleRate));
        var input = AudioInput.Open(device, sampleRate);
        input.SamplesAvailable += samples =>
        {
            if (_muted) return;
            _work.Writer.TryWrite(() =>
            {
                _recording?.Write(samples);
                spectrum.Push(samples);
                Feed(decoder, samples);
            });
        };
        input.Stopped += ex => InputStopped?.Invoke(ex);
        input.Start();
        _input = input;
    }

    public void StopListening()
    {
        StopRecording();
        _input?.Dispose();
        _input = null;
        if (_liveDecoder is { } decoder) _work.Writer.TryWrite(decoder.Stop);
    }

    /// <summary>Records all live input (except while muted for our own TX) to a WAV file until stopped.</summary>
    public void StartRecording(string path)
    {
        if (!IsListening) throw new InvalidOperationException("Start listening before recording.");
        if (IsRecording) return;
        // Create the file here so errors (bad folder, no permission) reach the caller, not the worker.
        var writer = new WavFileWriter(File.Create(path), _liveRate);
        _work.Writer.TryWrite(() => _recording = writer);
        IsRecording = true;
    }

    public void StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        _work.Writer.TryWrite(() =>
        {
            _recording?.Dispose();
            _recording = null;
        });
    }

    /// <summary>
    /// Starts receiving <paramref name="mode"/> now on the live input, for a transmission whose header was
    /// missed. A header heard later still takes over.
    /// </summary>
    public void StartManual(SstvMode mode)
    {
        if (_liveDecoder is { } decoder && IsListening) _work.Writer.TryWrite(() => decoder.StartManual(mode));
    }

    /// <summary>Ends the picture currently being received and delivers what has arrived so far.</summary>
    public void FinishCurrentImage()
    {
        if (ActiveDecoder is { } decoder) _work.Writer.TryWrite(decoder.Stop);
    }

    /// <summary>
    /// Decodes every picture in a WAV file, delivered through <see cref="ImageCompleted"/>. With a forced mode and
    /// no header in the file, decodes it from the start as that mode. Not available while listening.
    /// </summary>
    public async Task DecodeFileAsync(string path, DecodeOptions options, CancellationToken ct = default)
    {
        if (IsListening) throw new InvalidOperationException("Stop listening before decoding a file.");
        var (samples, rate) = await Task.Run(() =>
        {
            using var stream = File.OpenRead(path);
            return WavReader.Read(stream);
        }, ct);

        IsDecodingFile = true;
        try
        {
            var found = await DecodeSamplesAsync(samples, rate, options, manual: false, ct);
            if (found == 0 && options.Mode is not null)
                await DecodeSamplesAsync(samples, rate, options, manual: true, ct);
        }
        finally
        {
            IsDecodingFile = false;
            ActiveDecoder = _liveDecoder;
        }
    }

    /// <summary>Runs one pass over a recording on the worker; returns how many pictures it delivered.</summary>
    private async Task<int> DecodeSamplesAsync(float[] samples, int rate, DecodeOptions options, bool manual, CancellationToken ct)
    {
        var decoder = CreateDecoder(rate);
        options.ApplyTo(decoder);
        var found = 0;
        decoder.ImageCompleted += _ => found++;
        ActiveDecoder = decoder;

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Writer.TryWrite(() =>
        {
            ResetClip(rate);
            if (manual)
            {
                decoder.DetectVis = false;
                decoder.StartManual(options.Mode!);
            }
        });
        for (var i = 0; i < samples.Length; i += FileChunk)
        {
            var start = i;
            _work.Writer.TryWrite(() =>
            {
                if (!ct.IsCancellationRequested)
                    Feed(decoder, samples.AsSpan(start, Math.Min(FileChunk, samples.Length - start)));
            });
        }
        // A second of silence lets the final lines through, then deliver anything still in progress.
        _work.Writer.TryWrite(() => Feed(decoder, new float[rate]));
        _work.Writer.TryWrite(decoder.Stop);
        _work.Writer.TryWrite(() => done.TrySetResult());
        await done.Task.WaitAsync(ct);
        return found;
    }

    /// <summary>
    /// Decodes a saved picture's audio again with different options, off the live worker. Returns the pictures
    /// (normally one); nothing is raised through the events.
    /// </summary>
    public static Task<List<DecodedImage>> ReDecodeAsync(ReceivedAudio audio, DecodeOptions options) => Task.Run(() =>
    {
        var samples = audio.ToFloats();
        var results = Run(manual: false);
        if (results.Count == 0 && options.Mode is not null) results = Run(manual: true);
        return results;

        List<DecodedImage> Run(bool manual)
        {
            var decoder = new SstvDecoder(audio.SampleRate);
            options.ApplyTo(decoder);
            if (manual)
            {
                decoder.DetectVis = false;
                decoder.StartManual(options.Mode!);
            }
            var found = new List<DecodedImage>();
            decoder.ImageCompleted += found.Add;
            for (var i = 0; i < samples.Length; i += FileChunk)
                decoder.Process(samples.AsSpan(i, Math.Min(FileChunk, samples.Length - i)));
            decoder.Process(new float[audio.SampleRate]);
            decoder.Stop();
            return found;
        }
    });

    // ---- worker thread only below ----

    private void Feed(SstvDecoder decoder, ReadOnlySpan<float> samples)
    {
        _clip.Append(samples);
        decoder.Process(samples);
        if (_clipStart < 0) _clip.KeepLast((int)(PrerollSeconds * _clipRate));
    }

    private void ResetClip(int sampleRate)
    {
        _clip.Clear();
        _clipRate = sampleRate;
        _clipStart = -1;
    }

    private SstvDecoder CreateDecoder(int sampleRate)
    {
        var decoder = new SstvDecoder(sampleRate);
        decoder.ImageStarted += mode =>
        {
            // The VIS header ended inside the chunk just appended; keep the pre-roll before it.
            _clipStart = Math.Max(0, _clip.Count - (int)(PrerollSeconds * _clipRate));
            ImageStarted?.Invoke(mode);
        };
        decoder.ImageCompleted += image =>
        {
            var audio = _clipStart >= 0 ? new ReceivedAudio(_clip.CopyFrom(_clipStart), _clipRate) : null;
            _clipStart = -1;
            ImageCompleted?.Invoke(image, audio);
        };
        decoder.ImageAborted += () =>
        {
            _clipStart = -1;
            ImageAborted?.Invoke();
        };
        return decoder;
    }

    private async Task RunWorkerAsync()
    {
        await foreach (var action in _work.Reader.ReadAllAsync())
        {
            try { action(); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError($"SSTV receiver: {ex}"); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopListening();
        _work.Writer.TryComplete();
        await _worker;
    }
}
