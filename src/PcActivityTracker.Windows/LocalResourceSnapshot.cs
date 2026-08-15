using System.Diagnostics;

namespace PcActivityTracker.Windows;

/// <summary>Snapshot esclusivamente locale e privo di metadata di attività.</summary>
public sealed record LocalResourceSnapshot(TimeSpan TotalProcessorTime, long WorkingSetBytes, long DatabaseBytes)
{
    public static LocalResourceSnapshot Capture(string databasePath)
    {
        using var process = Process.GetCurrentProcess();
        var databaseBytes = Size(databasePath) + Size(databasePath + "-wal");
        return new(process.TotalProcessorTime, process.WorkingSet64, databaseBytes);
    }
    private static long Size(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
}
