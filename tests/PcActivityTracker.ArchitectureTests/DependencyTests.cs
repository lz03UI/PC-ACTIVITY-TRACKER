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

    [Fact]
    public void DesktopApplicationLoadsXamlControlsResources()
    {
        var appXaml = System.Xml.Linq.XDocument.Load(FindRepositoryFile(
            "src",
            "PcActivityTracker.Desktop",
            "App.xaml"));
        System.Xml.Linq.XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XNamespace controls = "using:Microsoft.UI.Xaml.Controls";

        var mergedDictionaries = appXaml.Root?
            .Element(presentation + "Application.Resources")?
            .Element(presentation + "ResourceDictionary")?
            .Element(presentation + "ResourceDictionary.MergedDictionaries");

        Assert.NotNull(mergedDictionaries);
        Assert.Contains(
            mergedDictionaries.Elements(),
            element => element.Name == controls + "XamlControlsResources");
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PcActivityTracker.sln")))
                return Path.Combine(directory.FullName, Path.Combine(segments));
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string FormatFailure(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
}
