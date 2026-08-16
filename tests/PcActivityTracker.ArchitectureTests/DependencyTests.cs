using System.Xml.Linq;
using NetArchTest.Rules;
using Xunit;

namespace PcActivityTracker.ArchitectureTests;

public sealed class DependencyTests
{
    private const string DesktopAppXamlResourceName = "PcActivityTracker.Desktop.App.xaml";

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
        using var appXaml = typeof(DependencyTests).Assembly
            .GetManifestResourceStream(DesktopAppXamlResourceName);

        Assert.NotNull(appXaml);

        var document = XDocument.Load(appXaml);
        var mergedDictionaries = document.Descendants()
            .Single(element => element.Name.LocalName == "ResourceDictionary.MergedDictionaries");

        Assert.Contains(
            mergedDictionaries.Elements(),
            element => element.Name.LocalName == "XamlControlsResources" &&
                       element.Name.NamespaceName == "using:Microsoft.UI.Xaml.Controls");
    }

    private static string FormatFailure(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
}
