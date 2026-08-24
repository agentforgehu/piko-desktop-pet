using Piko.Agent.Models;
using Piko.Runtime.Security;

namespace Piko.Runtime;

public sealed class CredentialAiApiKeySource : IAiApiKeySource
{
    private readonly WindowsCredentialStore _credentials;

    public CredentialAiApiKeySource(WindowsCredentialStore? credentials = null)
    {
        _credentials = credentials ?? new WindowsCredentialStore();
    }

    public ValueTask<string?> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_credentials.Read(RuntimeSecretNames.OpenAiApiKey));
    }
}
