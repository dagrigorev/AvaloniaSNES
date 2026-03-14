using SnesEmulator.Core;
using SnesEmulator.Core.Interfaces;

namespace SnesEmulator.Emulation.Memory;

/// <summary>
/// SNES Work RAM (WRAM): 128 KB of general-purpose RAM.
///
/// Memory map presence:
///   $7E0000–$7FFFFF  — full 128 KB view (banks $7E and $7F)
///   $0000–$1FFF in banks $00–$3F and $80–$BF — mirror of first 8 KB
///
/// The bus handles address routing; this class owns just the backing store.
/// </summary>
public sealed class WorkRam : IMemoryMappedDevice, IStateful
{
    /// <summary>Raw WRAM storage (128 KB).</summary>
    private readonly byte[] _ram = new byte[SnesConstants.WramSize];

    /// <summary>WRAM address port for DMA/CPU indirect access ($2180).</summary>
    private uint _addressPort;

    public string Name => "WRAM";

    /// <inheritdoc />
    public void Reset()
    {
        // On real hardware, WRAM is not cleared on reset — it retains its last contents.
        // We clear it here for deterministic emulation startup.
        Array.Clear(_ram, 0, _ram.Length);
        _addressPort = 0;
    }

    // ── IMemoryMappedDevice ──────────────────────────────────────────────────

    public byte Read(uint address)
    {
        // Caller has already resolved the address to a WRAM-relative offset
        return _ram[address & (SnesConstants.WramSize - 1)];
    }

    public void Write(uint address, byte value)
    {
        _ram[address & (SnesConstants.WramSize - 1)] = value;
    }

    // ── Direct bank access (for bus mapping) ─────────────────────────────────

    /// <summary>Reads directly from a WRAM offset (0x00000–0x1FFFF).</summary>
    public byte ReadDirect(int offset) => _ram[offset & 0x1FFFF];

    /// <summary>Writes directly to a WRAM offset.</summary>
    public void WriteDirect(int offset, byte value) => _ram[offset & 0x1FFFF] = value;

    // ── WDMADD port ($2180–$2183) ─────────────────────────────────────────────

    /// <summary>Handles reads from the WMDATA port ($2180).</summary>
    public byte ReadWmData()
    {
        byte value = _ram[_addressPort & 0x1FFFF];
        _addressPort = (_addressPort + 1) & 0x1FFFF;
        return value;
    }

    /// <summary>Handles writes to the WMDATA port ($2180).</summary>
    public void WriteWmData(byte value)
    {
        _ram[_addressPort & 0x1FFFF] = value;
        _addressPort = (_addressPort + 1) & 0x1FFFF;
    }

    /// <summary>Sets low byte of WRAM address port ($2181).</summary>
    public void WriteWmAddressLow(byte value)  => _addressPort = (_addressPort & 0x1FF00) | value;

    /// <summary>Sets mid byte of WRAM address port ($2182).</summary>
    public void WriteWmAddressMid(byte value)  => _addressPort = (_addressPort & 0x100FF) | ((uint)value << 8);

    /// <summary>Sets high bit of WRAM address port ($2183, only bit 0 matters).</summary>
    public void WriteWmAddressHigh(byte value) => _addressPort = (_addressPort & 0x0FFFF) | (((uint)(value & 1)) << 16);

    // ── IStateful ────────────────────────────────────────────────────────────

    public byte[] SaveState()
    {
        byte[] state = new byte[_ram.Length + 4];
        Buffer.BlockCopy(_ram, 0, state, 0, _ram.Length);
        state[_ram.Length]     = (byte)(_addressPort & 0xFF);
        state[_ram.Length + 1] = (byte)((_addressPort >> 8) & 0xFF);
        state[_ram.Length + 2] = (byte)((_addressPort >> 16) & 0xFF);
        state[_ram.Length + 3] = 0;
        return state;
    }

    public void LoadState(byte[] state)
    {
        if (state.Length < _ram.Length)
            throw new InvalidOperationException("WRAM state data is too short.");
        Buffer.BlockCopy(state, 0, _ram, 0, _ram.Length);
        _addressPort = (uint)(state[_ram.Length] | (state[_ram.Length + 1] << 8) | (state[_ram.Length + 2] << 16));
    }
}
