// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Imaging;
using SstvStudio.Core.Modes;

namespace SstvStudio.Core.Decoding;

public enum DecoderState { Searching, Receiving }

/// <param name="TuningOffsetHz">How far the signal was from nominal (positive = tones too high).</param>
/// <param name="ClockErrorPpm">Line-period error of the sender (or our sound card); positive = lines too long.</param>
public sealed record DecodedImage(SstvMode Mode, RgbImage Image, DateTime StartedUtc, bool Complete,
    double TuningOffsetHz = 0, double ClockErrorPpm = 0);

/// <summary>
/// Streaming SSTV receiver: waits for a VIS header, then decodes the picture line by line.
/// <para>
/// Timing comes from the sync pulses. The end of each pulse is located near where the current timing
/// model predicts it, and a robust straight-line fit over all measured pulses gives the line period
/// (slant correction) and phase. Lines are drawn as soon as their audio has arrived; when the picture is
/// complete, every line is redrawn with the final fit.
/// </para>
/// Not thread-safe: call <see cref="Process"/> and <see cref="Stop"/> from one thread. <see cref="Snapshot"/>
/// may be called from any thread.
/// </summary>
public sealed class SstvDecoder
{
    private const double SyncThresholdHz = 1350;
    private const double PrerollMs = 2000;
    private const int FalseTriggerPeriods = 8;
    // Sender sound-card clock error we correct. Real stations are seen ~1 % off (a Martin M1 with 450.8 ms
    // lines instead of 446.4 ms); 3 % leaves margin.
    private const double MaxSlant = 0.03;
    private const double LockedWindowMs = 10;  // search half-width once the timing is confirmed
    private const double InitialWindowMs = 25; // until then: covers the VIS-end uncertainty
    // Two locked pulses confirm the timing when they're whole periods apart within this, plus the clock error
    // allowed over the gap. A lone pulse never moves the timing: in Robot 36 a noise blip inside the VIS stop
    // bit (one long 1200 Hz tone with the first sync) looked like a sync end ~30 ms early, the timing followed
    // it, the next pulses were missed, and ~2 % of pictures were dropped at 10 dB SNR.
    private const double ConfirmToleranceMs = 2;
    // Sync score = fraction of samples matching an ideal sync edge (best position in the search window).
    private const double LockScore = 0.7; // good enough to use the pulse for timing
    // Presence = sync score minus the score of a control window half a period later (picture data).
    // Measured over Robot 36, Martin, Scottie and PD at 12/48 kHz: band noise alone has a median of ≈ 0.00
    // (p90 ≤ 0.16) in every mode, while every signal strong enough for its VIS to be detected has ≥ 0.59.
    private const double PresenceMargin = 0.15;
    // Signal-lost decision window: the median presence over this span must stay above PresenceMargin.
    private const double SignalLostMs = 6000;
    private const int SignalLostMinPeriods = 8;
    /// <summary>Largest receiver mistuning that is detected and corrected.</summary>
    public const double MaxTuningOffsetHz = VisDetector.MaxOffsetHz;

    private readonly FmDemodulator _demod;
    private readonly VisDetector _vis;
    private readonly FrequencyBuffer _freq = new();
    private readonly double _k; // frequency samples per millisecond
    private readonly Lock _imageLock = new();
    private readonly List<(int Period, double End)> _syncs = [];
    private readonly List<double> _presence = []; // one per scanned period
    private float[] _scratch = [];

    // State of the picture being received.
    private SstvMode? _mode;
    private ScanLayout _layout = null!;
    private RgbImage? _image;
    private byte[][] _planes = [];
    private bool[] _hasPlane1 = [];
    private bool[] _hasPlane2 = [];
    private int _periods;
    private double _a; // stream index of the end of period 0's sync pulse
    private double _b; // samples per period
    private double _bNominal;
    private int _nextSync;
    private int _nextRender;
    private int _lastPresentPeriod; // last period whose sync score showed a signal
    private int _lastLockPeriod;    // last period whose pulse was used for timing
    private bool _confirmed;        // two locked pulses agree, so the timing comes from the pulses
    private readonly List<double> _syncHz = []; // measured tone of locked sync pulses (after clock correction)
    private double _tuningOffsetHz;             // receiver mistuning, from the sync tone
    private DateTime _startedUtc;
    private bool _manual;     // started by StartManual: phase unknown until the first pulse locks
    private int _manualWaits; // periods searched without a lock in manual mode

