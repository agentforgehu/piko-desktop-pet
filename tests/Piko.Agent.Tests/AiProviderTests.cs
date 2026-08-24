using Piko.Agent.Models;

namespace Piko.Agent.Tests;

public sealed class AiProviderTests
{
    [Fact]
    public async Task DisabledProvider_FailsClosedWithoutNetwork()
    {
        IAiProvider provider = new DisabledAiProvider();

        var response = await provider.CompleteAsync(
            new AiModelRequest("system", "sanitized", "help"),
            CancellationToken.None);

        Assert.False(response.Available);
        Assert.Equal("disabled", response.Provider);
        Assert.Equal(string.Empty, response.Text);
    }
}
