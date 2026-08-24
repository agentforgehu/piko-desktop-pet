namespace Piko.Memory.Security;

public interface IMemoryProtector : IDisposable
{
    string Protect(string plaintext, string purpose);
    string Unprotect(string protectedText, string purpose);
}
