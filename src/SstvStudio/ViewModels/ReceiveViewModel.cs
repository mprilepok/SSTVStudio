// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SstvStudio.Core.Audio;
using SstvStudio.Core.Decoding;
using SstvStudio.Core.Imaging;
using SstvStudio.Core.Modes;
using SstvStudio.Services;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace SstvStudio.ViewModels;

/// <summary>A received picture. <see cref="Raw"/> is the decoder output; <see cref="Image"/> has the noise filter applied.</summary>
public sealed partial class ReceivedPicture : ObservableObject
{
    public ReceivedPicture(DecodedImage result, NoiseFilter filter, string? note = null)
    {
        Raw = result.Image;
        Mode = result.Mode;
        StartedUtc = result.StartedUtc;
        Complete = result.Complete;
        TuningOffsetHz = result.TuningOffsetHz;
        ClockErrorPpm = result.ClockErrorPpm;
        Note = note;
        ApplyFilter(filter);
    }

    public RgbImage Raw { get; }
    public SstvMode Mode { get; }
    public DateTime StartedUtc { get; }
    public bool Complete { get; }
    public double TuningOffsetHz { get; }
    public double ClockErrorPpm { get; }
    /// <summary>The rig's dial while the picture came in; null if unknown (no rig, a WAV file, or retuned meanwhile).</summary>
    public RigDial? Dial { get; init; }
    public string? Note { get; }
    [ObservableProperty] private RgbImage _image = null!;
    [ObservableProperty] private AvaloniaBitmap _bitmap = null!;
    public string? FilePath { get; set; }
    /// <summary>Audio of the picture; kept in memory only for the most recent pictures.</summary>
    public ReceivedAudio? Audio { get; set; }
    public string? AudioPath { get; set; }
    public bool HasAudio => Audio is not null || (AudioPath is not null && File.Exists(AudioPath));
    public string Title => $"{StartedUtc:HH:mm}Z  {Mode.Name}";
    public string Subtitle =>
        $"{StartedUtc:yyyy-MM-dd}{(Complete ? "" : " · partial")}{(Audio is null ? "" : " · audio")}{(Note is null ? "" : " · " + Note)}";

    public void ApplyFilter(NoiseFilter filter)
    {
        Image = ImageFilters.Denoise(Raw, filter);
        Bitmap = ImageComposer.ToAvaloniaBitmap(Image);
    }
}

/// <summary>A mode choice for manual start / re-decode; a null mode means "as the VIS header says".</summary>
public sealed record ModeChoice(string Label, SstvMode? Mode)
{
    public override string ToString() => Label;
}

