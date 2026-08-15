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

    private static DocumentResolverRegistry Create(IDocumentContextResolver? resolver = null, TimeSpan? timeout = null, DocumentResolverMetrics? metrics = null) =>
        new(resolver is null ? [] : [resolver], new() { Timeout = timeout ?? TimeSpan.FromSeconds(1) }, metrics);
    private sealed class FakeResolver(DocumentResolutionResult result) : IDocumentContextResolver
    {
        public int Calls { get; private set; }
        public string Id => "fake";
        public IReadOnlySet<string> SupportedProcessNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WINWORD" };
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
}
