using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SnesEmulator.Core.Exceptions;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Emulation.SaveState;
using Xunit;

namespace SnesEmulator.Emulation.Tests;

/// <summary>Tests for save/load state serialization.</summary>
public sealed class SaveStateManagerTests
{
    private readonly SaveStateManager _manager = new(NullLogger<SaveStateManager>.Instance);

    private static Mock<ICpu> MakeCpuMock(byte[]? stateData = null)
    {
        stateData ??= new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var mock = new Mock<ICpu>();
        mock.Setup(c => c.SaveState()).Returns(stateData);
        mock.Setup(c => c.LoadState(It.IsAny<byte[]>()));
        return mock;
    }

    private static Mock<IPpu> MakePpuMock(byte[]? stateData = null)
    {
        stateData ??= new byte[] { 10, 20, 30 };
        var mock = new Mock<IPpu>();
        mock.Setup(p => p.SaveState()).Returns(stateData);
        mock.Setup(p => p.LoadState(It.IsAny<byte[]>()));
        return mock;
    }

    private static Mock<IApu> MakeApuMock(byte[]? stateData = null)
    {
        stateData ??= new byte[] { 0xAA, 0xBB };
        var mock = new Mock<IApu>();
        mock.Setup(a => a.SaveState()).Returns(stateData);
        mock.Setup(a => a.LoadState(It.IsAny<byte[]>()));
        return mock;
    }

    private static Mock<IStateful> MakeWramMock(byte[]? stateData = null)
    {
        stateData ??= Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        var mock = new Mock<IStateful>();
        mock.Setup(w => w.SaveState()).Returns(stateData);
        mock.Setup(w => w.LoadState(It.IsAny<byte[]>()));
        return mock;
    }

    [Fact]
    public void SaveState_CreatesFileAtPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.state");
        try
        {
            _manager.SaveState(path, MakeCpuMock().Object, MakePpuMock().Object,
                               MakeApuMock().Object, MakeWramMock().Object);

            File.Exists(path).Should().BeTrue();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SaveThenLoad_CallsLoadStateOnAllComponents()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.state");
        try
        {
            var cpuData  = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var ppuData  = new byte[] { 10, 20, 30 };
            var apuData  = new byte[] { 0xAA, 0xBB };
            var wramData = new byte[] { 0x01, 0x02, 0x03 };

            var cpuMock  = MakeCpuMock(cpuData);
            var ppuMock  = MakePpuMock(ppuData);
            var apuMock  = MakeApuMock(apuData);
            var wramMock = MakeWramMock(wramData);

            _manager.SaveState(path, cpuMock.Object, ppuMock.Object,
                               apuMock.Object, wramMock.Object);

            // Load into fresh mocks
            var cpu2  = MakeCpuMock();
            var ppu2  = MakePpuMock();
            var apu2  = MakeApuMock();
            var wram2 = MakeWramMock();

            _manager.LoadState(path, cpu2.Object, ppu2.Object,
                               apu2.Object, wram2.Object);

            cpu2.Verify(c => c.LoadState(It.Is<byte[]>(d => d.SequenceEqual(cpuData))), Times.Once);
            ppu2.Verify(p => p.LoadState(It.Is<byte[]>(d => d.SequenceEqual(ppuData))), Times.Once);
            apu2.Verify(a => a.LoadState(It.Is<byte[]>(d => d.SequenceEqual(apuData))), Times.Once);
            wram2.Verify(w => w.LoadState(It.Is<byte[]>(d => d.SequenceEqual(wramData))), Times.Once);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LoadState_FileNotFound_ThrowsSaveStateException()
    {
        Action act = () => _manager.LoadState(
            "/nonexistent/path/game.state",
            MakeCpuMock().Object, MakePpuMock().Object,
            MakeApuMock().Object, MakeWramMock().Object);

        act.Should().Throw<SaveStateException>()
           .WithMessage("*not found*");
    }

    [Fact]
    public void LoadState_InvalidMagic_ThrowsSaveStateException()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bad_{Guid.NewGuid():N}.state");
        try
        {
            File.WriteAllBytes(path, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 });

            Action act = () => _manager.LoadState(path, MakeCpuMock().Object, MakePpuMock().Object,
                                                   MakeApuMock().Object, MakeWramMock().Object);
            act.Should().Throw<SaveStateException>()
               .WithMessage("*Invalid save state*");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
