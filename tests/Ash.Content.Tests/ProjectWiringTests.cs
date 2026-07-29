namespace Ash.Content.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void ContentAssemblyIsReachable()
    {
        Assert.Equal("Ash.Content", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
