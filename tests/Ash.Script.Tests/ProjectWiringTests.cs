namespace Ash.Script.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void ScriptAssemblyIsReachable()
    {
        Assert.Equal("Ash.Script", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