    public SstvDecoder(int sampleRate)
    {
        _demod = new FmDemodulator(sampleRate);
        _k = _demod.OutputRate / 1000.0;
        _vis = new VisDetector(_demod.OutputRate);
    }

    public event Action<SstvMode>? ImageStarted;
    /// <summary>Raised with the number of image lines drawn so far.</summary>
    public event Action<int>? LinesDecoded;
    public event Action<DecodedImage>? ImageCompleted;
    /// <summary>Raised when a VIS header turned out not to be followed by a picture.</summary>
    public event Action? ImageAborted;

    public DecoderState State { get; private set; }
    public SstvMode? CurrentMode => _mode;
    public int LineCount { get; private set; }
    /// <summary>Input RMS level of the last processed block, in dBFS.</summary>
    public double LevelDb { get; private set; } = -100;
    /// <summary>Average frequency over the last 10 ms.</summary>
    public double FrequencyHz => _vis.LastBlockHz;
    /// <summary>Mistuning of the picture being received (from its VIS header, refined on sync pulses).</summary>
    public double TuningOffsetHz => _tuningOffsetHz;

    /// <summary>
    /// Decode every header as this mode instead of the one it names (the header still gives the timing).
    /// For re-decoding a picture whose mode was misread.
    /// </summary>
    public SstvMode? ForcedMode { get; set; }
    /// <summary>When false, VIS headers are ignored (a manual start in a recording that has no header).</summary>
    public bool DetectVis { get; set; } = true;
    /// <summary>Re-decode adjustment: moves the picture right (positive) or left, in milliseconds.</summary>
    public double ShiftMs { get; set; }
    /// <summary>Re-decode adjustment on top of the automatic slant fit, in ppm of the line period.</summary>
    public double SlantPpm { get; set; }

    /// <summary>
    /// Starts decoding <paramref name="mode"/> now, without a VIS header (it was missed, or the recording starts
    /// mid-picture). The first sync pulse is searched over a whole line period; rows start at that pulse.
    /// </summary>
    public void StartManual(SstvMode mode)
    {
        if (State == DecoderState.Receiving) EndInterrupted();
        Begin(mode, _freq.End, 0, manual: true);
    }

    public void Process(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;
        double power = 0;
        foreach (var s in samples) power += s * s;
        LevelDb = 10 * Math.Log10(power / samples.Length + 1e-12);

        var max = _demod.MaxOutput(samples.Length);
        if (_scratch.Length < max) _scratch = new float[max];
        var n = _demod.Process(samples, _scratch);

        for (var i = 0; i < n; i++)
        {
            var index = _freq.Append(_scratch[i]);
            if (_vis.Push(_scratch[i], index) is { } hit && DetectVis
                && (ForcedMode ?? SstvModes.All.FirstOrDefault(m => m.VisCode == hit.Code)) is { } mode)
            {
                // A header can't occur inside picture data (its bits need tones below 1500 Hz), so a VIS heard
                // mid-picture means a new transmission: deliver what we have and start over.
                if (State == DecoderState.Receiving) EndInterrupted();
                Begin(mode, hit.EndIndex, hit.OffsetHz, manual: false);
            }
        }

        if (State == DecoderState.Receiving) Advance();
        else _freq.TrimTo((int)(PrerollMs * _k));
    }

    /// <summary>Ends reception early. A picture with some lines is delivered as incomplete.</summary>
    public void Stop()
    {
        if (State != DecoderState.Receiving) return;
        if (_nextRender > 0) Finish(complete: false);
        else Abort();
    }

    /// <summary>Copy of the picture being received, or null when idle.</summary>
    public RgbImage? Snapshot()
    {
        lock (_imageLock)
            return _image is null ? null : new RgbImage(_image.Width, _image.Height, (byte[])_image.Data.Clone());
    }

