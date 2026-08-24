using Xunit;

namespace Piko.Runtime.Tests;

public sealed class RuntimeCommandLineTests
{
    [Fact]
    public void StabilityModeHasBoundedDurationAndIsIsolatedFromOtherModes()
    {
        var options = RuntimeCommandLine.Parse(
            ["--stability-test", "--duration-seconds", "60", "--data-dir", "data", "--pipe-name", "pipe"]);

        Assert.True(options.StabilityTest);
        Assert.Equal(60, options.StabilityDurationSeconds);
        Assert.Equal("data", options.DataDirectory);
        Assert.Equal("pipe", options.PipeName);
        Assert.Throws<ArgumentException>(() => RuntimeCommandLine.Parse(
            ["--stability-test", "--duration-seconds", "9"]));
        Assert.Throws<ArgumentException>(() => RuntimeCommandLine.Parse(
            ["--stability-test", "--health-check"]));
    }

    [Fact]
    public void DurationCannotModifyNormalRuntimeMode()
    {
        Assert.Throws<ArgumentException>(() => RuntimeCommandLine.Parse(["--duration-seconds", "60"]));
    }
}
