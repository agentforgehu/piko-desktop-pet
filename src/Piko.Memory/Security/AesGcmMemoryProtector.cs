using System.Security.Cryptography;
using System.Text;

namespace Piko.Memory.Security;

public sealed class AesGcmMemoryProtector : IMemoryProtector
{
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private byte[]? _key;

    public AesGcmMemoryProtector(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("Memory encryption key must contain 32 bytes.", nameof(key));
        }

        _key = key.ToArray();
    }

    public string Protect(string plaintext, string purpose)
    {
        ObjectDisposedException.ThrowIf(_key is null, this);
        ArgumentNullException.ThrowIfNull(plaintext);
        ValidatePurpose(purpose);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var purposeBytes = Encoding.UTF8.GetBytes(purpose);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(_key!, TagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, purposeBytes);
            var result = new byte[1 + NonceSize + TagSize + ciphertext.Length];
            result[0] = FormatVersion;
            nonce.CopyTo(result, 1);
            tag.CopyTo(result, 1 + NonceSize);
            ciphertext.CopyTo(result, 1 + NonceSize + TagSize);
            return Convert.ToBase64String(result);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(purposeBytes);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public string Unprotect(string protectedText, string purpose)
    {
        ObjectDisposedException.ThrowIf(_key is null, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedText);
        ValidatePurpose(purpose);
        var data = Convert.FromBase64String(protectedText);
        if (data.Length < 1 + NonceSize + TagSize || data[0] != FormatVersion)
        {
            throw new CryptographicException("Unsupported encrypted memory format.");
        }

        var purposeBytes = Encoding.UTF8.GetBytes(purpose);
        var plaintext = new byte[data.Length - 1 - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(_key!, TagSize);
            aes.Decrypt(
                data.AsSpan(1, NonceSize),
                data.AsSpan(1 + NonceSize + TagSize),
                data.AsSpan(1 + NonceSize, TagSize),
                plaintext,
                purposeBytes);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
            CryptographicOperations.ZeroMemory(purposeBytes);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Dispose()
    {
        var key = Interlocked.Exchange(ref _key, null);
        if (key is not null)
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static void ValidatePurpose(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose) || purpose.Length > 256)
        {
            throw new ArgumentException("Encryption purpose is invalid.", nameof(purpose));
        }
    }
}
