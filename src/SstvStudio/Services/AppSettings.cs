// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SstvStudio.Core.Imaging;
using SstvStudio.Hamlib;

namespace SstvStudio.Services;

public enum ImageFit { Fill, Fit, Stretch }

/// <summary>User settings, persisted as JSON in %APPDATA%\SstvStudio\settings.json.</summary>
public partial class AppSettings : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Settings, ADIF log and similar files. <c>SSTVSTUDIO_DATA_DIR</c> overrides it, for a portable install or an
    /// isolated test instance that must not pick up the operator's real settings (auto-start, rig connection).
    /// </summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("SSTVSTUDIO_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SstvStudio");

    private static string FilePath => Path.Combine(DataDirectory, "settings.json");

    // Station
    [ObservableProperty] private string _myCall = "";
    [ObservableProperty] private string _myGrid = "";
    [ObservableProperty] private double _txPowerW = 50;

    // Rig (Hamlib rigctld)
    [ObservableProperty] private string _rigHost = "127.0.0.1";
    [ObservableProperty] private int _rigPort = 4532;
    [ObservableProperty] private bool _rigAutoConnect;
    [ObservableProperty] private PttMethod _pttMethod = PttMethod.Rigctld;
    [ObservableProperty] private string _serialPort = "";
    [ObservableProperty] private int _pttLeadMs = 150;
    [ObservableProperty] private int _pttTailMs = 150;
    [ObservableProperty] private int _maxTxSeconds = 330;
    /// <summary>Stop TX when SWR reaches this value on two consecutive readings; 0 disables.</summary>
    [ObservableProperty] private double _swrTripLevel = 3.0;

    // Audio
    [ObservableProperty] private string _audioDevice = AudioOutput.DefaultDevice;
    [ObservableProperty] private int _sampleRate = 48000;
    [ObservableProperty] private double _clockPpm;
    [ObservableProperty] private double _txLevelPercent = 70;

    // Receive
    [ObservableProperty] private string _rxAudioDevice = AudioInput.DefaultDevice;
    [ObservableProperty] private bool _rxAutoStart;
    [ObservableProperty] private bool _rxAutoSave = true;
    /// <summary>Also save each received picture's audio as a WAV next to its PNG.</summary>
    [ObservableProperty] private bool _rxSaveAudio;
    [ObservableProperty] private NoiseFilter _rxNoiseFilter = NoiseFilter.Off;
    [ObservableProperty] private string _rxSaveFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SstvStudio");

    // Transmission
    [ObservableProperty] private string _modeName = "Martin M1";
    [ObservableProperty] private bool _sendPreamble = true;
    [ObservableProperty] private bool _sendCwId;
    [ObservableProperty] private int _cwWpm = 20;
    [ObservableProperty] private ImageFit _imageFit = ImageFit.Fill;
    /// <summary>Zoom of the received pictures and of the TX preview, in % (100 = one picture pixel per screen pixel); 0 = fit.</summary>
    [ObservableProperty] private double _rxZoomPercent;
    [ObservableProperty] private double _txZoomPercent;
    // The single 1:1 checkbox from before zoom existed; read once by Migrate() and then dropped.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowActualSize { get; set; }
    [ObservableProperty] private string _overlayProfileName = "CQ";
    public ObservableCollection<OverlayProfile> OverlayProfiles { get; set; } = [];

    // Two-line overlay from before profiles existed; read once by Migrate() and then dropped.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OverlayTop { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OverlayBottom { get; set; }
    [ObservableProperty] private string? _lastImagePath;

    // Wavelog
    [ObservableProperty] private string _wavelogUrl = "";
    [ObservableProperty] private string _wavelogApiKey = "";
    [ObservableProperty] private string _wavelogStationId = "";
    [ObservableProperty] private bool _wavelogPushRadio;

    partial void OnMyCallChanged(string value)
    {
        if (value != value.ToUpperInvariant()) MyCall = value.ToUpperInvariant();
    }

    [JsonIgnore]
    public bool WavelogConfigured =>
        !string.IsNullOrWhiteSpace(WavelogUrl) && !string.IsNullOrWhiteSpace(WavelogApiKey) && !string.IsNullOrWhiteSpace(WavelogStationId);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) is { } loaded)
                return loaded.Migrate();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt or locked settings: start from defaults rather than refusing to launch.
        }
        return new AppSettings().Migrate();
    }

    /// <summary>Ensures there are overlay profiles; turns a customised legacy two-line overlay into one.</summary>
    private AppSettings Migrate()
    {
        if (OverlayProfiles.Count == 0)
            foreach (var profile in OverlayProfile.Defaults()) OverlayProfiles.Add(profile);

        var customised = (OverlayTop is not null && OverlayTop != "{CALL} de {MYCALL}")
                         || (OverlayBottom is not null && OverlayBottom != "UR {RST}  {DATE}");
        if (customised)
        {
            OverlayProfiles.Add(new OverlayProfile
            {
                Name = "My overlay",
                Lines =
                [
                    OverlayLine.Create(OverlayTop ?? "", OverlayPosition.Top, "#FFFF00", 11),
                    OverlayLine.Create(OverlayBottom ?? "", OverlayPosition.Bottom, "#FFFFFF", 11),
                ],
            });
            OverlayProfileName = "My overlay";
        }
        OverlayTop = OverlayBottom = null;

        if (ShowActualSize == true) RxZoomPercent = TxZoomPercent = 100;
        ShowActualSize = null;
        return this;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        ExportTo(FilePath);
    }

    /// <summary>Writes the settings to <paramref name="path"/>, e.g. to move them to another PC.</summary>
    public void ExportTo(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>Reads settings written by <see cref="ExportTo"/>; throws <see cref="InvalidDataException"/> for any other file.</summary>
    public static AppSettings ReadExport(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            // Any JSON object would deserialize as all defaults; require a property every export has.
            using (var doc = JsonDocument.Parse(json))
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(nameof(MyCall), out _))
                    throw new InvalidDataException("This isn't an SSTV Studio settings file.");
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)!.Migrate();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"This isn't an SSTV Studio settings file ({ex.Message}).", ex);
        }
    }

    /// <summary>
    /// Takes every setting from <paramref name="other"/>, raising change notifications so the UI follows. Overlay
    /// profiles are left to the caller, which has to keep a profile selected while it swaps them.
    /// </summary>
    public void CopyFrom(AppSettings other)
    {
        foreach (var property in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property is { CanRead: true, CanWrite: true } && property.Name != nameof(OverlayProfiles)
                && property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
                property.SetValue(this, property.GetValue(other));
        }
    }
}