    private double Scale => _b / _bNominal;
    private double SyncStart(int period) => _a + _b * period - _layout.SyncMs * _k * Scale;
    private double At(int period, double ms) => SyncStart(period) + ms * _k * Scale;
    /// <summary>Like <see cref="At"/> plus the user's re-decode adjustments; used only for drawing.</summary>
    private double RenderAt(int period, double ms) => At(period, ms) - ShiftMs * _k + SlantPpm * 1e-6 * _b * period;
    /// <summary>Sync threshold in raw (uncorrected) Hz: 1350 Hz moved by the mistuning and clock error.</summary>
    private double SyncThreshold => (SyncThresholdHz + _tuningOffsetHz) / Scale;

    private void Begin(SstvMode mode, long visEnd, double tuningOffsetHz, bool manual)
    {
        _mode = mode;
        _layout = mode.Layout;
        _periods = (mode.Height + _layout.LinesPerPeriod - 1) / _layout.LinesPerPeriod;
        _bNominal = _b = _layout.PeriodMs * _k;
        _a = visEnd + (_layout.FirstSyncMs + _layout.SyncMs) * _k;
        _syncs.Clear();
        _presence.Clear();
        _nextSync = 0;
        _nextRender = 0;
        _lastPresentPeriod = -1;
        _lastLockPeriod = -1;
        _confirmed = false;
        _syncHz.Clear();
        _tuningOffsetHz = tuningOffsetHz;
        _manual = manual;
        _manualWaits = 0;
        LineCount = 0;
        var pixels = mode.Width * mode.Height;
        _planes = [new byte[pixels], new byte[pixels], new byte[pixels]];
        _hasPlane1 = new bool[mode.Height];
        _hasPlane2 = new bool[mode.Height];
        lock (_imageLock) _image = new RgbImage(mode.Width, mode.Height);
        _startedUtc = DateTime.UtcNow;
        State = DecoderState.Receiving;
        ImageStarted?.Invoke(mode);
    }

    private void Advance()
    {
        var edgeAfter = (int)Math.Max(2, _k);
        while (_nextSync < _periods)
        {
            // Wide search until the phase is pinned down by a couple of measured pulses.
            var window = SearchWindowMs() * _k;
            var expected = _a + _b * _nextSync;
            var control = expected + _b / 2;
            if (_freq.End < Math.Max(expected + window, control + InitialWindowMs * _k) + edgeAfter + 1) break;

            var (end, score) = FindSyncEnd(expected, window);

            // Manual start: until the first pulse locks the phase is unknown, so keep searching whole periods
            // without counting them as picture lines. Give up after the signal-lost time.
            if (_manual && _lastLockPeriod < 0 && score < LockScore)
            {
                _a += _b;
                if (++_manualWaits > Math.Max(SignalLostMinPeriods, SignalLostMs / _layout.PeriodMs))
                {
                    Abort();
                    return;
                }
                continue;
            }
            // Presence uses its own narrow window, equal for sync and control. The fitted timing predicts the pulse
            // to within a millisecond or two, so narrow is enough, and equal widths keep noise at a median of ~0.
            // (Using the wide search window here makes weak, fading pictures look absent.)
            var presenceWindow = (_confirmed ? LockedWindowMs : InitialWindowMs) * _k;
            var presence = FindSyncEnd(expected, presenceWindow).Score - FindSyncEnd(control, presenceWindow).Score;
            _presence.Add(presence);
            if (score >= LockScore)
            {
                _syncs.Add((_nextSync, end));
                MeasureSyncTone(end);
                // Timing follows the pulses once two agree. A manual start has no VIS to go by, so its first
                // pulse sets the phase.
                if (_confirmed || Confirm() || (_manual && _lastLockPeriod < 0))
                {
                    _lastLockPeriod = _nextSync;
                    Refit();
                }
            }
            if (presence >= PresenceMargin) _lastPresentPeriod = _nextSync;
            _nextSync++;

            // Judged on the median, so a weak or fading picture whose pulses rarely lock is kept, while
            // noise (median ≈ 0) is recognised within a few seconds.
            if (!_manual && _nextSync == FalseTriggerPeriods && Median(_presence) < PresenceMargin)
            {
                Abort();
                return;
            }
            var lostWindow = (int)Math.Max(SignalLostMinPeriods, SignalLostMs / _layout.PeriodMs);
            if (_presence.Count >= lostWindow && Median(_presence.TakeLast(lostWindow)) < PresenceMargin)
            {
                EndInterrupted();
                return;
            }
        }

        var drawn = _nextRender;
        while (_nextRender < _nextSync && _freq.End > RenderAt(_nextRender, _layout.MaxEndMs) + 2)
            RenderPeriod(_nextRender++, compose: true);

        if (_nextRender != drawn)
        {
            LineCount = Math.Min(_mode!.Height, _nextRender * _layout.LinesPerPeriod);
            LinesDecoded?.Invoke(LineCount);
        }
        if (_nextRender == _periods) Finish(complete: true);
    }

