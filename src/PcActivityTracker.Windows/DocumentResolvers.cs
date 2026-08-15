using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using PcActivityTracker.Core.Domain;

namespace PcActivityTracker.Windows;

public sealed record DocumentResolutionRequest(int ProcessId, string ProcessName, nint WindowHandle);

public interface IDocumentContextResolver
{
    string Id { get; }
    IReadOnlySet<string> SupportedProcessNames { get; }
    ValueTask<DocumentResolutionResult> ResolveAsync(DocumentResolutionRequest request, CancellationToken cancellationToken);
}

public sealed record DocumentResolverOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(750);
}

public sealed class DocumentResolverMetrics
{
    private long attempts, fullPath, fileNameOnly, unresolved, timeouts, errors, totalLatencyTicks;
    public long Attempts => Interlocked.Read(ref attempts);
    public long FullPath => Interlocked.Read(ref fullPath);
    public long FileNameOnly => Interlocked.Read(ref fileNameOnly);
    public long Unresolved => Interlocked.Read(ref unresolved);
    public long Timeouts => Interlocked.Read(ref timeouts);
    public long Errors => Interlocked.Read(ref errors);
    public TimeSpan TotalLatency => TimeSpan.FromTicks(Interlocked.Read(ref totalLatencyTicks));
    internal void Record(DocumentResolutionResult result, TimeSpan latency)
    {
        Interlocked.Increment(ref attempts); Interlocked.Add(ref totalLatencyTicks, latency.Ticks);
        if (result.Precision == DocumentResolutionPrecision.FullPath) Interlocked.Increment(ref fullPath);
        else if (result.Precision == DocumentResolutionPrecision.FileNameOnly) Interlocked.Increment(ref fileNameOnly);
        else Interlocked.Increment(ref unresolved);
        if (result.Failure == DocumentResolutionFailure.TimedOut) Interlocked.Increment(ref timeouts);
        if (result.Failure is DocumentResolutionFailure.AccessDenied or DocumentResolutionFailure.ResolverError or
            DocumentResolutionFailure.ApplicationTerminated or DocumentResolutionFailure.ApiUnavailable) Interlocked.Increment(ref errors);
    }
}

public sealed class DocumentResolverRegistry
{
    private readonly IReadOnlyDictionary<string, IDocumentContextResolver> resolvers;
    private readonly TimeSpan timeout;
    private readonly TimeProvider time;
    private readonly DocumentResolverMetrics metrics;
    private readonly SemaphoreSlim isolationGate = new(1, 1);

    public DocumentResolverRegistry(IEnumerable<IDocumentContextResolver> resolvers, DocumentResolverOptions? options = null,
        DocumentResolverMetrics? metrics = null, TimeProvider? timeProvider = null)
    {
        var entries = new Dictionary<string, IDocumentContextResolver>(StringComparer.OrdinalIgnoreCase);
        foreach (var resolver in resolvers)
            foreach (var processName in resolver.SupportedProcessNames)
                if (!entries.TryAdd(Normalize(processName), resolver))
                    throw new ArgumentException($"Il processo '{processName}' ha più resolver.", nameof(resolvers));
        this.resolvers = entries; timeout = (options ?? new()).Timeout;
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
        this.metrics = metrics ?? new(); time = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<DocumentResolutionResult> ResolveAsync(DocumentResolutionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProcessName);
        if (!resolvers.TryGetValue(Normalize(request.ProcessName), out var resolver))
            return DocumentResolutionResult.Unresolved(DocumentResolutionFailure.UnsupportedApplication);

        var started = time.GetTimestamp();
        DocumentResolutionResult result;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await isolationGate.WaitAsync(timeoutSource.Token);
            var work = Task.Run(async () =>
            {
                try { return await resolver.ResolveAsync(request, timeoutSource.Token); }
                finally { isolationGate.Release(); }
            }, CancellationToken.None);
            result = await work.WaitAsync(timeoutSource.Token);
            result ??= DocumentResolutionResult.Unresolved(DocumentResolutionFailure.ResolverError, resolver.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            result = DocumentResolutionResult.Unresolved(DocumentResolutionFailure.TimedOut, resolver.Id);
        }
        catch (UnauthorizedAccessException)
        {
            result = DocumentResolutionResult.Unresolved(DocumentResolutionFailure.AccessDenied, resolver.Id);
        }
        catch (ArgumentException) when (!IsProcessAlive(request.ProcessId))
        {
            result = DocumentResolutionResult.Unresolved(DocumentResolutionFailure.ApplicationTerminated, resolver.Id);
        }
        catch (InvalidOperationException) when (!IsProcessAlive(request.ProcessId))
        {
            result = DocumentResolutionResult.Unresolved(DocumentResolutionFailure.ApplicationTerminated, resolver.Id);
        }
        catch (COMException exception) when (exception.HResult == unchecked((int)0x80070005))
        {
            result = DocumentResolutionResult.Unresolved(DocumentResolutionFailure.AccessDenied, resolver.Id);
        }
        catch (COMException)
        {
            result = DocumentResolutionResult.Unresolved(DocumentResolutionFailure.ApiUnavailable, resolver.Id);
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            result = DocumentResolutionResult.Unresolved(DocumentResolutionFailure.ResolverError, resolver.Id);
        }
        metrics.Record(result, time.GetElapsedTime(started));
        return result;
    }

