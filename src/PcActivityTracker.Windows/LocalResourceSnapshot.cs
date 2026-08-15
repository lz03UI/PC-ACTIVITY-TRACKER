using System.Diagnostics;

namespace PcActivityTracker.Windows;

/// <summary>Snapshot esclusivamente locale e privo di metadata di attività.</summary>
public sealed record LocalResourceSnapshot(TimeSpan TotalProcessorTime, long WorkingSetBytes, long DatabaseBytes)
{
    public static LocalResourceSnapshot Capture(string databasePath)
    {
        using var process = Process.GetCurrentProcess();
        var databaseBytes = File.Exists(databasePath) ? new FileInfo(databasePath).Length : 0;
        return new(process.TotalProcessorTime, process.WorkingSet64, databaseBytes);
    }
}
