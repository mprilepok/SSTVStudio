// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.IO.Ports;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using SstvStudio.Core.Audio;
using SstvStudio.Core.Encoding;
using SstvStudio.Core.Modes;
using SstvStudio.Hamlib;
using SstvStudio.Services;
using SstvStudio.Wavelog;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace SstvStudio.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private const string RadioName = "SSTV Studio";

    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#A6E3A1"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#F38BA8"));
    private static readonly IBrush YellowBrush = new SolidColorBrush(Color.Parse("#F9E2AF"));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#6C7086"));
    private const double CurrentBarMaxAmps = 25;

    private readonly RigService _rig = new();
    private readonly TransmitController _tx;
    private readonly WavelogClient _wavelog = new(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
    private readonly QsoLogger _logger;
    private readonly DispatcherTimer _progressTimer;
    private SKBitmap? _source;
    private SKBitmap? _composed;
    private CancellationTokenSource? _txCts;
    private Task? _txTask;
    private (long Freq, string Mode) _lastRadioPush;
    private TxMeterStats? _txStats;
    private int _swrTripCount;

    public MainWindowViewModel()
    {
        Settings = AppSettings.Load();
        Settings.PropertyChanged += OnSettingsChanged;
        _tx = new TransmitController(_rig);
        _logger = new QsoLogger(_wavelog);
        _rig.StatusChanged += s => Dispatcher.UIThread.Post(() => OnRigStatus(s));
        Qso = new QsoViewModel(_logger, Settings, () => SelectedMode);
        Numbers = new SettingsNumbers(Settings);
        TxZoom = new ZoomViewModel(Settings, nameof(AppSettings.TxZoomPercent), () => Settings.TxZoomPercent, v => Settings.TxZoomPercent = v);
        Qso.PropertyChanged += OnQsoChanged;
        Receive = new ReceiveViewModel(Settings, Qso);
        Receive.UseAsTxRequested += UseReceivedPicture;
        Receive.RetuneRequested += RetuneAsync;
        Receive.DialProvider = () => _rig.Status is { Connected: true } s ? new RigDial(s.FrequencyHz, s.Mode) : null;
        ErrorLog.Logged += OnErrorLogged;
        ErrorMessage = Settings.LoadProblem;
        _progressTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => UpdateProgress());

        _selectedMode = SstvModes.Find(Settings.ModeName) ?? SstvModes.MartinM1;
        _selectedProfile = Settings.OverlayProfiles.FirstOrDefault(p => p.Name == Settings.OverlayProfileName)
                           ?? Settings.OverlayProfiles[0];
        Watch(_selectedProfile);
        AudioDevices = AudioOutput.GetDevices();
        SerialPorts = SerialPort.GetPortNames();

        if (Settings.LastImagePath is { } path && File.Exists(path))
            LoadImage(path);
        else
            Recompose();

        if (Settings.RigAutoConnect)
            ConnectRig();
    }

    public AppSettings Settings { get; }
    public ReceiveViewModel Receive { get; }
    public QsoViewModel Qso { get; }
    /// <summary>The Settings tab's number fields, checked against their ranges.</summary>
    public SettingsNumbers Numbers { get; }
    /// <summary>Zoom of the TX preview.</summary>
    public ZoomViewModel TxZoom { get; }
    // Tab order: Receive first (the app opens on it), then Transmit, then Settings.
    public const int ReceiveTab = 0;
    public const int TransmitTab = 1;
    public const int SettingsTab = 2;
    [ObservableProperty] private int _selectedTab = ReceiveTab;
    public IReadOnlyList<SstvMode> Modes => SstvModes.All;
    public IReadOnlyList<ImageFit> FitModes { get; } = Enum.GetValues<ImageFit>();
    public IReadOnlyList<OverlayPosition> OverlayPositions { get; } = Enum.GetValues<OverlayPosition>();
    public IReadOnlyList<OverlayAlign> OverlayAligns { get; } = Enum.GetValues<OverlayAlign>();
    public IReadOnlyList<string> Fonts { get; } =
        FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    private OverlayProfile _selectedProfile;
    public IReadOnlyList<PttMethod> PttMethods { get; } = Enum.GetValues<PttMethod>();
    public IReadOnlyList<int> SampleRates { get; } = [44100, 48000];
    public IReadOnlyList<string> AudioDevices { get; private set; }
    public IReadOnlyList<string> SerialPorts { get; private set; }
    public ObservableCollection<WavelogStation> Stations { get; } = [];

    // --- Image / mode ---
    [ObservableProperty] private AvaloniaBitmap? _preview;
    [ObservableProperty] private string _imageName = "Test pattern";
    [ObservableProperty] private SstvMode _selectedMode;

    public string ModeInfo =>
        $"{SelectedMode.Width}×{SelectedMode.Height} · VIS {SelectedMode.VisCode} · " +
        $"{TimeSpan.FromMilliseconds(SstvEncoder.DurationMs(SelectedMode, BuildOptions())):m\\:ss} on air";

    // --- Transmit ---
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TransmitCommand), nameof(StopCommand), nameof(ToggleRigCommand))]
    private bool _isTransmitting;
    [ObservableProperty] private double _txProgress;
    [ObservableProperty] private string _txStatus = "Ready";
    [ObservableProperty] private string? _errorMessage;
    /// <summary>The banner reports an unexpected error written to errors.log; shows "Open log".</summary>
    [ObservableProperty] private bool _errorHasLog;

    // --- Rig ---
    [ObservableProperty] private bool _rigRunning;
    [ObservableProperty] private string _rigFrequency = "--.---.---";
    [ObservableProperty] private string _rigMode = "";
    [ObservableProperty] private string _rigState = "Rig offline";
    [ObservableProperty] private IBrush _rigStateBrush = DimBrush;
    [ObservableProperty] private bool _rigPtt;

    // --- TX meters ---
    [ObservableProperty] private string _powerText = "—";
    [ObservableProperty] private double _powerBar;
    [ObservableProperty] private string _swrText = "—";
    [ObservableProperty] private double _swrBar;
    [ObservableProperty] private IBrush _swrBrush = GreenBrush;
    [ObservableProperty] private string _alcText = "—";
    [ObservableProperty] private double _alcBar;
    [ObservableProperty] private IBrush _alcBrush = GreenBrush;
    [ObservableProperty] private string _currentText = "—";
    [ObservableProperty] private double _currentBar;
    [ObservableProperty] private string _powerSettingText = "";
    [ObservableProperty] private string _lastTxSummary = "";

    [ObservableProperty] private string _wavelogStatus = "";

    partial void OnSelectedModeChanged(SstvMode value)
    {
        Settings.ModeName = value.Name;
        OnPropertyChanged(nameof(ModeInfo));
        Recompose();
    }

    // Never decode our own transmission (rig monitor or audio loopback).
    partial void OnIsTransmittingChanged(bool value) => Receive.Muted = value;

    // The overlay shows QSO fields ({CALL}, {RST}, {NAME}, {GRID}, {FREQ}), so redraw when they change.
    private void OnQsoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QsoViewModel.Call) or nameof(QsoViewModel.RstSent) or nameof(QsoViewModel.Name)
            or nameof(QsoViewModel.Grid) or nameof(QsoViewModel.FrequencyMHz))
            Recompose();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.MyCall) or nameof(AppSettings.MyGrid) or nameof(AppSettings.ImageFit):
                Recompose();
                break;
            case nameof(AppSettings.SendPreamble) or nameof(AppSettings.SendCwId) or nameof(AppSettings.CwWpm):
                OnPropertyChanged(nameof(ModeInfo));
                break;
            case nameof(AppSettings.TxLevelPercent) when _tx.Current is { } synth:
                synth.Amplitude = Settings.TxLevelPercent / 100.0;
                break;
        }
    }

    // ---------------------------------------------------------------- image

    public void LoadImage(string path)
    {
        try
        {
            var bitmap = ImageComposer.Load(path) ?? throw new InvalidDataException("Unsupported image format.");
            _source?.Dispose();
            _source = bitmap;
            ImageName = Path.GetFileName(path);
            Settings.LastImagePath = path;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open image: {ex.Message}";
        }
        Recompose();
    }

    [RelayCommand]
    private void UseTestPattern()
    {
        _source?.Dispose();
        _source = null;
        ImageName = "Test pattern";
        Settings.LastImagePath = null;
        Recompose();
    }

    /// <summary>
    /// "Fix tuning" on the Receive tab: moves the rig's dial so a signal heard <paramref name="audioOffsetHz"/>
    /// too high (or low, if negative) on <paramref name="dial"/> is centred.
    /// </summary>
    private async Task<RetuneResult> RetuneAsync(double audioOffsetHz, RigDial? dial)
    {
        if (IsTransmitting) return new(false, "Not while transmitting.");
        var status = _rig.Status;
        var (target, refusal) = Retune.Plan(status, dial, audioOffsetHz);
        if (target is not { } hz) return new(false, refusal!);
        if (hz == status.FrequencyHz)
            return new(true, $"Already corrected ({RigService.FormatFrequency(hz)}).");
        try
        {
            var actual = await _rig.SetFrequencyAsync(hz);
            return Retune.Describe(status.FrequencyHz, hz, actual);
        }
        catch (Exception ex)
        {
            return new(false, $"Retune failed: {ex.Message}");
        }
    }

    /// <summary>Replies with a received picture: it becomes the TX source and the Transmit tab opens.</summary>
    private void UseReceivedPicture(ReceivedPicture picture)
    {
        _source?.Dispose();
        _source = ImageComposer.FromRgbImage(picture.Image);
        ImageName = $"Received {picture.Title}";
        Settings.LastImagePath = null;
        Recompose();
        SelectedTab = TransmitTab;
    }

    // ---------------------------------------------------------------- overlay profiles

    partial void OnSelectedProfileChanged(OverlayProfile? oldValue, OverlayProfile newValue)
    {
        if (oldValue is not null) Unwatch(oldValue);
        Watch(newValue);
        Settings.OverlayProfileName = newValue.Name;
        RefreshMoveCommands();
        Recompose();
    }

    // Any edit to the selected profile (name, a line's text/style, adding or removing lines) re-renders the preview.
    private void Watch(OverlayProfile profile)
    {
        profile.PropertyChanged += OnProfileChanged;
        profile.Lines.CollectionChanged += OnProfileLinesChanged;
        foreach (var line in profile.Lines) line.PropertyChanged += OnOverlayLineChanged;
    }

    private void Unwatch(OverlayProfile profile)
    {
        profile.PropertyChanged -= OnProfileChanged;
        profile.Lines.CollectionChanged -= OnProfileLinesChanged;
        foreach (var line in profile.Lines) line.PropertyChanged -= OnOverlayLineChanged;
    }

    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayProfile.Name) && sender is OverlayProfile p)
            Settings.OverlayProfileName = p.Name;
    }

    private void OnProfileLinesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (OverlayLine line in e.OldItems ?? Array.Empty<OverlayLine>()) line.PropertyChanged -= OnOverlayLineChanged;
        foreach (OverlayLine line in e.NewItems ?? Array.Empty<OverlayLine>()) line.PropertyChanged += OnOverlayLineChanged;
        RefreshMoveCommands();
        Recompose();
    }

    private void RefreshMoveCommands()
    {
        MoveOverlayLineUpCommand.NotifyCanExecuteChanged();
        MoveOverlayLineDownCommand.NotifyCanExecuteChanged();
    }

    private void OnOverlayLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OverlayLine.ColorValue)) Recompose();
    }

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new OverlayProfile
        {
            Name = UniqueProfileName("New overlay"),
            Lines = [OverlayLine.Create("{CALL} de {MYCALL}", OverlayPosition.Top, "#FFFF00", 11)],
        };
        Settings.OverlayProfiles.Add(profile);
        SelectedProfile = profile;
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        var copy = SelectedProfile.Clone(UniqueProfileName(SelectedProfile.Name + " copy"));
        Settings.OverlayProfiles.Add(copy);
        SelectedProfile = copy;
    }

    private bool CanDeleteProfile() => Settings.OverlayProfiles.Count > 1;

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile()
    {
        var index = Settings.OverlayProfiles.IndexOf(SelectedProfile);
        var doomed = SelectedProfile;
        SelectedProfile = Settings.OverlayProfiles[index == 0 ? 1 : index - 1];
        Settings.OverlayProfiles.Remove(doomed);
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Adds back any of the built-in CQ / Reply / 73 profiles that are missing; never overwrites.</summary>
    [RelayCommand]
    private void RestoreDefaultProfiles()
    {
        foreach (var profile in OverlayProfile.Defaults())
            if (Settings.OverlayProfiles.All(p => p.Name != profile.Name))
                Settings.OverlayProfiles.Add(profile);
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddOverlayLine() =>
        SelectedProfile.Lines.Add(OverlayLine.Create("", OverlayPosition.Bottom, "#FFFFFF"));

    [RelayCommand]
    private void RemoveOverlayLine(OverlayLine line) => SelectedProfile.Lines.Remove(line);

    // Order matters: lines sharing a position stack in list order.
    private bool CanMoveOverlayLineUp(OverlayLine? line) => line is not null && SelectedProfile.Lines.IndexOf(line) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveOverlayLineUp))]
    private void MoveOverlayLineUp(OverlayLine line)
    {
        var index = SelectedProfile.Lines.IndexOf(line);
        if (index > 0) SelectedProfile.Lines.Move(index, index - 1);
    }

    private bool CanMoveOverlayLineDown(OverlayLine? line) =>
        line is not null && SelectedProfile.Lines.IndexOf(line) is var i && i >= 0 && i < SelectedProfile.Lines.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveOverlayLineDown))]
    private void MoveOverlayLineDown(OverlayLine line)
    {
        var index = SelectedProfile.Lines.IndexOf(line);
        if (index >= 0 && index < SelectedProfile.Lines.Count - 1) SelectedProfile.Lines.Move(index, index + 1);
    }

    private string UniqueProfileName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Settings.OverlayProfiles.Any(p => p.Name == name); i++) name = $"{baseName} {i}";
        return name;
    }

    private void Recompose()
    {
        var mode = SelectedMode;
        var overlay = SelectedProfile.Lines.Select(l => (ExpandTemplate(l.Text), l)).ToList();
        var composed = ImageComposer.Compose(_source, mode.Width, mode.Height, Settings.ImageFit, overlay);
        _composed?.Dispose();
        _composed = composed;
        var old = Preview;
        Preview = ImageComposer.ToAvaloniaBitmap(composed);
        old?.Dispose();
    }

    /// <summary>Fills placeholders; a line whose placeholders are all still empty is dropped (no lonely "de").</summary>
    private string ExpandTemplate(string template)
    {
        var now = DateTime.UtcNow;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CALL"] = Qso.Call.Trim().ToUpperInvariant(),
            ["MYCALL"] = Settings.MyCall.Trim().ToUpperInvariant(),
            ["MYGRID"] = Settings.MyGrid.Trim().ToUpperInvariant(),
            ["GRID"] = Qso.Grid.Trim().ToUpperInvariant(),
            ["RST"] = Qso.RstSent.Trim(),
            ["NAME"] = Qso.Name.Trim(),
            ["MODE"] = SelectedMode.Name,
            ["DATE"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["TIME"] = now.ToString("HH:mm", CultureInfo.InvariantCulture) + "Z",
            ["FREQ"] = Qso.FrequencyMHz,
        };

        var placeholders = 0;
        var filled = 0;
        var text = PlaceholderRegex().Replace(template, m =>
        {
            if (!values.TryGetValue(m.Groups[1].Value, out var value)) return m.Value;
            placeholders++;
            if (value.Length > 0) filled++;
            return value;
        });
        return placeholders > 0 && filled == 0 ? "" : text.Trim();
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRegex();

    // ---------------------------------------------------------------- transmit

    private TransmitOptions BuildOptions() => new(
        SendPreamble: Settings.SendPreamble,
        CwId: Settings.SendCwId && !string.IsNullOrWhiteSpace(Settings.MyCall) ? "DE " + Settings.MyCall.Trim() : null,
        CwWpm: Settings.CwWpm);

    private TransmitRequest BuildRequest()
    {
        Recompose(); // refresh {TIME}/{DATE} right before going on air
        return new TransmitRequest(SelectedMode, ImageComposer.ToRgbImage(_composed!), BuildOptions(), Settings);
    }

    private bool CanTransmit() => !IsTransmitting;

    [RelayCommand(CanExecute = nameof(CanTransmit))]
    private async Task TransmitAsync()
    {
        ErrorMessage = null;
        if (Settings.PttMethod == PttMethod.Rigctld && !_rig.Status.Connected)
        {
            ErrorMessage = "PTT is set to rigctld but the rig is not connected. Connect it or choose another PTT method in Settings.";
            return;
        }

        var request = BuildRequest();
        Qso.EnsureStarted();
        Settings.Save();

        _txCts = new CancellationTokenSource();
        _txStats = new TxMeterStats();
        _swrTripCount = 0;
        IsTransmitting = true;
        TxStatus = $"Transmitting {request.Mode.Name}…";
        _progressTimer.Start();
        try
        {
            _txTask = _tx.TransmitAsync(request, _txCts.Token);
            await _txTask;
            TxStatus = $"Sent {request.Mode.Name} at {DateTime.UtcNow:HH:mm}Z";
            TxProgress = 100;
        }
        catch (OperationCanceledException)
        {
            TxStatus = "Transmission stopped";
        }
        catch (Exception ex)
        {
            TxStatus = "Transmission failed";
            ErrorMessage = ex.Message;
        }
        finally
        {
            _progressTimer.Stop();
            _txCts.Dispose();
            _txCts = null;
            _txTask = null;
            IsTransmitting = false;
            LastTxSummary = _txStats?.Summary() ?? "";
            _txStats = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsTransmitting))]
    private void Stop() => _txCts?.Cancel();

    private void UpdateProgress()
    {
        if (_tx.Current is not { } synth) return;
        TxProgress = synth.Progress * 100;
        var elapsed = TimeSpan.FromSeconds((double)synth.Position / synth.SampleRate);
        var total = TimeSpan.FromSeconds((double)synth.TotalSamples / synth.SampleRate);
        TxStatus = $"ON AIR · {SelectedMode.Name} · {elapsed:m\\:ss} / {total:m\\:ss}";
    }

    public async Task ExportWavAsync(Stream output)
    {
        try
        {
            var synth = TransmitController.CreateSynthesizer(BuildRequest());
            await Task.Run(() => WavWriter.WriteAsync(synth, output));
            TxStatus = $"Exported {SelectedMode.Name} WAV";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"WAV export failed: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- rig

    private bool CanToggleRig() => !IsTransmitting;

    [RelayCommand(CanExecute = nameof(CanToggleRig))]
    private async Task ToggleRigAsync()
    {
        if (_rig.IsRunning)
        {
            await _rig.StopAsync();
            RigRunning = false;
        }
        else
        {
            ConnectRig();
        }
    }

    private void ConnectRig()
    {
        _rig.Start(Settings.RigHost, Settings.RigPort);
        RigRunning = true;
        RigState = $"Connecting to {Settings.RigHost}:{Settings.RigPort}…";
        RigStateBrush = DimBrush;
    }

    private void OnRigStatus(RigStatus s)
    {
        RigPtt = s.Ptt;
        UpdateMeters(s);
        if (!s.Connected)
        {
            RigFrequency = "--.---.---";
            RigMode = "";
            RigState = s.Error is null ? "Rig offline" : $"Rig error: {s.Error}";
            RigStateBrush = s.Error is null ? DimBrush : RedBrush;
            return;
        }

        RigFrequency = RigService.FormatFrequency(s.FrequencyHz);
        RigMode = s.Mode;
        RigState = s.Ptt ? "TX" : "Connected";
        RigStateBrush = s.Ptt ? RedBrush : GreenBrush;
        if (s.FrequencyHz > 0)
            Qso.FrequencyMHz = (s.FrequencyHz / 1_000_000.0).ToString("0.000000", CultureInfo.InvariantCulture);

        _ = PushRadioStatusAsync(s);
    }

    private void UpdateMeters(RigStatus s)
    {
        var m = s.Meters;
        var keyed = s.Connected && (s.Ptt || IsTransmitting);
        var missing = keyed ? "n/a" : "—";
        PowerSettingText = s.PowerSetting is { } setting ? $"Rig power setting {setting * 100:0} %" : "";

        if (m.PowerWatts is { } w)
        {
            PowerText = $"{w:0} W";
            PowerBar = Math.Clamp(w / Math.Max(1, Settings.TxPowerW) * 100, 0, 100);
        }
        else if (m.PowerRelative is { } rel)
        {
            PowerText = $"{rel * 100:0} %";
            PowerBar = Math.Clamp(rel * 100, 0, 100);
        }
        else
        {
            PowerText = missing;
            PowerBar = 0;
        }

        // Some rigs report 0 instead of 1.0 when no RF is present.
        if (m.Swr is { } swr and >= 1)
        {
            SwrText = $"{swr:0.0}";
            SwrBar = Math.Clamp((swr - 1) / 2 * 100, 0, 100); // 1.0 … 3.0 full scale
            SwrBrush = swr < 1.5 ? GreenBrush : swr < 2.0 ? YellowBrush : RedBrush;
        }
        else
        {
            SwrText = missing;
            SwrBar = 0;
            SwrBrush = GreenBrush;
        }

        if (m.Alc is { } alc)
        {
            // SSTV wants a clean, linear signal: any real ALC action means the TX level is too high.
            AlcText = $"{alc * 100:0} %";
            AlcBar = Math.Clamp(alc * 100, 0, 100);
            AlcBrush = alc < 0.10 ? GreenBrush : alc < 0.30 ? YellowBrush : RedBrush;
        }
        else
        {
            AlcText = missing;
            AlcBar = 0;
            AlcBrush = GreenBrush;
        }

        if (m.CurrentAmps is { } amps)
        {
            CurrentText = $"{amps:0.0} A";
            CurrentBar = Math.Clamp(amps / CurrentBarMaxAmps * 100, 0, 100);
        }
        else
        {
            CurrentText = missing;
            CurrentBar = 0;
        }

        if (!IsTransmitting) return;
        _txStats?.Add(m);
        CheckSwrTrip(m.Swr);
    }

    private void CheckSwrTrip(double? swr)
    {
        if (Settings.SwrTripLevel <= 0 || swr is not { } value) return;
        _swrTripCount = value >= Settings.SwrTripLevel ? _swrTripCount + 1 : 0;
        if (_swrTripCount < 2 || _txCts is null || _txCts.IsCancellationRequested) return;

        _txCts.Cancel();
        ErrorMessage = $"Transmission stopped: SWR {value:0.0} reached the trip level ({Settings.SwrTripLevel:0.0}). Check the antenna.";
    }

    private async Task PushRadioStatusAsync(RigStatus s)
    {
        if (!Settings.WavelogPushRadio || string.IsNullOrWhiteSpace(Settings.WavelogUrl) || string.IsNullOrWhiteSpace(Settings.WavelogApiKey))
            return;
        if (_lastRadioPush == (s.FrequencyHz, s.Mode)) return;
        _lastRadioPush = (s.FrequencyHz, s.Mode);
        try
        {
            await _wavelog.SendRadioStatusAsync(WavelogOptions(), RadioName, s.FrequencyHz, s.Mode);
        }
        catch (Exception ex)
        {
            WavelogStatus = $"Radio push failed: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- settings

    private WavelogOptions WavelogOptions() =>
        new(Settings.WavelogUrl, Settings.WavelogApiKey, Settings.WavelogStationId);

    [RelayCommand]
    private async Task LoadStationsAsync()
    {
        WavelogStatus = "Contacting Wavelog…";
        try
        {
            var stations = await _wavelog.GetStationsAsync(WavelogOptions());
            Stations.Clear();
            foreach (var s in stations) Stations.Add(s);
            SelectedStation = stations.FirstOrDefault(s => s.Id == Settings.WavelogStationId)
                              ?? stations.FirstOrDefault(s => s.Active) ?? stations.FirstOrDefault();
            WavelogStatus = $"Found {stations.Count} station profile(s).";
        }
        catch (Exception ex)
        {
            WavelogStatus = $"Failed: {ex.Message}";
        }
    }

    [ObservableProperty] private WavelogStation? _selectedStation;

    partial void OnSelectedStationChanged(WavelogStation? value)
    {
        if (value is null) return;
        Settings.WavelogStationId = value.Id;
        if (string.IsNullOrWhiteSpace(Settings.MyCall)) Settings.MyCall = value.Callsign;
        if (string.IsNullOrWhiteSpace(Settings.MyGrid)) Settings.MyGrid = value.Gridsquare;
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        AudioDevices = AudioOutput.GetDevices();
        SerialPorts = SerialPort.GetPortNames();
        Receive.RefreshDevices();
        OnPropertyChanged(nameof(AudioDevices));
        OnPropertyChanged(nameof(SerialPorts));
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            Settings.Save();
            WavelogStatus = "Settings saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not save settings: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- settings & log files

    private const string BeforeImportFile = "settings-before-import.json";

    /// <summary>Result of the last export / import / new-log action on the Settings tab.</summary>
    [ObservableProperty] private string _dataStatus = "";
    [ObservableProperty] private string _logSummary = "";

    partial void OnSelectedTabChanged(int value)
    {
        if (value == SettingsTab) RefreshLogSummary();
    }

    private void RefreshLogSummary()
    {
        var count = QsoLogger.CountQsos(QsoLogger.AdifPath);
        LogSummary = count == 0 ? "Log: no QSOs yet" : $"Log: {count} QSO{(count == 1 ? "" : "s")}";
    }

    public void ExportSettings(string path)
    {
        try
        {
            Settings.ExportTo(path);
            DataStatus = $"Settings exported to {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            DataStatus = $"Could not export settings: {ex.Message}";
        }
    }

    /// <summary>Replaces all settings with an exported file. The settings in use are kept in the data folder first.</summary>
    public void ImportSettings(string path)
    {
        if (IsTransmitting)
        {
            DataStatus = "Not while transmitting.";
            return;
        }
        try
        {
            var imported = AppSettings.ReadExport(path);
            Directory.CreateDirectory(AppSettings.DataDirectory);
            Settings.ExportTo(Path.Combine(AppSettings.DataDirectory, BeforeImportFile));
            Settings.CopyFrom(imported);
            ReplaceProfiles(imported.OverlayProfiles);
            Settings.Save();
            DataStatus = $"Imported {Path.GetFileName(path)}. Your previous settings are kept as {BeforeImportFile} " +
                         "in the data folder. Reconnect the rig and restart listening to use the new devices.";
        }
        catch (Exception ex)
        {
            DataStatus = $"Could not import settings: {ex.Message}";
        }
    }

    // A profile must stay selected throughout: add the new ones, select one, then drop the old ones.
    private void ReplaceProfiles(IReadOnlyList<OverlayProfile> profiles)
    {
        var name = Settings.OverlayProfileName;
        var old = Settings.OverlayProfiles.ToList();
        foreach (var profile in profiles) Settings.OverlayProfiles.Add(profile);
        SelectedProfile = profiles.FirstOrDefault(p => p.Name == name) ?? profiles[0];
        foreach (var profile in old) Settings.OverlayProfiles.Remove(profile);
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    public void ExportLog(string path)
    {
        try
        {
            var count = QsoLogger.ExportLog(QsoLogger.AdifPath, path);
            DataStatus = count == 0 ? "No QSOs logged yet, so nothing was exported." : $"Exported {count} QSO(s) to {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            DataStatus = $"Could not export the log: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NewLog()
    {
        try
        {
            DataStatus = QsoLogger.StartNewLog(QsoLogger.AdifPath) is { } old
                ? $"Started a new log. The old one ({old.Qsos} QSOs) is kept in the data folder as {old.Name}."
                : "The log is already empty.";
        }
        catch (Exception ex)
        {
            DataStatus = $"Could not start a new log: {ex.Message}";
        }
        RefreshLogSummary();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            var folder = Directory.CreateDirectory(AppSettings.DataDirectory).FullName;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DataStatus = $"Could not open the data folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DismissError()
    {
        ErrorMessage = null;
        ErrorHasLog = false;
    }

    // Raised on whichever thread hit the error.
    private void OnErrorLogged(string where) => Dispatcher.UIThread.Post(() =>
    {
        ErrorMessage = $"Something went wrong ({where}). SSTV Studio keeps running; the details are in errors.log.";
        ErrorHasLog = true;
    });

    [RelayCommand]
    private void OpenErrorLog()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ErrorLog.FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open the log: {ex.Message}";
        }
    }

    /// <summary>Collects meter readings during one transmission for the "last TX" summary.</summary>
    private sealed class TxMeterStats
    {
        private double _wattsSum;
        private int _wattsCount;
        private double _relativeSum;
        private int _relativeCount;
        private double? _maxSwr;
        private double? _maxAlc;
        private double? _maxCurrent;

        public void Add(TxMeters m)
        {
            if (m.PowerWatts is { } w) { _wattsSum += w; _wattsCount++; }
            if (m.PowerRelative is { } r) { _relativeSum += r; _relativeCount++; }
            if (m.Swr is { } swr and >= 1) _maxSwr = Math.Max(_maxSwr ?? 0, swr);
            if (m.Alc is { } alc) _maxAlc = Math.Max(_maxAlc ?? 0, alc);
            if (m.CurrentAmps is { } a) _maxCurrent = Math.Max(_maxCurrent ?? 0, a);
        }

        public string Summary()
        {
            var parts = new List<string>();
            if (_wattsCount > 0) parts.Add($"{_wattsSum / _wattsCount:0} W avg");
            else if (_relativeCount > 0) parts.Add($"{_relativeSum / _relativeCount * 100:0} % avg");
            if (_maxSwr is { } swr) parts.Add($"SWR ≤ {swr:0.0}");
            if (_maxAlc is { } alc) parts.Add($"ALC ≤ {alc * 100:0} %");
            if (_maxCurrent is { } a) parts.Add($"Id ≤ {a:0.0} A");
            return parts.Count == 0 ? "" : "Last TX: " + string.Join(" · ", parts);
        }
    }

    // ---------------------------------------------------------------- shutdown

    /// <summary>Stops any transmission (which un-keys the rig) and persists settings.</summary>
    public async ValueTask DisposeAsync()
    {
        ErrorLog.Logged -= OnErrorLogged;
        _txCts?.Cancel();
        if (_txTask is not null)
        {
            try { await _txTask; }
            catch (Exception) { /* outcome already reported; we only need PTT released */ }
        }
        try { Settings.Save(); } catch (IOException) { }
        await Receive.DisposeAsync();
        await _rig.DisposeAsync();
        _source?.Dispose();
        _composed?.Dispose();
    }
}
