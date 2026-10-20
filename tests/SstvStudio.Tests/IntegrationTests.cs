// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Hamlib;
using SstvStudio.Services;
using SstvStudio.Wavelog;

namespace SstvStudio.Tests;

public class AdifTests
{
    [Fact]
    public void ToRecord_WritesLengthPrefixedFields()
    {
        var qso = new QsoRecord
        {
            Call = "ok1abc",
            StartUtc = new DateTime(2026, 10, 19, 9, 5, 7, DateTimeKind.Utc),
            FrequencyHz = 14_230_000,
            SstvMode = "Martin M1",
            RstSent = "595",
            RstRcvd = "585",
            StationCallsign = "om6xx",
        };

        var adif = Adif.ToRecord(qso);

        Assert.Contains("<CALL:6>OK1ABC ", adif);
        Assert.Contains("<QSO_DATE:8>20261019 ", adif);
        Assert.Contains("<TIME_ON:6>090507 ", adif);
        Assert.Contains("<FREQ:9>14.230000 ", adif);
        Assert.Contains("<BAND:3>20m ", adif);
        Assert.Contains("<MODE:4>SSTV ", adif);
        Assert.Contains("<RST_RCVD:3>585 ", adif);
        Assert.Contains("<COMMENT:14>SSTV Martin M1 ", adif);
        Assert.Contains("<STATION_CALLSIGN:5>OM6XX ", adif);
        Assert.DoesNotContain("NAME", adif);
        Assert.EndsWith("<EOR>", adif);
    }

    [Theory]
    [InlineData(3_730_000, "80m")]
    [InlineData(7_171_000, "40m")]
    [InlineData(14_230_000, "20m")]
    [InlineData(28_680_000, "10m")]
    [InlineData(145_800_000, "2m")]
    [InlineData(433_400_000, "70cm")]
    [InlineData(9_000_000, null)]
    public void BandPlan_MapsFrequency(long hz, string? band) => Assert.Equal(band, BandPlan.FromFrequency(hz));

    [Theory]
    [InlineData("https://log.example.com", "https://log.example.com/index.php/api/qso")]
    [InlineData("https://log.example.com/wavelog/", "https://log.example.com/wavelog/index.php/api/qso")]
    [InlineData("https://log.example.com/index.php", "https://log.example.com/index.php/api/qso")]
    public void WavelogUrl_NormalizesBase(string baseUrl, string expected) =>
        Assert.Equal(expected, WavelogClient.Url(new WavelogOptions(baseUrl, "k", "1"), "api/qso").ToString());
}

public class RigctldTests
{
    [Fact]
    public void ParseResponse_StripsRprtZero() =>
        Assert.Equal(["USB", "2400"], RigctldClient.ParseResponse("USB\n2400\nRPRT 0\n"));

    [Fact]
    public void ParseResponse_AcceptsMissingRprt() =>
        Assert.Equal(["14230000"], RigctldClient.ParseResponse("14230000\n"));

    [Theory]
    // Signal heard 40 Hz too high: on USB the dial goes up 40 Hz, on LSB (mirrored audio) down 40 Hz.
    [InlineData("USB", 40, 14_230_040)]
    [InlineData("PKTUSB", 40, 14_230_040)]
    [InlineData("USB", -25, 14_229_975)]
    [InlineData("LSB", 40, 14_229_960)]
    [InlineData("PKTLSB", -25, 14_230_025)]
    public void RetunedFrequency_FollowsSideband(string mode, double offsetHz, long expected) =>
        Assert.Equal(expected, RigService.RetunedFrequency(14_230_000, mode, offsetHz));

    [Theory]
    [InlineData("FM")]
    [InlineData("AM")]
    [InlineData("CW")]
    public void RetunedFrequency_RefusesNonSsbModes(string mode) =>
        Assert.Null(RigService.RetunedFrequency(145_500_000, mode, 40));

    [Fact]
    public void ParseResponse_ThrowsOnError() =>
        Assert.Equal(-11, Assert.Throws<RigctldException>(() => RigctldClient.ParseResponse("RPRT -11\n")).Code);
}

public class RetuneTests
{
    private static RigStatus Rig(long hz, string mode = "USB") => new(true, hz, mode, false, null);
    private static readonly RigDial PictureDial = new(14_230_000, "USB");

    [Fact]
    public void Plan_UsesThePicturesDialNotTheCurrentOne()
    {
        // Already retuned by +40 Hz once: pressing again on the same picture must aim at the same frequency.
        Assert.Equal(14_230_040, Retune.Plan(Rig(14_230_040), PictureDial, 40).TargetHz);
        Assert.Equal(14_230_040, Retune.Plan(Rig(14_230_000), PictureDial, 40).TargetHz);
    }

    [Fact]
    public void Plan_FollowsSideband() =>
        Assert.Equal(14_229_960, Retune.Plan(Rig(14_230_000, "LSB"), PictureDial with { Mode = "LSB" }, 40).TargetHz);

    [Theory]
    [InlineData(false, true, 14_230_000, "USB", "Connect the rig")]
    [InlineData(true, false, 14_230_000, "USB", "wasn't connected")]
    [InlineData(true, true, 7_100_000, "USB", "has moved")]
    [InlineData(true, true, 14_230_000, "LSB", "has moved")]
    public void Plan_Refuses(bool connected, bool dialKnown, long rigHz, string rigMode, string reason)
    {
        var rig = new RigStatus(connected, rigHz, rigMode, false, null);
        var (target, refusal) = Retune.Plan(rig, dialKnown ? PictureDial : null, 40);
        Assert.Null(target);
        Assert.Contains(reason, refusal);
    }

    [Fact]
    public void Plan_RefusesNonSsbModes() =>
        Assert.Contains("FM mode", Retune.Plan(Rig(145_500_000, "FM"), new RigDial(145_500_000, "FM"), 40).Refusal);

    [Fact]
    public void Describe_ExactMove() =>
        Assert.Equal(new RetuneResult(true, "Retuned 14.230.000 → 14.230.040 (+40 Hz)"),
            Retune.Describe(14_230_000, 14_230_040, 14_230_040));

    [Fact]
    public void Describe_RoundedCloseEnough() =>
        // 10 Hz tuning step: 3 Hz off is fine and needs no explanation.
        Assert.Equal(new RetuneResult(true, "Retuned 14.230.000 → 14.230.050 (+50 Hz)"),
            Retune.Describe(14_230_000, 14_230_047, 14_230_050));

    [Fact]
    public void Describe_StepTooCoarse()
    {
        var result = Retune.Describe(14_230_000, 14_230_047, 14_230_100);
        Assert.False(result.Corrected);
        Assert.Contains("still 53 Hz off", result.Message);
    }

    [Fact]
    public void Describe_IgnoredChange()
    {
        var result = Retune.Describe(14_230_000, 14_230_040, 14_230_000);
        Assert.False(result.Corrected);
        Assert.Contains("didn't move from 14.230.000", result.Message);
    }
}
