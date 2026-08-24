namespace Piko.Update;

public static class TrustedUpdateSigners
{
    // Production 1.0 remains fail-closed until the publisher's Authenticode
    // certificate thumbprint is supplied and this list is rebuilt into Piko.
    public static IReadOnlyCollection<string> Thumbprints { get; } = Array.Empty<string>();
}