    /// <summary>
    /// Locates the falling-to-rising edge at the end of a sync pulse: the index maximizing
    /// "samples below threshold before" + "samples above threshold after". Returns that index and its
    /// score as a fraction of the ideal (1.0 = perfect edge).
    /// </summary>
    private (double End, double Score) FindSyncEnd(double expected, double window)
    {
        var before = (int)Math.Max(2, _layout.SyncMs * 0.8 * _k);
        var after = (int)Math.Max(2, _k);
        var lo = (long)Math.Floor(expected - window);
        var hi = (long)Math.Ceiling(expected + window);
        var origin = lo - before;
        if (origin < _freq.Start) return (expected, 0);

        // Prefix count of low (sync) samples over [origin, hi + after).
        var threshold = SyncThreshold;
        var length = (int)(hi + after - origin);
        var low = new int[length + 1];
        for (var i = 0; i < length; i++)
            low[i + 1] = low[i] + (_freq[origin + i] < threshold ? 1 : 0);

        var best = -1;
        long first = 0, last = 0;
        for (var n = lo; n <= hi; n++)
        {
            var at = (int)(n - origin);
            var score = (low[at] - low[at - before]) + (after - (low[at + after] - low[at]));
            if (score > best) { best = score; first = last = n; }
            else if (score == best) last = n;
        }

        return ((first + last) / 2.0, (double)best / (before + after));
    }

    /// <summary>
    /// Half-width of the sync search: the prediction can be off by at most the clock error accumulated since
    /// the last locked pulse, so the window grows with that gap. It is capped at 40 % of a period so it
    /// never reaches a neighbouring pulse.
    /// </summary>
    private double SearchWindowMs()
    {
        if (_manual && _lastLockPeriod < 0) return 0.5 * _layout.PeriodMs; // phase unknown: search a whole period
        var periodsSinceLock = _lastLockPeriod < 0 ? _nextSync + 1 : _nextSync - _lastLockPeriod;
        var baseMs = _confirmed ? LockedWindowMs : InitialWindowMs;
        return Math.Min(baseMs + MaxSlant * _layout.PeriodMs * periodsSinceLock, 0.4 * _layout.PeriodMs);
    }

    /// <summary>
    /// Looks for an earlier locked pulse that agrees with the newest one: whole periods apart, within the
    /// tolerance plus the clock error allowed over the gap. If one does, the timing is confirmed and pulses
    /// that don't fit the pair (false edges) are dropped from the fit.
    /// </summary>
    private bool Confirm()
    {
        var latest = _syncs[^1];
        double Tolerance(int gap) => (ConfirmToleranceMs + MaxSlant * _layout.PeriodMs * Math.Abs(gap)) * _k;
        foreach (var earlier in _syncs.Take(_syncs.Count - 1).Reverse())
        {
            var gap = latest.Period - earlier.Period;
            if (Math.Abs(latest.End - earlier.End - _bNominal * gap) > Tolerance(gap)) continue;
            _syncs.RemoveAll(s => Math.Abs(s.End - earlier.End - _bNominal * (s.Period - earlier.Period)) > Tolerance(s.Period - earlier.Period));
            _confirmed = true;
            return true;
        }
        return false;
    }

