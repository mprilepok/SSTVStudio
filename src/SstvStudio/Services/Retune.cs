// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Hamlib;

namespace SstvStudio.Services;

/// <summary>Where the rig was tuned while a picture was received.</summary>
public sealed record RigDial(long FrequencyHz, string Mode);

/// <summary>Outcome of "Fix tuning": <see cref="Corrected"/> when the rig now sits where the picture says it should.</summary>
public sealed record RetuneResult(bool Corrected, string Message);

/// <summary>
/// "Fix tuning" logic. The target comes from the dial the picture was received on, not the dial now, so pressing
/// the button twice, or on an older picture after a retune, sends the same frequency instead of correcting twice.
/// </summary>
public static class Retune
{
    /// <summary>Offsets below this count as tuned; a retune landing this close to the target counts as done.</summary>
    public const double ToleranceHz = 10;

    /// <summary>How far the dial may be from the picture's and still count as the same frequency (earlier retunes stay well inside).</summary>
    public const long MaxDialMoveHz = 1000;

    /// <summary>The dial frequency that centres the picture's signal, or why the rig can't be retuned.</summary>
    public static (long? TargetHz, string? Refusal) Plan(RigStatus rig, RigDial? dial, double audioOffsetHz)
    {
        if (!rig.Connected) return (null, "Connect the rig to retune it.");
        if (dial is null)
            return (null, "Can't fix: the rig wasn't connected, or was retuned, during this picture.");
        if (!string.Equals(rig.Mode, dial.Mode, StringComparison.OrdinalIgnoreCase) ||
            Math.Abs(rig.FrequencyHz - dial.FrequencyHz) > MaxDialMoveHz)
            return (null, $"Can't fix: the rig has moved since this picture ({RigService.FormatFrequency(dial.FrequencyHz)} {dial.Mode}).");
        if (RigService.RetunedFrequency(dial.FrequencyHz, dial.Mode, audioOffsetHz) is not { } target)
            return (null, $"Can't retune in {dial.Mode} mode (only USB/LSB and their data modes).");
        return (target, null);
    }

    /// <summary>Describes a dial change from <paramref name="fromHz"/>, given what the rig reported back afterwards.</summary>
    public static RetuneResult Describe(long fromHz, long targetHz, long actualHz)
    {
        if (actualHz == fromHz)
            return new(false, $"The rig didn't move from {RigService.FormatFrequency(fromHz)}. Is the VFO locked?");
        var moved = $"Retuned {RigService.FormatFrequency(fromHz)} → {RigService.FormatFrequency(actualHz)} ({actualHz - fromHz:+0;-0} Hz)";
        if (actualHz == targetHz) return new(true, moved);
        var off = actualHz - targetHz;
        return Math.Abs(off) < ToleranceHz
            ? new(true, moved)
            : new(false, $"{moved}, still {Math.Abs(off)} Hz off: the rig's tuning step is too coarse.");
    }
}
