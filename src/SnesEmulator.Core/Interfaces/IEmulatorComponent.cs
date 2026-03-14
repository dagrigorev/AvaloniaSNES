namespace SnesEmulator.Core.Interfaces;

/// <summary>
/// Base interface for all emulator hardware components.
/// Every emulated component implements this to participate in
/// the unified lifecycle: reset, clock step, and state serialization.
/// </summary>
public interface IEmulatorComponent
{
    /// <summary>Gets the human-readable name of this component.</summary>
    string Name { get; }

    /// <summary>
    /// Resets the component to its power-on state.
    /// Called both at initial power-on and on hard reset.
    /// </summary>
    void Reset();
}

/// <summary>
/// Interface for components that can be clocked (stepped by CPU cycles).
/// </summary>
public interface IClockable : IEmulatorComponent
{
    /// <summary>
    /// Advances the component by the specified number of master clock cycles.
    /// </summary>
    /// <param name="masterCycles">Number of master clock cycles to advance.</param>
    void Clock(int masterCycles);
}

/// <summary>
/// Interface for components that participate in state save/load.
/// </summary>
public interface IStateful
{
    /// <summary>Serializes the component's internal state to a byte array.</summary>
    byte[] SaveState();

    /// <summary>Restores the component's internal state from a byte array.</summary>
    void LoadState(byte[] state);
}