    private void Refit()
    {
        if (_syncs.Count < 2)
        {
            _b = _bNominal;
            _a = Median(_syncs.Select(s => s.End - _b * s.Period));
            return;
        }

        // From the second pulse on the line period is measured, not assumed: two clean pulses already pin it
        // to ~0.02 %, which is what lets a 1 % slow sender be followed from the first lines.
        var (a, b) = LeastSquares(_syncs);
        if (_syncs.Count >= 4)
        {
            var tolerance = 1.5 * _k;
            var inliers = _syncs.Where(s => Math.Abs(s.End - (a + b * s.Period)) < tolerance).ToList();
            if (inliers.Count >= 3) (a, b) = LeastSquares(inliers);
        }

        if (Math.Abs(b / _bNominal - 1) > MaxSlant)
        {
            _b = _bNominal;
            _a = Median(_syncs.Select(s => s.End - _b * s.Period));
        }
        else
        {
            _a = a;
            _b = b;
        }
    }

    private static (double A, double B) LeastSquares(IReadOnlyList<(int Period, double End)> points)
    {
        double n = points.Count, sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var (p, t) in points)
        {
            sx += p;
            sy += t;
            sxx += (double)p * p;
            sxy += p * t;
        }
        var b = (n * sxy - sx * sy) / (n * sxx - sx * sx);
        return ((sy - b * sx) / n, b);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    private void RenderPeriod(int period, bool compose)
    {
        var mode = _mode!;
        var width = mode.Width;
        var firstRow = period * _layout.LinesPerPeriod;

        foreach (var segment in _layout.Segments)
        {
            var channel = segment.Channel;
            var row = channel == ScanChannel.Y2 ? firstRow + 1 : firstRow;
            if (row >= mode.Height) continue;

            if (channel == ScanChannel.Chroma && _layout.ChromaSeparator is { } sep)
                channel = Corrected(Average(RenderAt(period, sep.StartMs), RenderAt(period, sep.EndMs))) > SstvFrequencies.Leader
                    ? ScanChannel.BY
                    : ScanChannel.RY;

            var plane = channel switch
            {
                ScanChannel.Red or ScanChannel.Y or ScanChannel.Y2 => 0,
                ScanChannel.Green or ScanChannel.RY => 1,
                _ => 2,
            };
            // PD chroma is shared by both lines of the period.
            var rows = _layout.LinesPerPeriod == 2 && plane != 0 ? 2 : 1;

            var start = RenderAt(period, segment.StartMs);
            var pixel = segment.DurationMs * _k * Scale / width;
            for (var x = 0; x < width; x++)
            {
                var value = ToValue(Corrected(Average(start + x * pixel, start + (x + 1) * pixel)));
                for (var r = row; r < row + rows && r < mode.Height; r++)
                    _planes[plane][r * width + x] = value;
            }
            for (var r = row; r < row + rows && r < mode.Height; r++)
            {
                if (plane == 1) _hasPlane1[r] = true;
                if (plane == 2) _hasPlane2[r] = true;
            }
        }

        if (!compose) return;
        var lastRow = Math.Min(mode.Height, firstRow + _layout.LinesPerPeriod) - 1;
        // Robot 36 rows borrow the missing chroma from the neighbouring row, so redo the previous one too.
        for (var r = Math.Max(0, firstRow - 1); r <= lastRow; r++)
            ComposeRow(r);
    }

    private void ComposeRow(int row)
    {
        var mode = _mode!;
        var width = mode.Width;
        var offset = row * width;
        lock (_imageLock)
        {
            if (_image is null) return;
            if (!_layout.IsYCrCb)
            {
                for (var x = 0; x < width; x++)
                    _image[x, row] = (_planes[0][offset + x], _planes[1][offset + x], _planes[2][offset + x]);
                return;
            }

            var crRow = ChromaRow(_hasPlane1, row);
            var cbRow = ChromaRow(_hasPlane2, row);
            for (var x = 0; x < width; x++)
            {
                var y = 1.164 * (_planes[0][offset + x] - 16);
                var cr = crRow < 0 ? 0 : _planes[1][crRow * width + x] - 128;
                var cb = cbRow < 0 ? 0 : _planes[2][cbRow * width + x] - 128;
                _image[x, row] = (Clamp(y + 1.596 * cr), Clamp(y - 0.813 * cr - 0.391 * cb), Clamp(y + 2.018 * cb));
            }
        }
    }

