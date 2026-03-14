using Microsoft.Extensions.Logging;
using SnesEmulator.Core.Interfaces;

namespace SnesEmulator.Audio.Apu;

/// <summary>
/// SNES APU — Sony SPC700 processor + DSP-1 audio chip.
///
/// The SPC700 is a completely self-contained 8-bit CPU running at ~1.024 MHz,
/// with its own 64 KB of RAM and an 8-channel DSP that produces stereo audio
/// at 32 kHz. It communicates with the main 65C816 CPU via four 8-bit ports
/// at addresses $2140–$2143 (as seen by the main CPU).
///
/// This implementation provides:
///   - Communication port model (4 bidirectional ports)
///   - IPL ROM boot sequence stub
///   - Audio buffer generation (silence for now; full SPC700 is a future milestone)
///   - Architectural foundation for adding full SPC700 emulation
///
/// Full SPC700 implementation is a significant undertaking and is staged
/// as the next major milestone after core gameplay is working.
/// See docs/roadmap.md for details.
/// </summary>
public sealed class Apu : IApu
{
    private readonly ILogger<Apu> _logger;

    // Communication ports (shared between main CPU and APU)
    // Port 0–3: main CPU writes here → APU reads; APU writes → main CPU reads
    private readonly byte[] _portsFromCpu = new byte[4];
    private readonly byte[] _portsFromApu = new byte[4];

    // Internal APU RAM (64 KB)
    private readonly byte[] _apuRam = new byte[0x10000];

    // Simple audio accumulator for timing
    private long _masterCycleAccum;
    private int  _sampleAccum;

    // ~32 kHz output at 21.477 MHz master clock
    // 21_477_272 / 32_000 ≈ 671 master cycles per sample
    private const int MasterCyclesPerSample = 671;

    // IPL ROM: the SNES boots the SPC700 using a small 64-byte IPL ROM.
    // We stub this out so the main CPU's initialization transfers don't hang.
    private static readonly byte[] IplRom = BuildIplRomStub();

    public string Name => "APU (SPC700 + DSP)";

    public Apu(ILogger<Apu> logger)
    {
        _logger = logger;
    }

    // ── IEmulatorComponent ────────────────────────────────────────────────────
    private bool _handshakeComplete;
    public void Reset()
    {
        Array.Clear(_portsFromCpu);
        Array.Clear(_portsFromApu);
        Array.Clear(_apuRam);
        _masterCycleAccum = 0;
        _handshakeComplete = false;

        Buffer.BlockCopy(IplRom, 0, _apuRam, 0xFFC0, IplRom.Length);

        // Signal APU ready: $AA/$BB
        _portsFromApu[0] = 0xAA;
        _portsFromApu[1] = 0xBB;
    }

    // ── IApu ──────────────────────────────────────────────────────────────────

    public void Clock(int masterCycles)
    {
        _masterCycleAccum += masterCycles;
        // Future: step the SPC700 CPU in proportion to master cycles
        // SPC700 runs at ~1.024 MHz = masterClock / 21 approx
    }

    public byte ReadPort(byte port)
    {
        if (port > 3) return 0xFF;
        // Main CPU reads what the APU has written
        return _portsFromApu[port & 3];
    }

    public void WritePort(byte port, byte value)
    {
        if (port > 3) return;
        _portsFromCpu[port] = value;

        if (!_handshakeComplete)
        {
            if (port == 0 && value == 0xCC)
            {
                // CPU acknowledged ready signal — handshake done
                _handshakeComplete = true;
                _portsFromApu[0] = 0xCC;
            }
            // Don't echo during handshake — keep $AA/$BB visible
            return;
        }

        // Post-handshake: echo everything back unconditionally
        _portsFromApu[port] = value;
    }

    public void FillAudioBuffer(short[] buffer, int offset, int count)
    {
        // Output silence until full DSP is implemented
        // This prevents audio thread starvation
        Array.Clear(buffer, offset, count);
    }

    // ── IPL ROM handshake simulation ──────────────────────────────────────────

    /// <summary>
    /// Simulates the SPC700 IPL ROM communication protocol enough
    /// to allow most games to proceed past their audio initialization.
    ///
    /// The real IPL protocol:
    ///   1. APU signals ready: port 0 = $AA, port 1 = $BB
    ///   2. Main CPU writes port 0 = $CC to start transfer
    ///   3. APU acknowledges with port 0 = $CC
    ///   4. Data transfer proceeds via ports 0–3
    /// </summary>
    private byte _lastPort0Write;

    private void SimulateIplHandshake(byte port, byte value)
    {
        switch (port)
        {
            case 0:
                if (value == 0xCC)
                {
                    // Step 2: CPU requests transfer start → acknowledge
                    _portsFromApu[0] = 0xCC;
                }
                else if (value != 0 && value != _lastPort0Write)
                {
                    // Step 4+: CPU sends incrementing counter → echo it back
                    _portsFromApu[0] = value;
                }
                _lastPort0Write = value;
                break;

            case 1:
                // Some games write the destination address to port 1
                // Echo it to unblock the wait loop
                _portsFromApu[1] = value;
                break;
        }
    }

    // ── State ─────────────────────────────────────────────────────────────────

    public byte[] SaveState()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write(_portsFromCpu);
        bw.Write(_portsFromApu);
        bw.Write(_apuRam);
        return ms.ToArray();
    }

    public void LoadState(byte[] state)
    {
        using var ms = new System.IO.MemoryStream(state);
        using var br = new System.IO.BinaryReader(ms);
        br.Read(_portsFromCpu);
        br.Read(_portsFromApu);
        br.Read(_apuRam);
    }

    // ── IPL ROM stub ──────────────────────────────────────────────────────────

    private static byte[] BuildIplRomStub()
    {
        // 64-byte stub that mimics the IPL ROM interface
        // Real IPL ROM is copyrighted by Sony; this is an independent stub.
        var rom = new byte[64];
        // NOP sled + infinite loop to keep APU "alive"
        Array.Fill(rom, (byte)0x00); // NOP (SPC700: 0x00 = NOP)
        rom[62] = 0x2F; // BRA -2 (infinite loop)
        rom[63] = 0xFE;
        return rom;
    }
}
