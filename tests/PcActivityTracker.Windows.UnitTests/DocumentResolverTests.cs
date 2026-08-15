using PcActivityTracker.Core.Domain;
using PcActivityTracker.Windows;
using Xunit;

namespace PcActivityTracker.Windows.UnitTests;

public sealed class DocumentResolverTests
{
    private static readonly DocumentResolutionRequest Request = new(Environment.ProcessId, "WINWORD.EXE", (nint)42);

    [Fact] public async Task DispatcherSelectsSupportedResolverCaseInsensitively() { var fake = new FakeResolver(DocumentResolutionResult.FileNameOnly("plan.docx", DocumentProvenance.DirectlyObserved, "fake")); var registry = Create(fake); var result = await registry.ResolveAsync(Request); Assert.Equal(DocumentResolutionPrecision.FileNameOnly, result.Precision); Assert.Equal(1, fake.Calls); }
    [Fact] public async Task UnsupportedApplicationIsExplicitlyUnresolved() { var result = await Create().ResolveAsync(Request with { ProcessName = "unknown.exe" }); Assert.Equal(DocumentResolutionFailure.UnsupportedApplication, result.Failure); Assert.Equal(DocumentResolutionPrecision.Unresolved, result.Precision); }
    [Fact] public async Task FullPathIsPreservedWithoutLoggingOrInference() { var expected = @"C:\Work\plan.docx"; var result = await Create(new FakeResolver(DocumentResolutionResult.FullPath(expected, DocumentProvenance.DirectlyObserved, "fake"))).ResolveAsync(Request); Assert.Equal(expected, result.Value); Assert.Equal(DocumentProvenance.DirectlyObserved, result.Provenance); }
    [Fact] public async Task ResolverCanReturnUnresolvedWithoutFailure() { var result = await Create(new FakeResolver(DocumentResolutionResult.Unresolved(resolverId: "fake"))).ResolveAsync(Request); Assert.Equal(DocumentResolutionFailure.None, result.Failure); Assert.Null(result.Value); }
    [Fact] public async Task AccessDeniedBecomesPrivacySafeFailure() { var result = await Create(new ThrowingResolver(new UnauthorizedAccessException())).ResolveAsync(Request); Assert.Equal(DocumentResolutionFailure.AccessDenied, result.Failure); }
    [Fact] public async Task ResolverExceptionIsContained() { var result = await Create(new ThrowingResolver(new NotSupportedException())).ResolveAsync(Request); Assert.Equal(DocumentResolutionFailure.ResolverError, result.Failure); }
    [Fact] public async Task TerminatedApplicationIsDistinguished() { var result = await Create(new ThrowingResolver(new InvalidOperationException())).ResolveAsync(Request with { ProcessId = int.MaxValue }); Assert.Equal(DocumentResolutionFailure.ApplicationTerminated, result.Failure); }
    [Fact] public async Task TimeoutDoesNotWaitForBrokenResolver() { var metrics = new DocumentResolverMetrics(); var registry = Create(new SlowResolver(), TimeSpan.FromMilliseconds(30), metrics); var result = await registry.ResolveAsync(Request); Assert.Equal(DocumentResolutionFailure.TimedOut, result.Failure); Assert.Equal(1, metrics.Timeouts); Assert.Equal(1, metrics.Unresolved); }
    [Fact] public async Task CallerCancellationIsNotConvertedToUnresolved() { using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Create(new SlowResolver()).ResolveAsync(Request, cancellation.Token)); }
    [Fact] public async Task MetricsContainCountsAndLatencyButNoDocumentMetadata() { var metrics = new DocumentResolverMetrics(); await Create(new FakeResolver(DocumentResolutionResult.FullPath(@"C:\Sensitive\plan.docx", DocumentProvenance.DirectlyObserved, "fake")), metrics: metrics).ResolveAsync(Request); Assert.Equal(1, metrics.Attempts); Assert.Equal(1, metrics.FullPath); Assert.DoesNotContain(typeof(DocumentResolverMetrics).GetProperties(), property => property.PropertyType == typeof(string)); }
    [Fact] public async Task WordUsesOfficialFullNameAndFallsBackOnlyToName() { var full = new WordDocumentContextResolver(new FakeWordFacade(new(@"C:\Work\plan.docx", "plan.docx"))); var fallback = new WordDocumentContextResolver(new FakeWordFacade(new(null, "draft.docx"))); Assert.Equal(DocumentResolutionPrecision.FullPath, (await full.ResolveAsync(Request, default)).Precision); var result = await fallback.ResolveAsync(Request, default); Assert.Equal(DocumentResolutionPrecision.FileNameOnly, result.Precision); Assert.Equal("draft.docx", result.Value); }
    [Fact] public async Task WordWithoutActiveDocumentIsUnresolvedAndNeverReadsWindowTitle() { var resolver = new WordDocumentContextResolver(new FakeWordFacade(null)); var result = await resolver.ResolveAsync(Request, default); Assert.Equal(DocumentResolutionPrecision.Unresolved, result.Precision); Assert.DoesNotContain(typeof(DocumentResolutionRequest).GetProperties(), property => property.Name.Contains("Title", StringComparison.Ordinal)); }
    [Fact]
    public async Task SameResolverExecutesAtMostOnceConcurrently()
    {
        var resolver = new ControlledResolver("shared", new HashSet<string> { "WINWORD" }); var registry = Create(resolver);
        var first = registry.ResolveAsync(Request).AsTask(); await resolver.Started.Task;
        var second = registry.ResolveAsync(Request).AsTask(); await Task.Delay(30);
        Assert.Equal(1, resolver.Calls); Assert.Equal(1, resolver.MaximumConcurrency);
        resolver.Release.TrySetResult(); await Task.WhenAll(first, second);
        Assert.Equal(1, resolver.MaximumConcurrency);
    }
    [Fact]
    public async Task BlockedResolverDoesNotBlockDifferentResolver()
    {
        var blocked = new ControlledResolver("word", new HashSet<string> { "WINWORD" }); var healthy = new FakeResolver(DocumentResolutionResult.FileNameOnly("book.xlsx", DocumentProvenance.DirectlyObserved, "excel"), "EXCEL");
        var registry = new DocumentResolverRegistry([blocked, healthy], new() { Timeout = TimeSpan.FromSeconds(1) });
        var first = registry.ResolveAsync(Request).AsTask(); await blocked.Started.Task;
        var result = await registry.ResolveAsync(Request with { ProcessName = "EXCEL.EXE" });
        Assert.Equal(DocumentResolutionPrecision.FileNameOnly, result.Precision);
        blocked.Release.TrySetResult(); await first;
    }
    [Fact]
    public async Task TimeoutOfOneResolverDoesNotChangeOtherResolverResult()
    {
        var blocked = new ControlledResolver("word", new HashSet<string> { "WINWORD" }, ignoreCancellation: true); var expected = DocumentResolutionResult.FullPath(@"C:\Work\book.xlsx", DocumentProvenance.DirectlyObserved, "excel");
        var registry = new DocumentResolverRegistry([blocked, new FakeResolver(expected, "EXCEL")], new() { Timeout = TimeSpan.FromMilliseconds(40) });
        var timedOut = await registry.ResolveAsync(Request); var healthy = await registry.ResolveAsync(Request with { ProcessName = "EXCEL" });
        Assert.Equal(DocumentResolutionFailure.TimedOut, timedOut.Failure); Assert.Equal(expected, healthy);
        blocked.Release.TrySetResult();
    }
    [Fact]
    public async Task CancellationWhileWaitingForResolverGateIsPropagated()
    {
        var resolver = new ControlledResolver("word", new HashSet<string> { "WINWORD" }, ignoreCancellation: true); var registry = Create(resolver, TimeSpan.FromSeconds(2));
        var first = registry.ResolveAsync(Request).AsTask(); await resolver.Started.Task;
        using var cancellation = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.ResolveAsync(Request, cancellation.Token).AsTask());
        resolver.Release.TrySetResult(); await first;
    }
    [Fact]
    public async Task QueuedCallsDoNotCreateUnboundedResolverWork()
    {
        var resolver = new ControlledResolver("word", new HashSet<string> { "WINWORD" }, ignoreCancellation: true); var registry = Create(resolver, TimeSpan.FromMilliseconds(40));
        var calls = Enumerable.Range(0, 32).Select(_ => registry.ResolveAsync(Request).AsTask()).ToArray();
        await Task.WhenAll(calls); Assert.Equal(1, resolver.Calls); Assert.All(calls, call => Assert.Equal(DocumentResolutionFailure.TimedOut, call.Result.Failure));
        resolver.Release.TrySetResult();
    }
    [Fact]
    public async Task ExcelUsesOfficialFullNameAndFallsBackOnlyToWorkbookName()
    {
        var full = new ExcelDocumentContextResolver(new FakeExcelFacade(new(@"C:\Work\book.xlsx", "book.xlsx")));
        var name = new ExcelDocumentContextResolver(new FakeExcelFacade(new(null, "Book1")));
        Assert.Equal(DocumentResolutionPrecision.FullPath, (await full.ResolveAsync(Request with { ProcessName = "EXCEL" }, default)).Precision);
        Assert.Equal("Book1", (await name.ResolveAsync(Request with { ProcessName = "EXCEL" }, default)).Value);
    }
    [Fact]
    public async Task ExcelWithoutWorkbookOrWithAmbiguousInstanceIsUnresolved()
    {
        var resolver = new ExcelDocumentContextResolver(new FakeExcelFacade(null));
        var result = await resolver.ResolveAsync(Request with { ProcessName = "EXCEL" }, default);
        Assert.Equal(DocumentResolutionPrecision.Unresolved, result.Precision); Assert.Null(result.Value);
    }
    [Fact]
    public async Task ExcelFacadeReceivesForegroundWindowAndProcessForMismatchValidation()
    {
        var facade = new CapturingExcelFacade(); var resolver = new ExcelDocumentContextResolver(facade);
        var request = Request with { ProcessName = "EXCEL", ProcessId = 123, WindowHandle = (nint)456 };
        Assert.Equal(DocumentResolutionPrecision.Unresolved, (await resolver.ResolveAsync(request, default)).Precision);
        Assert.Equal(request, facade.Request);
    }
    [Fact]
    public void ExcelContractsContainNeitherWindowTitleNorDocumentContent()
    {
        Assert.DoesNotContain(typeof(DocumentResolutionRequest).GetProperties(), p => p.Name.Contains("Title", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(ExcelDocumentSnapshot).GetProperties(), p => p.Name.Contains("Content", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["FullName", "Name"], typeof(ExcelDocumentSnapshot).GetProperties().Select(p => p.Name));
    }
    [Fact]
    public async Task ExcelCancellationBeforeFacadeCallIsCorrect()
    {
        var facade = new CapturingExcelFacade(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ExcelDocumentContextResolver(facade).ResolveAsync(Request, cancellation.Token).AsTask());
        Assert.Null(facade.Request);
    }

    private static DocumentResolverRegistry Create(IDocumentContextResolver? resolver = null, TimeSpan? timeout = null, DocumentResolverMetrics? metrics = null) =>
        new(resolver is null ? [] : [resolver], new() { Timeout = timeout ?? TimeSpan.FromSeconds(1) }, metrics);
    private sealed class FakeResolver(DocumentResolutionResult result, string processName = "WINWORD") : IDocumentContextResolver
    {
        public int Calls { get; private set; }
        public string Id => "fake";
        public IReadOnlySet<string> SupportedProcessNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { processName };
        public ValueTask<DocumentResolutionResult> ResolveAsync(DocumentResolutionRequest request, CancellationToken cancellationToken) { Calls++; return ValueTask.FromResult(result); }
    }
    private sealed class ThrowingResolver(Exception exception) : IDocumentContextResolver
    {
        public string Id => "throwing";
        public IReadOnlySet<string> SupportedProcessNames { get; } = new HashSet<string> { "WINWORD" };
        public ValueTask<DocumentResolutionResult> ResolveAsync(DocumentResolutionRequest request, CancellationToken cancellationToken) => throw exception;
    }
    private sealed class SlowResolver : IDocumentContextResolver
    {
        public string Id => "slow";
        public IReadOnlySet<string> SupportedProcessNames { get; } = new HashSet<string> { "WINWORD" };
        public async ValueTask<DocumentResolutionResult> ResolveAsync(DocumentResolutionRequest request, CancellationToken cancellationToken) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return DocumentResolutionResult.Unresolved(); }
    }
    private sealed class FakeWordFacade(WordDocumentSnapshot? result) : IWordDocumentFacade { public WordDocumentSnapshot? TryReadActiveDocument(nint foregroundWindow) => result; }
    private sealed class FakeExcelFacade(ExcelDocumentSnapshot? result) : IExcelDocumentFacade { public ExcelDocumentSnapshot? TryReadActiveWorkbook(DocumentResolutionRequest request) => result; }
    private sealed class CapturingExcelFacade : IExcelDocumentFacade { public DocumentResolutionRequest? Request { get; private set; } public ExcelDocumentSnapshot? TryReadActiveWorkbook(DocumentResolutionRequest request) { Request = request; return null; } }
    private sealed class ControlledResolver(string id, IReadOnlySet<string> processNames, bool ignoreCancellation = false) : IDocumentContextResolver
    {
        private int concurrency;
        public string Id => id; public IReadOnlySet<string> SupportedProcessNames => processNames;
        public int Calls { get; private set; }
        public int MaximumConcurrency { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<DocumentResolutionResult> ResolveAsync(DocumentResolutionRequest request, CancellationToken cancellationToken)
        {
            Calls++; var current = Interlocked.Increment(ref concurrency); MaximumConcurrency = Math.Max(MaximumConcurrency, current); Started.TrySetResult();
            try { if (ignoreCancellation) await Release.Task; else await Release.Task.WaitAsync(cancellationToken); return DocumentResolutionResult.Unresolved(resolverId: Id); }
            finally { Interlocked.Decrement(ref concurrency); }
        }
    }
}