    private int ChromaRow(bool[] has, int row)
    {
        if (has[row]) return row;
        if (row > 0 && has[row - 1]) return row - 1;
        if (row + 1 < has.Length && has[row + 1]) return row + 1;
        return -1;
    }

    /// <summary>
    /// The transmission ended early (signal lost, or a new header arrived): deliver the picture up to the
    /// last line where the signal was still present, so trailing noise is not kept. Nothing usable means abort.
    /// </summary>
    private void EndInterrupted()
    {
        if (_lastPresentPeriod < 0) Abort();
        else Finish(complete: false, periods: _lastPresentPeriod + 1);
    }

    /// <param name="periods">Periods to keep; default is everything drawn so far. Later rows are blanked.</param>
    private void Finish(bool complete, int? periods = null)
    {
        var mode = _mode!;
        var keep = Math.Min(periods ?? _nextRender, _periods);
        // Final pass: redraw everything received with the timing fitted over the whole picture.
        for (var p = 0; p < keep; p++)
            RenderPeriod(p, compose: false);
        var rows = Math.Min(mode.Height, keep * _layout.LinesPerPeriod);
        for (var r = 0; r < rows; r++)
            ComposeRow(r);

        RgbImage image;
        lock (_imageLock)
        {
            image = _image!;
            _image = null;
        }
        Array.Clear(image.Data, rows * mode.Width * 3, (mode.Height - rows) * mode.Width * 3);
        Reset();
        ImageCompleted?.Invoke(new DecodedImage(mode, image, _startedUtc, complete, _tuningOffsetHz, (Scale - 1) * 1e6));
    }

    private void Abort()
    {
        lock (_imageLock) _image = null;
        Reset();
        ImageAborted?.Invoke();
    }

    private void Reset()
    {
        State = DecoderState.Searching;
        _mode = null;
        LineCount = 0;
        _vis.Reset();
    }

    /// <summary>Weighted mean of the frequency stream over a fractional index interval.</summary>
    private double Average(double from, double to)
    {
        if (to - from < 1) return _freq[(long)Math.Round((from + to) / 2)];
        double sum = 0, weight = 0;
        for (var i = (long)Math.Floor(from); i < to; i++)
        {
            var w = Math.Min(to, i + 1) - Math.Max(from, i);
            if (w <= 0) continue;
            sum += _freq[i] * w;
            weight += w;
        }
        return sum / weight;
    }

    /// <summary>
    /// Undoes frequency errors before a tone is read as brightness. A sender (or receiver) sound card running
    /// off-speed stretches time and scales every tone by the same factor; the line-period fit measures that
    /// factor (<see cref="Scale"/>) precisely. Whatever offset remains is receiver mistuning, measured on the
    /// sync tone.
    /// </summary>
    private double Corrected(double hz) => hz * Scale - _tuningOffsetHz;

    /// <summary>Records the tone of a locked sync pulse (its middle, away from the filtered edges).</summary>
    private void MeasureSyncTone(double end)
    {
        var length = _layout.SyncMs * _k * Scale;
        var hz = Average(end - length * 0.8, end - length * 0.2) * Scale;
        if (Math.Abs(hz - SstvFrequencies.Sync - _tuningOffsetHz) > 150) return; // not a clean pulse
        _syncHz.Add(hz);
        if (_syncHz.Count >= 3)
            _tuningOffsetHz = Math.Clamp(Median(_syncHz) - SstvFrequencies.Sync, -MaxTuningOffsetHz, MaxTuningOffsetHz);
    }

    private static byte ToValue(double hz) =>
        Clamp((hz - SstvFrequencies.Black) * 255.0 / (SstvFrequencies.White - SstvFrequencies.Black));

    private static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
