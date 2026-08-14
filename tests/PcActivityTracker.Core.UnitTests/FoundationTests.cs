using Xunit;

namespace PcActivityTracker.Core.UnitTests;

public sealed class FoundationTests
{
    [Fact]
    public void CoreAssemblyHasExpectedName()
    {
        var assemblyName = typeof(Core.AssemblyMarker).Assembly.GetName().Name;

        Assert.Equal("PcActivityTracker.Core", assemblyName);
    }
}
