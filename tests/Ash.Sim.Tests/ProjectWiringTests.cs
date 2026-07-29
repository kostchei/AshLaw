namespace Ash.Sim.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void SimulationAssemblyIsReachable()
    {
        Assert.Equal("Ash.Sim", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
