using Microsoft.Extensions.Logging;
using SnesEmulator.Core.Interfaces;

namespace SnesEmulator.Audio.Apu;

/// <summary>
/// SNES APU stub — SPC700 + DSP communication layer.
///
/// Full IPL handshake protocol (per SNES hardware):
///   1. On power-on APU sets port0=$AA, port1=$BB (ready signal)
///   2. CPU polls port0 until it reads $AA, port1 until it reads $BB
///   3. CPU writes $CC to port0 to begin transfer
///   4. APU echoes $CC on port0
///   5. For each data block: CPU writes address (port2/3), data (port1),
///      then increments a counter on port0. APU echoes the counter.
///   6. CPU sends $00 on port0 when done → APU jumps to loaded code
///
/// Our stub handles all phases so games proceed past audio init.
/// </summary>
public sealed class Apu : IApu
{
    private readonly ILogger<Apu> _logger;

    private readonly byte[] _portsFromCpu = new byte[4];
    private readonly byte[] _portsFromApu = new byte[4];
    private readonly byte[] _apuRam       = new byte[0x10000];

    private long _masterCycleAccum;

    // Handshake state machine
    private enum HandshakePhase { WaitingForCC, Transferring, Done }
    private HandshakePhase _phase;
    private byte _lastCounter; // tracks the incrementing port0 counter

    public string Name => "APU (SPC700 + DSP)";

    public Apu(ILogger<Apu> logger) => _logger = logger;

    public void Reset()
    {
        Array.Clear(_portsFromCpu);
        Array.Clear(_portsFromApu);
        Array.Clear(_apuRam);
        _masterCycleAccum = 0;
        _phase            = HandshakePhase.WaitingForCC;
        _lastCounter      = 0;

        // Signal APU ready: port0=$AA, port1=$BB
        _portsFromApu[0] = 0xAA;
        _portsFromApu[1] = 0xBB;
    }

    public void Clock(int masterCycles)
    {
        _masterCycleAccum += masterCycles;
    }

    public byte ReadPort(byte port)
    {
        if (port > 3) return 0xFF;
        return _portsFromApu[port];
    }

    public void WritePort(byte port, byte value)
    {
        if (port > 3) return;
        _portsFromCpu[port] = value;

        switch (_phase)
        {
            case HandshakePhase.WaitingForCC:
                // CPU writes $CC to acknowledge the $AA/$BB ready signal
                if (port == 0 && value == 0xCC)
                {
                    _portsFromApu[0] = 0xCC; // Echo $CC back
                    _phase           = HandshakePhase.Transferring;
                    _lastCounter     = 0;
                    _logger.LogDebug("APU: IPL handshake complete, entering transfer phase");
                }
                break;

            case HandshakePhase.Transferring:
                if (port == 0)
                {
                    if (value == 0x00)
                    {
                        // Transfer complete — CPU tells APU to execute
                        _portsFromApu[0] = 0x00;
                        _phase           = HandshakePhase.Done;
                        _logger.LogDebug("APU: Transfer done, executing uploaded code");
                    }
                    else
                    {
                        // CPU sends incrementing counter — echo it immediately
                        _portsFromApu[0] = value;
                        _lastCounter     = value;
                    }
                }
                else
                {
                    // Echo ports 1-3 so address/data writes don't stall
                    _portsFromApu[port] = value;
                }
                break;

            case HandshakePhase.Done:
                // Post-init: echo everything so games that poll ports don't hang
                _portsFromApu[port] = value;
                break;
        }
    }

    public void FillAudioBuffer(short[] buffer, int offset, int count)
        => Array.Clear(buffer, offset, count);

    public byte[] SaveState()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write(_portsFromCpu);
        bw.Write(_portsFromApu);
        bw.Write(_apuRam);
        bw.Write((byte)_phase);
        bw.Write(_lastCounter);
        return ms.ToArray();
    }

    public void LoadState(byte[] state)
    {
        using var ms = new System.IO.MemoryStream(state);
        using var br = new System.IO.BinaryReader(ms);
        br.Read(_portsFromCpu);
        br.Read(_portsFromApu);
        br.Read(_apuRam);
        _phase       = (HandshakePhase)br.ReadByte();
        _lastCounter = br.ReadByte();
    }
}
