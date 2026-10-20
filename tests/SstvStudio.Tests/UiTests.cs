// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SstvStudio.Core.Imaging;
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

    private static string Errors(Control control) =>
        string.Join("; ", DataValidationErrors.GetErrors(control)?.Select(e => e?.ToString()) ?? []);

    [Fact]
    public Task NoiseFilterSelectionHasNoBindingError() => Session.Dispatch(() =>
    {
        var (window, vm) = OpenReceiveTools();
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
