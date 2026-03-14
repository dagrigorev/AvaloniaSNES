using System.Collections.ObjectModel;
using Avalonia.Threading;
using SnesEmulator.Core.Models;
using SnesEmulator.Infrastructure.Logging;
using ReactiveUI;

namespace SnesEmulator.Desktop.ViewModels;

/// <summary>
/// ViewModel for the CPU register diagnostic panel.
/// Updated from the main VM's timer or on Step.
/// </summary>
public sealed class CpuStateViewModel : ReactiveObject
{
    private string _aReg    = "A:  0000";
    private string _xReg    = "X:  0000";
    private string _yReg    = "Y:  0000";
    private string _spReg   = "SP: 0000";
    private string _dpReg   = "DP: 0000";
    private string _pcReg   = "PC: 00:0000";
    private string _dbrReg  = "DBR: 00";
    private string _pReg    = "P:  --------";
    private string _eFlag   = "E: 1";
    private string _cycles  = "Cycles: 0";

    public string AReg   { get => _aReg;   private set => this.RaiseAndSetIfChanged(ref _aReg, value); }
    public string XReg   { get => _xReg;   private set => this.RaiseAndSetIfChanged(ref _xReg, value); }
    public string YReg   { get => _yReg;   private set => this.RaiseAndSetIfChanged(ref _yReg, value); }
    public string SpReg  { get => _spReg;  private set => this.RaiseAndSetIfChanged(ref _spReg, value); }
    public string DpReg  { get => _dpReg;  private set => this.RaiseAndSetIfChanged(ref _dpReg, value); }
    public string PcReg  { get => _pcReg;  private set => this.RaiseAndSetIfChanged(ref _pcReg, value); }
    public string DbrReg { get => _dbrReg; private set => this.RaiseAndSetIfChanged(ref _dbrReg, value); }
    public string PReg   { get => _pReg;   private set => this.RaiseAndSetIfChanged(ref _pReg, value); }
    public string EFlag  { get => _eFlag;  private set => this.RaiseAndSetIfChanged(ref _eFlag, value); }
    public string Cycles { get => _cycles; private set => this.RaiseAndSetIfChanged(ref _cycles, value); }

    /// <summary>Updates all register displays from a CPU snapshot.</summary>
    public void Update(CpuRegisters regs)
    {
        AReg   = $"A:   {regs.A:X4}";
        XReg   = $"X:   {regs.X:X4}";
        YReg   = $"Y:   {regs.Y:X4}";
        SpReg  = $"SP:  {regs.SP:X4}";
        DpReg  = $"DP:  {regs.DP:X4}";
        PcReg  = $"PC:  {regs.PBR:X2}:{regs.PC:X4}";
        DbrReg = $"DBR: {regs.DBR:X2}";
        PReg   = $"P:   {FlagsString(regs)}";
        EFlag  = $"E:   {(regs.EmulationMode ? "1 (6502)" : "0 (native)")}";
    }

    private static string FlagsString(CpuRegisters r) =>
        $"{(r.FlagN ? 'N' : 'n')}" +
        $"{(r.FlagV ? 'V' : 'v')}" +
        $"{(r.FlagM ? 'M' : 'm')}" +
        $"{(r.FlagX ? 'X' : 'x')}" +
        $"{(r.FlagD ? 'D' : 'd')}" +
        $"{(r.FlagI ? 'I' : 'i')}" +
        $"{(r.FlagZ ? 'Z' : 'z')}" +
        $"{(r.FlagC ? 'C' : 'c')}";
}

/// <summary>
/// ViewModel for the log/diagnostics panel.
/// Subscribes to DiagnosticLogSink and exposes a bounded observable list.
/// </summary>
public sealed class LogViewModel : ReactiveObject
{
    public ObservableCollection<LogEntryViewModel> Entries { get; } = new();
    private const int MaxVisibleEntries = 200;

    public LogViewModel(DiagnosticLogSink logSink)
    {
        logSink.LogAdded += (_, entry) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Entries.Add(new LogEntryViewModel(entry));
                while (Entries.Count > MaxVisibleEntries)
                    Entries.RemoveAt(0);
            });
        };
    }

    public void Clear() => Entries.Clear();
}

/// <summary>
/// Display model for a single log entry in the log panel.
/// </summary>
public sealed class LogEntryViewModel
{
    public string Timestamp { get; }
    public string Level     { get; }
    public string Category  { get; }
    public string Message   { get; }
    public string LevelColor { get; }

    public LogEntryViewModel(LogEntry entry)
    {
        Timestamp = entry.Timestamp.ToString("HH:mm:ss.fff");
        Level     = entry.LevelIndicator;
        Category  = entry.ShortCategory;
        Message   = entry.Message;
        LevelColor = entry.Level switch
        {
            Microsoft.Extensions.Logging.LogLevel.Warning  => "#FFB347",
            Microsoft.Extensions.Logging.LogLevel.Error    => "#E94560",
            Microsoft.Extensions.Logging.LogLevel.Critical => "#FF0040",
            Microsoft.Extensions.Logging.LogLevel.Debug    => "#6080A0",
            _                                               => "#9090A8"
        };
    }
}
