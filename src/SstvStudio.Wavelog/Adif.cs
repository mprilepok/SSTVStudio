// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Globalization;
using System.Text;

namespace SstvStudio.Wavelog;

public static class Adif
{
    public const string FileHeader = "SSTV Studio ADIF export\n<ADIF_VER:5>3.1.4\n<PROGRAMID:11>SSTV Studio\n<EOH>\n";

    /// <summary>Serializes one QSO as a single ADIF record terminated by &lt;EOR&gt;.</summary>
    public static string ToRecord(QsoRecord q)
    {
        var sb = new StringBuilder();
        Field(sb, "CALL", q.Call.ToUpperInvariant());
        Field(sb, "QSO_DATE", q.StartUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        Field(sb, "TIME_ON", q.StartUtc.ToString("HHmmss", CultureInfo.InvariantCulture));
        if (q.EndUtc is { } end)
        {
            Field(sb, "QSO_DATE_OFF", end.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            Field(sb, "TIME_OFF", end.ToString("HHmmss", CultureInfo.InvariantCulture));
        }
        if (q.FrequencyHz > 0)
        {
            Field(sb, "FREQ", (q.FrequencyHz / 1_000_000.0).ToString("0.000000", CultureInfo.InvariantCulture));
            Field(sb, "BAND", BandPlan.FromFrequency(q.FrequencyHz));
        }
        Field(sb, "MODE", q.Mode);
        Field(sb, "RST_SENT", q.RstSent);
        Field(sb, "RST_RCVD", q.RstRcvd);
        Field(sb, "NAME", q.Name);
        Field(sb, "QTH", q.Qth);
        Field(sb, "GRIDSQUARE", q.Gridsquare?.ToUpperInvariant());
        Field(sb, "COMMENT", BuildComment(q));
        Field(sb, "STATION_CALLSIGN", q.StationCallsign?.ToUpperInvariant());
        Field(sb, "MY_GRIDSQUARE", q.MyGridsquare?.ToUpperInvariant());
        if (q.TxPowerW is > 0)
            Field(sb, "TX_PWR", q.TxPowerW.Value.ToString("0.#", CultureInfo.InvariantCulture));
        sb.Append("<EOR>");
        return sb.ToString();
    }

    private static string? BuildComment(QsoRecord q)
    {
        var parts = new[] { q.SstvMode is null ? null : $"SSTV {q.SstvMode}", q.Comment }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var comment = string.Join(" - ", parts);
        return comment.Length == 0 ? null : comment;
    }

    private static void Field(StringBuilder sb, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        value = value.Trim();
        sb.Append('<').Append(name).Append(':').Append(value.Length).Append('>').Append(value).Append(' ');
    }
}
