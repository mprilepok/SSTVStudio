// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SstvStudio.Core.Audio;
using SstvStudio.Core.Encoding;
using SstvStudio.Core.Imaging;
using SstvStudio.Core.Modes;
using SstvStudio.Services;
using SstvStudio.ViewModels;
using SstvStudio.Views;
using SstvStudio.Wavelog;

namespace SstvStudio.Tests;

/// <summary>Builds the real app headlessly: no window on screen, no sound card, no rig.</summary>
public static class HeadlessApp
{
    // Real Skia rendering (still off-screen): stub drawing would decode every bitmap as 1×1.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class UiTests
{
    // Isolated settings: defaults mean no auto-listen and no rig auto-connect, whatever the operator has set.
    private static readonly string DataDir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "sstvstudio-uitest-" + Environment.ProcessId)).FullName;

    private static readonly HeadlessUnitTestSession Session = StartSession();

    private static HeadlessUnitTestSession StartSession()
    {
        Environment.SetEnvironmentVariable("SSTVSTUDIO_DATA_DIR", DataDir);
        return HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
    }

    private static (MainWindow Window, MainWindowViewModel Vm) OpenReceiveTools()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.SelectedTab = MainWindowViewModel.ReceiveTab;
        Dispatcher.UIThread.RunJobs();
        var tools = window.GetVisualDescendants().OfType<Expander>()
            .First(e => e.Header is string h && h.StartsWith("Tools", StringComparison.Ordinal));
        tools.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static ComboBox NoiseFilterBox(Window window) =>
        window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.ItemsSource is IEnumerable<NoiseFilter>);

    // What the field shows: an exception's message, as Avalonia's error template displays it.
    private static string Errors(Control control) =>
        string.Join("; ", DataValidationErrors.GetErrors(control)?.Select(e => e is Exception ex ? ex.Message : e?.ToString()) ?? []);

    [Fact]
    public Task NoiseFilterSelectionHasNoBindingError() => Session.Dispatch(() =>
    {
        var (window, vm) = OpenReceiveTools();
        vm.Settings.RxNoiseFilter = NoiseFilter.Off; // don't depend on what an earlier test left in the shared settings
        Dispatcher.UIThread.RunJobs();
        var box = NoiseFilterBox(window);
        Assert.False(DataValidationErrors.GetHasErrors(box), Errors(box));
        Assert.Equal(NoiseFilter.Off, box.SelectedItem);

        box.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.False(DataValidationErrors.GetHasErrors(box), Errors(box));
        Assert.Equal(NoiseFilter.Low, vm.Settings.RxNoiseFilter);

        box.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(NoiseFilter.High, vm.Settings.RxNoiseFilter);
        Assert.False(DataValidationErrors.GetHasErrors(box), Errors(box));
        window.Close();
    }, CancellationToken.None);

    private static (MainWindow Window, MainWindowViewModel Vm) OpenOverlayEditor()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.SelectedTab = MainWindowViewModel.TransmitTab;
        Dispatcher.UIThread.RunJobs();
        var editor = window.GetVisualDescendants().OfType<Expander>()
            .First(e => e.Header is "Edit overlay profile");
        editor.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    [Fact]
    public Task OverlayLinesCanBeReordered() => Session.Dispatch(() =>
    {
        var (window, vm) = OpenOverlayEditor();
        var lines = vm.SelectedProfile.Lines;
        Assert.True(lines.Count >= 3);
        var (first, second, last) = (lines[0], lines[1], lines[^1]);

        Assert.False(vm.MoveOverlayLineUpCommand.CanExecute(first));
        Assert.False(vm.MoveOverlayLineDownCommand.CanExecute(last));
        Assert.True(vm.MoveOverlayLineDownCommand.CanExecute(first));

        vm.MoveOverlayLineDownCommand.Execute(first);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(second, lines[0]);
        Assert.Same(first, lines[1]);
        Assert.True(vm.MoveOverlayLineUpCommand.CanExecute(first));

        vm.MoveOverlayLineUpCommand.Execute(first);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(first, lines[0]);

        // The editor shows one row per line, in the new order, and the ▲/▼ buttons exist.
        var rows = window.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("overlayRow")).ToList();
        Assert.Equal(lines.Count, rows.Count);
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Content is "▲");
        window.Close();
    }, CancellationToken.None);

    /// <summary>Like the noise filter: the per-line font/position/align dropdowns must survive tab switches.</summary>
    [Fact]
    public Task OverlayEditorSurvivesTabSwitches() => Session.Dispatch(() =>
    {
        var (window, vm) = OpenOverlayEditor();
        var before = vm.SelectedProfile.Lines.Select(l => (l.FontFamily, l.Position, l.Align)).ToList();

        foreach (var tab in new[] { 0, 1, 2, 1, 0, 1 })
        {
            vm.SelectedTab = tab;
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(before, vm.SelectedProfile.Lines.Select(l => (l.FontFamily, l.Position, l.Align)).ToList());
        window.Close();
    }, CancellationToken.None);

    /// <summary>Each tab has its own zoom: Fit (default), a 25–400 % slider, and 1:1 (one picture pixel per screen pixel).</summary>
    [Fact]
    public Task ZoomShowsPicturesFittedOrAtTheChosenSize() => Session.Dispatch(() =>
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1256, Height = 860 };
        window.Show();
        vm.SelectedTab = MainWindowViewModel.TransmitTab;
        Dispatcher.UIThread.RunJobs();

        StackPanel Bar() => window.GetVisualDescendants().OfType<StackPanel>()
            .Single(p => p.Classes.Contains("zoombar") && p.IsEffectivelyVisible);
        LayoutTransformControl Viewer() => window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(s => s.Classes.Contains("zoom") && s.IsEffectivelyVisible)
            .GetVisualDescendants().OfType<LayoutTransformControl>().Single();
        // Martin M1 preview: 320×256 picture pixels (render scaling is 1 here).
        var (w, h) = (vm.SelectedMode.Width, vm.SelectedMode.Height);

        Assert.True(vm.TxZoom.IsFit);
        var fitted = Viewer().Bounds.Width;
        Assert.True(fitted > w); // fitted: scaled up to the panel

        Bar().GetVisualDescendants().OfType<Button>().Single(b => b.Content is "1:1").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(100, vm.Settings.TxZoomPercent);
        Assert.Equal(w, Viewer().Bounds.Width, 0.5);
        Assert.Equal(h, Viewer().Bounds.Height, 0.5);

        Bar().GetVisualDescendants().OfType<Slider>().Single().Value = 200;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("200 %", vm.TxZoom.Label);
        Assert.Equal(2 * w, Viewer().Bounds.Width, 0.5);

        Bar().GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true; // back to Fit
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, vm.Settings.TxZoomPercent);
        Assert.Equal(fitted, Viewer().Bounds.Width, 0.5); // the panel's size again, not the zoomed one
        Bar().GetVisualDescendants().OfType<Slider>().Single().Value = 200;
        Dispatcher.UIThread.RunJobs();

        vm.SelectedTab = MainWindowViewModel.ReceiveTab; // the Receive tab keeps its own zoom
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Receive.Zoom.IsFit);
        Assert.True(Bar().GetVisualDescendants().OfType<CheckBox>().Single().IsChecked);

        vm.TxZoom.IsFit = true; // the other UI tests expect fitted viewers
        Assert.Equal(0, vm.Settings.TxZoomPercent);
        Assert.Equal(200, vm.TxZoom.Percent); // the slider remembers where it was
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public void OldOneToOneSettingBecomesZoom()
    {
        var path = Path.Combine(TempFolder("zoom"), "old.json");
        File.WriteAllText(path, """{ "MyCall": "OM1PU", "ShowActualSize": true }""");
        var settings = AppSettings.ReadExport(path);
        Assert.Equal(100, settings.RxZoomPercent);
        Assert.Equal(100, settings.TxZoomPercent);
        Assert.Null(settings.ShowActualSize);
    }

    [Fact]
    public Task OpensOnReceiveTab() => Session.Dispatch(() =>
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        Assert.Equal(MainWindowViewModel.ReceiveTab, tabs.SelectedIndex);
        Assert.Equal("Receive", ((TabItem)tabs.SelectedItem!).Header);
        Assert.Equal(new[] { "Receive", "Transmit", "Settings" }, tabs.Items.Cast<TabItem>().Select(t => (string)t.Header!));
        window.Close();
    }, CancellationToken.None);

    /// <summary>A callsign typed on the Receive tab is the one the Transmit tab and its overlay use.</summary>
    [Fact]
    public Task QsoEnteredOnReceiveTabIsSharedWithTransmit() => Session.Dispatch(() =>
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextBox CallBox() => window.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.PlaceholderText == "OK1ABC" && t.IsEffectivelyVisible);

        var previewBefore = vm.Preview;
        CallBox().Text = "OZ1LHS";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("OZ1LHS", vm.Qso.Call);
        Assert.NotNull(vm.Qso.StartUtc);                // the QSO starts when the call is entered
        Assert.NotSame(previewBefore, vm.Preview);       // the reply overlay ({CALL}) was redrawn

        vm.SelectedTab = MainWindowViewModel.TransmitTab;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("OZ1LHS", CallBox().Text);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task QsoCanBeLoggedFromReceiveTab() => Session.Dispatch(async () =>
    {
        var vm = new MainWindowViewModel();
        vm.Qso.Call = "OK1ABC";
        vm.Qso.RstSent = "575";
        await vm.Qso.LogCommand.ExecuteAsync(null);

        Assert.StartsWith("Saved to", vm.Qso.LogStatus);  // Wavelog isn't configured in the test settings
        Assert.Contains("OK1ABC", vm.Qso.Recent[0]);
        Assert.Equal("", vm.Qso.Call);                     // cleared for the next contact
        Assert.Null(vm.Qso.StartUtc);
        var adif = await File.ReadAllTextAsync(Path.Combine(DataDir, "log.adi"));
        Assert.Contains("<CALL:6>OK1ABC", adif);
        Assert.Contains("<RST_SENT:3>575", adif);
    }, CancellationToken.None);

    /// <summary>Switching tabs detaches and re-attaches the Receive tab; the filter must survive without errors.</summary>
    [Fact]
    public Task NoiseFilterSurvivesTabSwitches() => Session.Dispatch(() =>
    {
        var (window, vm) = OpenReceiveTools();
        NoiseFilterBox(window).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var fit = vm.Settings.ImageFit;
        var ptt = vm.Settings.PttMethod;

        foreach (var tab in new[] { 0, 1, 2, 1, 0, 1, 2, 0 })
        {
            vm.SelectedTab = tab;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(NoiseFilter.Low, vm.Settings.RxNoiseFilter);
            // The other enum ComboBoxes (scaling on Transmit, PTT on Settings) must survive detaching too.
            Assert.Equal(fit, vm.Settings.ImageFit);
            Assert.Equal(ptt, vm.Settings.PttMethod);
        }
        vm.SelectedTab = MainWindowViewModel.ReceiveTab;
        Dispatcher.UIThread.RunJobs();

        var tools = window.GetVisualDescendants().OfType<Expander>()
            .First(e => e.Header is string h && h.StartsWith("Tools", StringComparison.Ordinal));
        tools.IsExpanded = false;
        Dispatcher.UIThread.RunJobs();
        tools.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        var box = NoiseFilterBox(window);
        Assert.False(DataValidationErrors.GetHasErrors(box), Errors(box));
        Assert.Equal(NoiseFilter.Low, box.SelectedItem);
        Assert.Equal(NoiseFilter.Low, vm.Settings.RxNoiseFilter);
        vm.Settings.RxNoiseFilter = NoiseFilter.Off; // closing saves the settings; leave the default for other tests
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task CallsignIsUpperCasedWhileTyping() => Session.Dispatch(() =>
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.SelectedTab = MainWindowViewModel.ReceiveTab;
        Dispatcher.UIThread.RunJobs();
        var box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "OK1ABC");
        box.Focus();

        window.KeyTextInput("ok1ab");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("OK1AB", box.Text);
        Assert.Equal("OK1AB", vm.Qso.Call);
        Assert.Equal(5, box.CaretIndex);

        box.CaretIndex = 2; // typing in the middle keeps the caret in place
        window.KeyTextInput("x");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("OKX1AB", vm.Qso.Call);
        Assert.Equal(3, box.CaretIndex);

        box.Text = "om1abc"; // as a paste lands
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("OM1ABC", box.Text);
        Assert.Equal("OM1ABC", vm.Qso.Call);

        vm.Settings.MyCall = "om1pu";
        Assert.Equal("OM1PU", vm.Settings.MyCall);
        vm.Settings.MyCall = "";
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task OverlayDropdownFollowsTheOrderSetInSettings() => Session.Dispatch(() =>
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1256, Height = 860 };
        window.Show();
        vm.SelectedTab = MainWindowViewModel.TransmitTab;
        Dispatcher.UIThread.RunJobs();
        var profiles = vm.Settings.OverlayProfiles;
        var original = profiles.ToList();
        var box = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "OverlayProfileBox");

        box.SelectedIndex = 1; // the dropdown selects the profile
        Dispatcher.UIThread.RunJobs();
        var chosen = vm.SelectedProfile;
        Assert.Same(profiles[1], chosen);

        vm.SelectedTab = MainWindowViewModel.SettingsTab; // reorder in Settings
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.MoveProfileUpCommand.CanExecute(profiles[0]));
        Assert.False(vm.MoveProfileDownCommand.CanExecute(profiles[^1]));
        vm.MoveProfileUpCommand.Execute(chosen);                 // the selected one moves to the top
        vm.MoveProfileDownCommand.Execute(profiles[1]);          // another one moves past it
        Dispatcher.UIThread.RunJobs();
        Assert.Same(chosen, vm.SelectedProfile);                 // moving never changes the selection
        Assert.Same(chosen, vm.SelectedProfileChoice);

        vm.SelectedTab = MainWindowViewModel.TransmitTab;        // the dropdown shows the new order
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(profiles.Select(p => p.Name), box.Items.Cast<OverlayProfile>().Select(p => p.Name));
        Assert.Same(chosen, box.SelectedItem);

        vm.MoveProfileDownCommand.Execute(chosen);               // a move while the dropdown is visible
        Dispatcher.UIThread.RunJobs();
        Assert.Same(chosen, vm.SelectedProfile);
        Assert.Same(chosen, box.SelectedItem);

        foreach (var tab in new[] { 2, 0, 1, 2, 1 })              // detaching the tab must not clear the selection
        {
            vm.SelectedTab = tab;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(chosen, vm.SelectedProfile);
        }

        for (var i = 0; i < original.Count; i++) profiles.Move(profiles.IndexOf(original[i]), i); // restore
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task MouseWheelOverTheTxLevelAdjustsIt() => Session.Dispatch(() =>
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1256, Height = 860 };
        window.Show();
        vm.SelectedTab = MainWindowViewModel.TransmitTab;
        Dispatcher.UIThread.RunJobs();
        var slider = window.GetVisualDescendants().OfType<Slider>().Single(s => s.Classes.Contains("wheel") && s.IsEffectivelyVisible);
        var over = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), window)!.Value;
        var panel = slider.FindAncestorOfType<ScrollViewer>()!;
        var scrolled = panel.Offset;
        var start = vm.Settings.TxLevelPercent;

        window.MouseWheel(over, new Vector(0, 1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(start + 1, vm.Settings.TxLevelPercent);

        window.MouseWheel(over, new Vector(0, -1));
        window.MouseWheel(over, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(start - 1, vm.Settings.TxLevelPercent);
        Assert.Equal(scrolled, panel.Offset); // the wheel adjusted the level instead of scrolling the panel

        vm.Settings.TxLevelPercent = start;
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task NumberFieldsRejectTextOutsideTheirRange() => Session.Dispatch(() =>
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1256, Height = 860 };
        window.Show();
        vm.SelectedTab = MainWindowViewModel.SettingsTab;
        Dispatcher.UIThread.RunJobs();
        // The TextBox next to a label, as laid out on the Settings tab.
        TextBox Field(string label) => window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == label && t.IsEffectivelyVisible)
            .FindAncestorOfType<StackPanel>()!.GetVisualDescendants().OfType<TextBox>().First();
        var port = Field("Port");
        var before = vm.Settings.RigPort;

        foreach (var bad in new[] { "abc", "4532.5", "70000" })
        {
            port.Text = bad;
            Dispatcher.UIThread.RunJobs();
            Assert.True(DataValidationErrors.GetHasErrors(port), bad);
            Assert.Equal("Whole number, 1 to 65535", Errors(port)); // exactly this, not an exception dump
            Assert.Equal(before, vm.Settings.RigPort); // not applied
        }

        port.Text = "4533";
        Dispatcher.UIThread.RunJobs();
        Assert.False(DataValidationErrors.GetHasErrors(port));
        Assert.Equal(4533, vm.Settings.RigPort);

        var ppm = Field("Clock correction (ppm)");
        ppm.Text = "1,5";
        Dispatcher.UIThread.RunJobs();
        Assert.False(DataValidationErrors.GetHasErrors(ppm), Errors(ppm));
        Assert.Equal(1.5, vm.Settings.ClockPpm);

        vm.Settings.RigPort = before; // the other UI tests expect the defaults
        vm.Settings.ClockPpm = 0;
        window.Close();
    }, CancellationToken.None);

    // ---------------------------------------------------------------- error log
    // Here because ErrorLog writes to the data folder, which must be the temp DataDir.

    /// <summary>A WAV with two Robot 36 pictures, a second of silence before, between and after them.</summary>
    private static string TwoPictureRecording()
    {
        const int rate = 12000;
        var path = Path.Combine(TempFolder("rx"), "two.wav");
        var options = new TransmitOptions(SendPreamble: true);
        var mode = SstvModes.Robot36;
        using var writer = new WavFileWriter(File.Create(path), rate);
        var buffer = new float[4096];
        writer.Write(new float[rate]);
        for (var p = 0; p < 2; p++)
        {
            var synth = new ToneSynthesizer(SstvEncoder.Encode(mode, RgbImage.Blank(mode.Width, mode.Height), options),
                SstvEncoder.DurationMs(mode, options), rate, 0.5);
            int n;
            while ((n = synth.Read(buffer)) > 0) writer.Write(buffer.AsSpan(0, n));
            writer.Write(new float[rate]);
        }
        return path;
    }

    [Fact]
    public async Task ReceiverErrorIsLoggedAndReceptionContinues()
    {
        var recording = TwoPictureRecording();
        File.Delete(ErrorLog.FilePath);
        await using var service = new ReceiveService();
        var started = 0;
        var completed = 0;
        service.ImageStarted += _ =>
        {
            if (++started == 1) throw new InvalidOperationException("forced test failure in the receiver");
        };
        service.ImageCompleted += (_, _) => completed++;

        await service.DecodeFileAsync(recording, DecodeOptions.Default);

        Assert.Equal(2, started);
        Assert.True(completed >= 1, "the picture after the error must still be received");
        var log = File.ReadAllText(ErrorLog.FilePath);
        Assert.Contains("receiver", log);
        Assert.Contains("forced test failure in the receiver", log);
    }

    [Fact]
    public Task LoggedErrorShowsTheBannerWithOpenLog() => Session.Dispatch(() =>
    {
        var vm = new MainWindowViewModel();
        ErrorLog.Write("test", new InvalidOperationException($"banner test {Guid.NewGuid()}"));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("errors.log", vm.ErrorMessage);
        Assert.True(vm.ErrorHasLog);
        vm.DismissErrorCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.ErrorHasLog);
    }, CancellationToken.None);

    [Fact]
    public void RepeatedErrorIsWrittenOnce()
    {
        _ = DataDir;
        var marker = $"repeat test {Guid.NewGuid()}";
        for (var i = 0; i < 50; i++) ErrorLog.Write("loop", new InvalidOperationException(marker));
        ErrorLog.Write("other", new InvalidOperationException("something else")); // flushes the repeat count
        var log = File.ReadAllText(ErrorLog.FilePath);
        Assert.Equal(1, log.Split(marker).Length - 1);
        Assert.Contains("repeated 49 more time(s)", log);
    }

    [Fact]
    public Task UnreadableSettingsFallBackToTheLastGoodCopy() => Session.Dispatch(() =>
    {
        var file = Path.Combine(DataDir, "settings.json");
        var original = File.Exists(file) ? File.ReadAllText(file) : null;
        var originalBackup = File.Exists(AppSettings.BackupPath) ? File.ReadAllText(AppSettings.BackupPath) : null;
        try
        {
            var good = AppSettings.Load();
            good.MyCall = "OM0BAK";
            good.Save();
            Assert.Null(AppSettings.Load().LoadProblem); // a clean load refreshes the backup

            File.WriteAllText(file, "{ \"MyCall\": \"OM0BAK\", \"RigPort\": "); // cut off mid-write
            var vm = new MainWindowViewModel();
            Assert.Equal("OM0BAK", vm.Settings.MyCall);
            Assert.Contains("last good copy", vm.ErrorMessage);
            var kept = Directory.GetFiles(DataDir, "settings-unreadable-*.json");
            Assert.NotEmpty(kept);
            Assert.Contains("\"RigPort\": ", File.ReadAllText(kept[^1])); // set aside, not lost

            File.WriteAllText(file, "garbage");
            File.Delete(AppSettings.BackupPath);
            var fresh = AppSettings.Load();
            Assert.Equal("", fresh.MyCall);
            Assert.Contains("default settings", fresh.LoadProblem);
        }
        finally
        {
            foreach (var f in Directory.GetFiles(DataDir, "settings-unreadable-*.json")) File.Delete(f);
            if (original is null) File.Delete(file); else File.WriteAllText(file, original);
            if (originalBackup is null) File.Delete(AppSettings.BackupPath); else File.WriteAllText(AppSettings.BackupPath, originalBackup);
        }
    }, CancellationToken.None);

    // ---------------------------------------------------------------- backup: settings and log
    // These live here because DataDir must point SSTVSTUDIO_DATA_DIR at a temp folder before AppSettings is touched.

    private static string TempFolder(string name) =>
        Directory.CreateDirectory(Path.Combine(DataDir, $"{name}-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public Task ImportedSettingsReplaceTheCurrentOnes() => Session.Dispatch(() =>
    {
        var dir = TempFolder("import");
        var original = Path.Combine(dir, "original.json");
        var exported = Path.Combine(dir, "exported.json");
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.SelectedTab = MainWindowViewModel.SettingsTab;
        Dispatcher.UIThread.RunJobs();
        vm.ExportSettings(original);

        var other = AppSettings.ReadExport(original);
        other.MyCall = "OM0TEST";
        other.RigPort = 4999;
        other.OverlayProfiles.Add(new OverlayProfile { Name = "Contest", Lines = [OverlayLine.Create("QRZ?", OverlayPosition.Top, "#FFFF00")] });
        other.OverlayProfileName = "Contest";
        other.ExportTo(exported);

        vm.ImportSettings(exported);
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.StartsWith("Imported", vm.DataStatus);
            Assert.Equal("OM0TEST", vm.Settings.MyCall);
            Assert.Equal(4999, vm.Settings.RigPort);
            Assert.Equal("Contest", vm.SelectedProfile.Name);
            Assert.Equal(other.OverlayProfiles.Select(p => p.Name), vm.Settings.OverlayProfiles.Select(p => p.Name));
            Assert.True(File.Exists(Path.Combine(DataDir, "settings-before-import.json")));
            Assert.Equal("OM0TEST", AppSettings.ReadExport(Path.Combine(DataDir, "settings.json")).MyCall); // saved
        }
        finally
        {
            vm.ImportSettings(original); // the other UI tests expect the defaults
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public void SettingsImportRejectsOtherFiles()
    {
        var dir = TempFolder("reject");
        var notJson = Path.Combine(dir, "a.json");
        var otherJson = Path.Combine(dir, "b.json");
        File.WriteAllText(notJson, "<ADIF_VER:5>3.1.4");
        File.WriteAllText(otherJson, """{ "name": "something else" }""");

        Assert.Throws<InvalidDataException>(() => AppSettings.ReadExport(notJson));
        Assert.Throws<InvalidDataException>(() => AppSettings.ReadExport(otherJson));
    }

    [Fact]
    public void NewLogKeepsTheOldOneAndExportCopiesIt()
    {
        var dir = TempFolder("log");
        var log = Path.Combine(dir, "log.adi");
        var export = Path.Combine(dir, "export.adi");
        Assert.Equal(0, QsoLogger.ExportLog(log, export));
        Assert.False(File.Exists(export));
        Assert.Null(QsoLogger.StartNewLog(log));

        File.WriteAllText(log, Adif.FileHeader + "<CALL:6>OK1ABC<EOR>\n<CALL:5>OM1PU<eor>\n");
        Assert.Equal(2, QsoLogger.ExportLog(log, export));
        Assert.Equal(File.ReadAllText(log), File.ReadAllText(export));

        var old = QsoLogger.StartNewLog(log);
        Assert.NotNull(old);
        Assert.Equal(2, old.Value.Qsos);
        Assert.Matches(@"^log_\d{8}_\d{6}Z\.adi$", old.Value.Name);
        Assert.False(File.Exists(log)); // the next QSO starts a fresh file
        Assert.Equal(2, QsoLogger.CountQsos(Path.Combine(dir, old.Value.Name)));
    }
}
