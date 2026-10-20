// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.IO.Ports;

namespace SstvStudio.Hamlib;

public enum PttMethod { Rigctld, SerialRts, SerialDtr, Vox }

public interface IPttSwitch : IAsyncDisposable
{
    Task SetAsync(bool transmit, CancellationToken ct = default);
}

public static class PttSwitch
{
    public static IPttSwitch Create(PttMethod method, RigService rig, string serialPort) => method switch
    {
        PttMethod.Rigctld => new RigctldPtt(rig),
        PttMethod.SerialRts => new SerialPtt(serialPort, useRts: true),
        PttMethod.SerialDtr => new SerialPtt(serialPort, useRts: false),
        _ => new VoxPtt(),
    };
}

public sealed class RigctldPtt(RigService rig) : IPttSwitch
{
    public Task SetAsync(bool transmit, CancellationToken ct = default) => rig.SetPttAsync(transmit, ct);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Keys the rig through the RTS or DTR line of a (USB-)serial port.</summary>
public sealed class SerialPtt : IPttSwitch
{
    private readonly SerialPort _port;
    private readonly bool _useRts;

    public SerialPtt(string portName, bool useRts)
    {
        _useRts = useRts;
        _port = new SerialPort(portName) { RtsEnable = false, DtrEnable = false };
    }

    public Task SetAsync(bool transmit, CancellationToken ct = default)
    {
        if (!_port.IsOpen) _port.Open();
        if (_useRts) _port.RtsEnable = transmit;
        else _port.DtrEnable = transmit;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_port.IsOpen)
        {
            _port.RtsEnable = false;
            _port.DtrEnable = false;
            _port.Close();
        }
        _port.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>No keying; the rig's VOX does it.</summary>
public sealed class VoxPtt : IPttSwitch
{
    public Task SetAsync(bool transmit, CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