public partial class ReceiveViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#A6E3A1"));
    private static readonly IBrush YellowBrush = new SolidColorBrush(Color.Parse("#F9E2AF"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#F38BA8"));
    private const int PicturesWithAudioInMemory = 10;
    private const double RetuneThresholdHz = Retune.ToleranceHz;

    private readonly AppSettings _settings;
    private readonly ReceiveService _service = new();
    private readonly WaterfallRenderer _waterfall = new();
    private readonly DispatcherTimer _timer;
    private int _shownLines = -1;
    private string? _recordingPath;
    private RigDial? _liveDial;      // dial when the live picture started
    private RigDial? _measuredDial;  // dial that MeasuredOffsetHz was measured on
    private bool _decodingFile;

    public ReceiveViewModel(AppSettings settings, QsoViewModel qso)
    {
        _settings = settings;
        Qso = qso;
        Zoom = new ZoomViewModel(settings, nameof(AppSettings.RxZoomPercent), () => settings.RxZoomPercent, v => settings.RxZoomPercent = v);
        _settings.PropertyChanged += OnSettingsChanged;
        InputDevices = AudioInput.GetDevices();
        ModeChoices = [new ModeChoice("Auto (VIS header)", null), .. SstvModes.All.Select(m => new ModeChoice(m.Name, m))];
        _redecodeMode = ModeChoices[0];
        _manualMode = SstvModes.MartinM1;

        _service.ImageStarted += mode => Dispatcher.UIThread.Post(() => OnImageStarted(mode));
        _service.ImageCompleted += (img, audio) => Dispatcher.UIThread.Post(() => OnImageCompleted(img, audio));
        _service.ImageAborted += () => Dispatcher.UIThread.Post(() =>
        {
            IsReceiving = false;
            Status = "No picture followed (false trigger, or nothing found for the manual start).";
        });
        _service.InputStopped += ex => Dispatcher.UIThread.Post(() =>
        {
            if (ex is not null) ErrorMessage = $"Audio input stopped: {ex.Message}";
        });
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();

        if (settings.RxAutoStart) StartListening();
    }

    /// <summary>Raised when the user wants to reply with a received picture.</summary>
    public event Action<ReceivedPicture>? UseAsTxRequested;
    /// <summary>Raised with the audio offset (Hz, + = too high) and the dial it was measured on.</summary>
    public event Func<double, RigDial?, Task<RetuneResult>>? RetuneRequested;
    /// <summary>The rig's dial now, or null when no rig is connected.</summary>
    public Func<RigDial?>? DialProvider { get; set; }

    public IReadOnlyList<string> InputDevices { get; private set; }
    /// <summary>The QSO being worked, shared with the Transmit tab.</summary>
    public QsoViewModel Qso { get; }
    public ObservableCollection<ReceivedPicture> Gallery { get; } = [];
    public IReadOnlyList<SstvMode> Modes => SstvModes.All;
    public IReadOnlyList<ModeChoice> ModeChoices { get; }
    public IReadOnlyList<NoiseFilter> NoiseFilters { get; } = Enum.GetValues<NoiseFilter>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand), nameof(StartManualCommand))]
    private bool _isListening;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _recordingText = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixTuningCommand))]
    private bool _isReceiving;
    [ObservableProperty] private string _status = "Not listening";
    [ObservableProperty] private string _modeText = "";
    [ObservableProperty] private double _lineProgress;
    [ObservableProperty] private double _levelPercent;
    [ObservableProperty] private string _levelText = "";
    [ObservableProperty] private IBrush _levelBrush = GreenBrush;
    [ObservableProperty] private string _frequencyText = "";
    [ObservableProperty] private AvaloniaBitmap? _liveImage;
    [ObservableProperty] private string? _errorMessage;
    /// <summary>Warning when audio is being lost or the decoder can't keep up; empty when healthy.</summary>
    [ObservableProperty] private string _inputHealth = "";

    // --- Tuning aid ---
    [ObservableProperty] private AvaloniaBitmap? _waterfallImage;
    [ObservableProperty] private string _tuningText = "Tuning: shown after a picture header";
    [ObservableProperty] private IBrush _tuningBrush = YellowBrush;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixTuningCommand))]
    private double? _measuredOffsetHz;
    [ObservableProperty] private string _retuneStatus = "";

    // --- Manual start / re-decode ---
    [ObservableProperty] private SstvMode _manualMode;
    [ObservableProperty] private ModeChoice _redecodeMode;
    [ObservableProperty] private decimal _shiftMs;
    [ObservableProperty] private decimal _slantPpm;
    [ObservableProperty] private bool _isRedecoding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayImage), nameof(HasSelection), nameof(HasSelectedAudio))]
    [NotifyCanExecuteChangedFor(nameof(UseAsTxCommand), nameof(RedecodeCommand))]
    private ReceivedPicture? _selected;

    /// <summary>The gallery selection, or the live picture when nothing is selected.</summary>
    public AvaloniaBitmap? DisplayImage => Selected?.Bitmap ?? LiveImage;
    public bool HasSelection => Selected is not null;
    public bool HasSelectedAudio => Selected?.HasAudio == true;

    partial void OnLiveImageChanged(AvaloniaBitmap? value) => OnPropertyChanged(nameof(DisplayImage));

    partial void OnSelectedChanged(ReceivedPicture? oldValue, ReceivedPicture? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnSelectedPictureChanged;
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedPictureChanged;
            ShowTuning(newValue.TuningOffsetHz, newValue.Dial, $"{newValue.Mode.Name} {newValue.StartedUtc:HH:mm}Z");
        }
    }

    private void OnSelectedPictureChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReceivedPicture.Bitmap)) OnPropertyChanged(nameof(DisplayImage));
    }

    /// <summary>
    /// Noise filter as a ComboBox index. An index rather than the enum item, because while the Receive tab is
    /// detached (another tab shown) the ComboBox reports "nothing selected"; as an item that is null, which can't
    /// be stored in the enum setting (InvalidCastException). The −1 index is simply ignored.
    /// </summary>
    public int NoiseFilterIndex
    {
        get => (int)_settings.RxNoiseFilter;
        set
        {
            if (value >= 0 && value < NoiseFilters.Count) _settings.RxNoiseFilter = NoiseFilters[value];
        }
    }

    /// <summary>Zoom of the picture viewer on this tab.</summary>
    public ZoomViewModel Zoom { get; }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppSettings.RxNoiseFilter)) return;
        OnPropertyChanged(nameof(NoiseFilterIndex));
        foreach (var picture in Gallery) picture.ApplyFilter(_settings.RxNoiseFilter);
        _shownLines = -1; // refresh the live picture too
    }

    /// <summary>Set by the transmit side: our own TX must not be decoded.</summary>
    public bool Muted
    {
        get => _service.Muted;
        set => _service.Muted = value;
    }

    // ------------------------------------------------------------- listening & recording

    [RelayCommand]
    private void ToggleListening()
    {
        if (IsListening) StopListening();
        else StartListening();
    }

    private void StartListening()
    {
        try
        {
            _service.StartListening(_settings.RxAudioDevice, _settings.SampleRate);
            IsListening = true;
            ErrorMessage = null;
            Status = "Listening — waiting for a VIS header…";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open audio input: {ex.Message}";
        }
    }

    private void StopListening()
    {
        var recording = IsRecording;
        _service.StopListening(); // also closes any recording
        IsListening = false;
        Status = "Not listening";
        if (recording) FinishRecordingUi();
    }

    [RelayCommand(CanExecute = nameof(IsListening))]
    private void ToggleRecording()
    {
        if (IsRecording)
        {
            _service.StopRecording();
            FinishRecordingUi();
            return;
        }
        try
        {
            Directory.CreateDirectory(_settings.RxSaveFolder);
            _recordingPath = Path.Combine(_settings.RxSaveFolder, $"rx_{DateTime.UtcNow:yyyyMMdd_HHmmss}Z.wav");
            _service.StartRecording(_recordingPath);
            IsRecording = true;
            RecordingText = "● REC";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not start recording: {ex.Message}";
        }
    }

    private void FinishRecordingUi()
    {
        IsRecording = false;
        RecordingText = _recordingPath is null ? "" : $"Saved {Path.GetFileName(_recordingPath)}";
    }

    [RelayCommand]
    private void FinishImage() => _service.FinishCurrentImage();

    // ------------------------------------------------------------- manual start & re-decode

    /// <summary>Starts receiving the chosen mode now, for a picture whose header was missed.</summary>
    [RelayCommand(CanExecute = nameof(IsListening))]
    private void StartManual()
    {
        _service.StartManual(ManualMode);
        Status = $"Manual start: looking for {ManualMode.Name} sync pulses…";
    }

    private DecodeOptions CurrentOptions() => new(RedecodeMode.Mode, (double)ShiftMs, (double)SlantPpm);

    [RelayCommand]
    private void ResetAdjustments()
    {
        RedecodeMode = ModeChoices[0];
        ShiftMs = 0;
        SlantPpm = 0;
    }

    private bool CanRedecode() => Selected?.HasAudio == true && !IsRedecoding;

    /// <summary>Decodes the selected picture's audio again with the mode / shift / slant settings.</summary>
    [RelayCommand(CanExecute = nameof(CanRedecode))]
    private async Task RedecodeAsync()
    {
        if (Selected is not { } picture) return;
        IsRedecoding = true;
        RedecodeCommand.NotifyCanExecuteChanged();
        try
        {
            var audio = picture.Audio ?? LoadAudio(picture.AudioPath!);
            var results = await ReceiveService.ReDecodeAsync(audio, CurrentOptions());
            if (results.Count == 0)
            {
                Status = "Re-decode found no picture with these settings.";
                return;
            }
            foreach (var result in results)
            {
                var redone = new ReceivedPicture(result with { StartedUtc = picture.StartedUtc }, _settings.RxNoiseFilter, "re-decoded")
                {
                    Audio = audio,
                    Dial = picture.Dial,
                };
                Gallery.Insert(0, redone);
                Selected = redone;
            }
            Status = $"Re-decoded as {results[0].Mode.Name}" + (results[0].Complete ? "" : " (partial)");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Re-decode failed: {ex.Message}";
        }
        finally
        {
            IsRedecoding = false;
            RedecodeCommand.NotifyCanExecuteChanged();
        }
    }

    private static ReceivedAudio LoadAudio(string path)
    {
        using var stream = File.OpenRead(path);
        var (samples, rate) = WavReader.Read(stream);
        return new ReceivedAudio(Array.ConvertAll(samples, s => (short)Math.Clamp(s * 32767, short.MinValue, short.MaxValue)), rate);
    }

    /// <summary>Decodes every picture in a WAV recording (only while not listening), with the re-decode settings.</summary>
    public async Task DecodeFileAsync(string path)
    {
        var before = Gallery.Count;
        Status = $"Decoding {Path.GetFileName(path)}…";
        _decodingFile = true; // the rig's dial says nothing about a recording
        try
        {
            await _service.DecodeFileAsync(path, CurrentOptions());
            // Completion events are posted to the UI thread; let them land before counting.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var found = Gallery.Count - before;
            Status = found == 0 ? $"No SSTV picture found in {Path.GetFileName(path)}." : $"Decoded {found} picture(s) from {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not decode file: {ex.Message}";
            Status = "Decoding failed";
        }
        finally
        {
            _decodingFile = false;
        }
    }

    // ------------------------------------------------------------- tuning aid

    private void ShowTuning(double offsetHz, RigDial? dial, string source)
    {
        MeasuredOffsetHz = offsetHz;
        _measuredDial = dial;
        var abs = Math.Abs(offsetHz);
        TuningText = abs < RetuneThresholdHz
            ? $"Tuning OK ({offsetHz:+0;-0;0} Hz) — {source}"
            : $"Signal {abs:0} Hz too {(offsetHz > 0 ? "high" : "low")} — {source}{(IsReceiving ? " (fix it when the picture ends)" : "")}";
        TuningBrush = abs < RetuneThresholdHz ? GreenBrush : abs < 60 ? YellowBrush : RedBrush;
    }

    // Not mid-picture: the decoder keeps its measured offset, so the rest of the picture would come out off-colour.
    private bool CanFixTuning() => !IsReceiving && MeasuredOffsetHz is { } o && Math.Abs(o) >= RetuneThresholdHz;

    /// <summary>Retunes the rig through Hamlib so the next picture from this station is centred.</summary>
    [RelayCommand(CanExecute = nameof(CanFixTuning))]
    private async Task FixTuningAsync()
    {
        if (MeasuredOffsetHz is not { } offset || RetuneRequested is null) return;
        var result = await RetuneRequested(offset, _measuredDial);
        RetuneStatus = result.Message;
        if (result.Corrected)
        {
            MeasuredOffsetHz = 0;
            TuningText = "Tuning corrected — the next picture will confirm it";
            TuningBrush = GreenBrush;
        }
    }

    // ------------------------------------------------------------- gallery

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void UseAsTx()
    {
        if (Selected is { } picture) UseAsTxRequested?.Invoke(picture);
    }

    /// <summary>Shows the selected picture in Explorer, or the save folder when it was not saved.</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            var arguments = Selected?.FilePath is { } file && File.Exists(file)
                ? $"/select,\"{file}\""
                : $"\"{Directory.CreateDirectory(_settings.RxSaveFolder).FullName}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShowLive() => Selected = null;

    public void SavePicture(ReceivedPicture picture, string path)
    {
        try
        {
            ImageComposer.SavePng(picture.Image, path);
            picture.FilePath = path;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not save picture: {ex.Message}";
        }
    }

    /// <summary>Writes the picture's audio to <paramref name="path"/> (from memory, or by copying its saved WAV).</summary>
    public void SaveAudio(ReceivedPicture picture, string path)
    {
        try
        {
            if (picture.Audio is { } audio)
                WavFileWriter.Write(path, audio.Samples, audio.SampleRate);
            else if (picture.AudioPath is { } existing && File.Exists(existing))
                File.Copy(existing, path, overwrite: true);
            else
                throw new InvalidOperationException("The audio of this picture is no longer available.");
            picture.AudioPath = path;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not save audio: {ex.Message}";
        }
    }

    private void OnImageStarted(SstvMode mode)
    {
        IsReceiving = true;
        _liveDial = _decodingFile ? null : DialProvider?.Invoke();
        Selected = null; // show the live picture
        ModeText = $"{mode.Name} · {mode.Width}×{mode.Height}";
        _shownLines = -1;
        LineProgress = 0;
    }

    private void OnImageCompleted(DecodedImage result, ReceivedAudio? audio)
    {
        IsReceiving = false;
        LineProgress = result.Complete ? 100 : LineProgress;
        // A dial that changed during the picture leaves the measured offset meaningless.
        var dial = !_decodingFile && _liveDial is { } started && DialProvider?.Invoke() == started ? started : null;
        _liveDial = null;
        var picture = new ReceivedPicture(result, _settings.RxNoiseFilter) { Audio = audio, Dial = dial };

        if (_settings.RxAutoSave)
        {
            try
            {
                Directory.CreateDirectory(_settings.RxSaveFolder);
                var baseName = Path.Combine(_settings.RxSaveFolder,
                    $"{result.StartedUtc:yyyyMMdd_HHmmss}Z_{result.Mode.Name.Replace(' ', '-')}{(result.Complete ? "" : "_partial")}");
                SavePicture(picture, baseName + ".png");
                if (_settings.RxSaveAudio && audio is not null)
                    SaveAudio(picture, baseName + ".wav");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Auto-save failed: {ex.Message}";
            }
        }

        Gallery.Insert(0, picture);
        // A long PD picture is ~30 MB of audio; keep it in memory only for the latest few.
        foreach (var older in Gallery.Skip(PicturesWithAudioInMemory)) older.Audio = null;
        Selected = picture;
        LiveImage = picture.Bitmap;
        Status = $"Received {result.Mode.Name} at {result.StartedUtc:HH:mm}Z{(result.Complete ? "" : " (partial)")}"
                 + (IsListening ? " — listening for the next picture…" : "");
    }

    private void Tick()
    {
        if (IsRecording)
            RecordingText = $"● REC {_service.RecordingDuration:h\\:mm\\:ss} · {_service.RecordingBytes / 1048576.0:0.0} MB";

        // Healthy capture runs ≤ 0.25 s behind the clock (buffering); more means Windows dropped input audio.
        var lost = (_service.InputShortfall ?? TimeSpan.Zero).TotalSeconds - 0.3;
        var behind = _service.Backlog * 0.05;
        InputHealth = lost > 0.2 ? $"⚠ {lost:0.0} s of input audio lost since listening started"
                    : behind > 1 ? $"⚠ Decoder {behind:0.0} s behind"
                    : "";

        UpdateWaterfall();

        var decoder = _service.ActiveDecoder;
        if (decoder is null) return;

        var db = decoder.LevelDb;
        LevelPercent = Math.Clamp((db + 60) / 60 * 100, 0, 100);
        LevelText = $"{db:0} dB";
        // Aim for roughly -30…-10 dBFS; near 0 dBFS the codec clips.
        LevelBrush = db > -6 ? RedBrush : db < -45 ? YellowBrush : GreenBrush;
        FrequencyText = decoder.FrequencyHz > 0 ? $"{decoder.FrequencyHz:0} Hz" : "";

        if (decoder.State != DecoderState.Receiving || decoder.CurrentMode is not { } mode) return;
        ShowTuning(decoder.TuningOffsetHz, _liveDial, "this picture");
        var lines = decoder.LineCount;
        LineProgress = 100.0 * lines / mode.Height;
        Status = $"Receiving {mode.Name} · line {lines}/{mode.Height}";
        if (lines == _shownLines) return;
        _shownLines = lines;
        if (decoder.Snapshot() is { } snapshot)
        {
            var old = LiveImage;
            LiveImage = ImageComposer.ToAvaloniaBitmap(ImageFilters.Denoise(snapshot, _settings.RxNoiseFilter));
            if (old is not null && Gallery.All(g => g.Bitmap != old)) old.Dispose();
        }
    }

    private void UpdateWaterfall()
    {
        if (_service.Spectrum is not { } spectrum) return;
        var added = false;
        while (spectrum.TryTakeRow() is { } row)
        {
            _waterfall.AddRow(row);
            added = true;
        }
        if (!added) return;
        var old = WaterfallImage;
        WaterfallImage = _waterfall.ToBitmap();
        old?.Dispose();
    }

    public void RefreshDevices()
    {
        InputDevices = AudioInput.GetDevices();
        OnPropertyChanged(nameof(InputDevices));
    }

    public async ValueTask DisposeAsync()
    {
        _timer.Stop();
        await _service.DisposeAsync();
    }
}
