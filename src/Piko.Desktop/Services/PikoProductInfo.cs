using System.Reflection;

namespace Piko.Desktop.Services;

internal static class PikoProductInfo
{
    internal static string Version
    {
        get
        {
            var informational = typeof(PikoProductInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return string.IsNullOrWhiteSpace(informational)
                ? "0.0.0"
                : informational.Split('+', 2)[0];
        }
    }
}
