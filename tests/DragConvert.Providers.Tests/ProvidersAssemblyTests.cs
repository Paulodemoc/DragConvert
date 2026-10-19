using DragConvert.Providers;

namespace DragConvert.Providers.Tests;

public class ProvidersAssemblyTests
{
    [Fact]
    public void Assembly_loads() => Assert.Equal("DragConvert.Providers", typeof(ProvidersAssembly).Assembly.GetName().Name);
}
