using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Piko.Update;

public sealed record UpdatePackageVerification(
    bool IsTrusted,
    string Reason,
    string? SignerThumbprint = null);

public static class UpdatePackageVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static UpdatePackageVerification Verify(
        string path,
        UpdateInstaller installer,
        IReadOnlyCollection<string> trustedSignerThumbprints)
    {
        if (!File.Exists(path))
        {
            return new UpdatePackageVerification(false, "installer_missing");
        }

        using (var stream = File.OpenRead(path))
        {
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(hash, installer.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new UpdatePackageVerification(false, "sha256_mismatch");
            }
        }

        if (!installer.AuthenticodeRequired)
        {
            return new UpdatePackageVerification(false, "authenticode_not_required_by_manifest");
        }

        if (trustedSignerThumbprints.Count == 0)
        {
            return new UpdatePackageVerification(false, "no_trusted_signer_configured");
        }

        if (!VerifyAuthenticodeTrust(path))
        {
            return new UpdatePackageVerification(false, "authenticode_invalid");
        }

        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            var thumbprint = NormalizeThumbprint(certificate.Thumbprint);
            var trusted = trustedSignerThumbprints
                .Select(NormalizeThumbprint)
                .Contains(thumbprint, StringComparer.OrdinalIgnoreCase);
            return trusted
                ? new UpdatePackageVerification(true, "trusted", thumbprint)
                : new UpdatePackageVerification(false, "signer_not_pinned", thumbprint);
        }
        catch (CryptographicException)
        {
            return new UpdatePackageVerification(false, "signer_unavailable");
        }
    }

    private static string NormalizeThumbprint(string value) =>
        new(value.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool VerifyAuthenticodeTrust(string path)
    {
        var pathPointer = Marshal.StringToCoTaskMemUni(path);
        var fileInfoPointer = nint.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                StructureSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = pathPointer
            };
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

            var data = new WinTrustData
            {
                StructureSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,
                RevocationChecks = 1,
                UnionChoice = 1,
                FileInfo = fileInfoPointer,
                ProviderFlags = 0x00000080
            };
            var action = GenericVerifyV2;
            return WinVerifyTrust(nint.Zero, ref action, ref data) == 0;
        }
        finally
        {
            if (fileInfoPointer != nint.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }

            Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        nint window,
        ref Guid actionId,
        ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructureSize;
        public nint FilePath;
        public nint FileHandle;
        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructureSize;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint FileInfo;
        public uint StateAction;
        public nint StateData;
        public nint UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }
}
