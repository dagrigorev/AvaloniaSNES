using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SnesEmulator.Audio.Apu;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Emulation.Cpu;
using SnesEmulator.Emulation.Memory;
using SnesEmulator.Emulation.SaveState;
using SnesEmulator.Emulation.Timing;
using SnesEmulator.Graphics.Ppu;
using SnesEmulator.Hardware.Rom;
using SnesEmulator.Infrastructure.Logging;
using SnesEmulator.Input.Controllers;

namespace SnesEmulator.Infrastructure.DependencyInjection;

/// <summary>
/// Registers all emulator services with the DI container.
/// Uses the Builder pattern for clean, testable composition.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all SNES emulator services to the service collection.
    /// </summary>
    public static IServiceCollection AddSnesEmulator(this IServiceCollection services)
    {
        // ── Logging ──────────────────────────────────────────────────────────
        services.AddSingleton<DiagnosticLogSink>();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddConsole();
            // The DiagnosticLogSink is added as a provider after the container is built
        });

        // ── Hardware components (all singletons — one instance per emulator) ─
        services.AddSingleton<WorkRam>();
        services.AddSingleton<MemoryBus>();
        services.AddSingleton<IMemoryBus>(sp => sp.GetRequiredService<MemoryBus>());

        services.AddSingleton<Cpu65C816>();
        services.AddSingleton<ICpu>(sp => sp.GetRequiredService<Cpu65C816>());

        services.AddSingleton<Ppu>();
        services.AddSingleton<IPpu>(sp => sp.GetRequiredService<Ppu>());

        services.AddSingleton<Apu>();
        services.AddSingleton<IApu>(sp => sp.GetRequiredService<Apu>());

        services.AddSingleton<InputManager>();
        services.AddSingleton<IInputManager>(sp => sp.GetRequiredService<InputManager>());

        // ── Infrastructure ────────────────────────────────────────────────────
        services.AddSingleton<IRomLoader, RomLoader>();
        services.AddSingleton<EmulationLoop>();
        services.AddSingleton<SaveStateManager>();

        // ── Top-level Emulator facade ─────────────────────────────────────────
        services.AddSingleton<SnesEmulatorFacade>();
        services.AddSingleton<IEmulator>(sp => sp.GetRequiredService<SnesEmulatorFacade>());

        return services;
    }
}