    private static bool IsProcessAlive(int processId)
    {
        try { return !Process.GetProcessById(processId).HasExited; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return true; }
    }
    private static string Normalize(string value) => Path.GetFileNameWithoutExtension(value.Trim());
}

public interface IWordDocumentFacade
{
    WordDocumentSnapshot? TryReadActiveDocument(nint foregroundWindow);
}

public sealed record WordDocumentSnapshot(string? FullName, string? Name);

public sealed class WordDocumentContextResolver(IWordDocumentFacade facade) : IDocumentContextResolver
{
    public string Id => "microsoft-word-com-v1";
    public IReadOnlySet<string> SupportedProcessNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WINWORD" };
    public ValueTask<DocumentResolutionResult> ResolveAsync(DocumentResolutionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = facade.TryReadActiveDocument(request.WindowHandle);
        cancellationToken.ThrowIfCancellationRequested();
        if (document?.FullName is { } fullName && IsFullyQualifiedWindowsPath(fullName))
            return ValueTask.FromResult(DocumentResolutionResult.FullPath(fullName, DocumentProvenance.DirectlyObserved, Id));
        if (document?.Name is { } name && !string.IsNullOrWhiteSpace(name))
            return ValueTask.FromResult(DocumentResolutionResult.FileNameOnly(Path.GetFileName(name), DocumentProvenance.DirectlyObserved, Id));
        return ValueTask.FromResult(DocumentResolutionResult.Unresolved(DocumentResolutionFailure.None, Id));
    }
    private static bool IsFullyQualifiedWindowsPath(string value) =>
        value.StartsWith(@"\\", StringComparison.Ordinal) || value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/';
}

/// <summary>Adapter minimale al Word Object Model: legge solo Hwnd, FullName e Name del documento attivo.</summary>
public sealed partial class WordComDocumentFacade : IWordDocumentFacade
{
    public WordDocumentSnapshot? TryReadActiveDocument(nint foregroundWindow)
    {
        object? application = null, window = null, document = null;
        try
        {
            ThrowIfFailed(CLSIDFromProgID("Word.Application", out var classId));
            ThrowIfFailed(GetActiveObject(in classId, 0, out application));
            if (application is null) return null;
            window = GetProperty(application, "ActiveWindow");
            if (window is null || Convert.ToInt64(GetProperty(window, "Hwnd")) != foregroundWindow.ToInt64()) return null;
            document = GetProperty(application, "ActiveDocument");
            if (document is null) return null;
            return new(GetProperty(document, "FullName") as string, GetProperty(document, "Name") as string);
        }
        finally
        {
            Release(document); Release(window); Release(application);
        }
    }

    private static object? GetProperty(object instance, string name)
    {
        try { return instance.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, instance, null); }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is { } inner)
        { ExceptionDispatchInfo.Capture(inner).Throw(); throw; }
    }
    private static void ThrowIfFailed(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) _ = Marshal.FinalReleaseComObject(value); }
    [LibraryImport("ole32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int CLSIDFromProgID(string progId, out Guid classId);
    [LibraryImport("oleaut32.dll")] private static partial int GetActiveObject(in Guid classId, nint reserved, [MarshalAs(UnmanagedType.Interface)] out object? instance);
}
