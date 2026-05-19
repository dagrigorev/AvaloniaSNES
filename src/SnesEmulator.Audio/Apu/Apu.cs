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
///   6. After transfer, APU jumps to loaded code and sets $AA/$BB running signal
///
/// Our stub echoes all writes during transfer so polling loops match.
/// After N idle reads (no writes), it auto-responds with $AA/$BB to
/// simulate the SPC700 running signal, allowing games to proceed past
/// SPC700 init without actual SPC700 emulation.
/// </summary>
public sealed class Apu : IApu
{
    private readonly ILogger<Apu> _logger;

    private readonly byte[] _portsFromCpu = new byte[4];
    private readonly byte[] _portsFromApu = new byte[4];
    private readonly byte[] _apuRam       = new byte[0x10000];

    private long _masterCycleAccum;

    // Handshake state machine
    private enum HandshakePhase { WaitingForCC, Transferring, Busy, Ready }
    private HandshakePhase _phase;
    private byte _lastCounter;

    // After this many consecutive reads without any write, signal ready ($AA/$BB)
    private const int IdleReadThreshold = 500;
    private int _readsSinceLastWrite;

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
        _readsSinceLastWrite = 0;

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

        // Auto-detect end of transfer: after many idle reads without writes,
        // switch to ready-signal mode ($AA/$BB) so games that poll for the
        // SPC700 running signal can proceed.
        if (_phase == HandshakePhase.Transferring || _phase == HandshakePhase.Busy)
        {
            _readsSinceLastWrite++;
            if (_readsSinceLastWrite >= IdleReadThreshold)
            {
                _phase = HandshakePhase.Ready;
                _portsFromApu[0] = 0xAA;
                _portsFromApu[1] = 0xBB;
                _logger.LogDebug("APU: auto-detect idle -> ready ($AA/$BB)");
            }
        }

        return _portsFromApu[port];
    }

    public void WritePort(byte port, byte value)
    {
        if (port > 3) return;
        _portsFromCpu[port] = value;
        _readsSinceLastWrite = 0;

        switch (_phase)
        {
            case HandshakePhase.WaitingForCC:
                if (port == 0 && value == 0xCC)
                {
                    _portsFromApu[0] = 0xCC;
                    _phase           = HandshakePhase.Transferring;
                    _lastCounter     = 0;
                    _logger.LogDebug("APU: IPL handshake complete, entering transfer phase");
                }
                break;

            case HandshakePhase.Transferring:
            case HandshakePhase.Busy:
                _portsFromApu[port] = value;
                break;

            case HandshakePhase.Ready:
                // Game is writing again — switch back to transfer echo mode
                _phase = HandshakePhase.Transferring;
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
        bw.Write(_readsSinceLastWrite);
        return ms.ToArray();
    }

    public void LoadState(byte[] state)
    {
        using var ms = new System.IO.MemoryStream(state);
        using var br = new System.IO.BinaryReader(ms);
        br.Read(_portsFromCpu);
        br.Read(_portsFromApu);
        br.Read(_apuRam);
        _phase              = (HandshakePhase)br.ReadByte();
        _lastCounter        = br.ReadByte();
        _readsSinceLastWrite = state.Length >= ms.Position + 4 ? br.ReadInt32() : 0;
    }
}
