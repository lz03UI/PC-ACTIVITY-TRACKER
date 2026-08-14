using NetArchTest.Rules;
using Xunit;

namespace PcActivityTracker.ArchitectureTests;

public sealed class DependencyTests
{
    private static readonly System.Reflection.Assembly CoreAssembly =
        typeof(Core.AssemblyMarker).Assembly;

    [Theory]
    [InlineData("Microsoft.Data.Sqlite")]
    [InlineData("Microsoft.UI.Xaml")]
    [InlineData("PcActivityTracker.Data")]
    [InlineData("PcActivityTracker.Reporting")]
    [InlineData("PcActivityTracker.BrowserIntegration")]
    [InlineData("PcActivityTracker.Windows")]
    public void CoreDoesNotDependOnOuterLayers(string forbiddenNamespace)
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn(forbiddenNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure(result));
    }

    [Theory]
    [InlineData(typeof(Reporting.AssemblyMarker))]
    [InlineData(typeof(BrowserIntegration.AssemblyMarker))]
    public void CrossPlatformFeaturesDoNotDependOnPlatformAdapters(Type assemblyMarker)
    {
        var result = Types.InAssembly(assemblyMarker.Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.UI.Xaml",
                "PcActivityTracker.Data",
                "PcActivityTracker.Windows")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure(result));
    }

    private static string FormatFailure(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
}
