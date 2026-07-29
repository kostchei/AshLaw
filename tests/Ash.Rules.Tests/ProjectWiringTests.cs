namespace Ash.Rules.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void RulesAssemblyIsReachable()
    {
        Assert.Equal("Ash.Rules", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
