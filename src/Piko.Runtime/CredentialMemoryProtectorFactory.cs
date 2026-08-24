using System.Security.Cryptography;
using Piko.Memory.Security;
using Piko.Runtime.Security;

namespace Piko.Runtime;

public sealed class CredentialMemoryProtectorFactory
{
    private readonly WindowsCredentialStore _credentials;

    public CredentialMemoryProtectorFactory(WindowsCredentialStore? credentials = null)
    {
        _credentials = credentials ?? new WindowsCredentialStore();
    }

    public IMemoryProtector Create()
    {
        var encoded = _credentials.Read(RuntimeSecretNames.MemoryEncryptionKey);
        byte[] key;
        if (string.IsNullOrWhiteSpace(encoded))
        {
            key = RandomNumberGenerator.GetBytes(32);
            try
            {
                _credentials.Save(RuntimeSecretNames.MemoryEncryptionKey, Convert.ToBase64String(key));
            }
            catch
            {
                CryptographicOperations.ZeroMemory(key);
                throw;
            }
        }
        else
        {
            try
            {
                key = Convert.FromBase64String(encoded);
            }
            catch (FormatException exception)
            {
                throw new CryptographicException("Stored memory key is invalid.", exception);
            }
        }

        try
        {
            return new AesGcmMemoryProtector(key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
