using System.Reflection;

namespace Piko.Runtime;

public static class RuntimeProductInfo
{
    public static string Version
    {
        get
        {
            var informational = typeof(RuntimeProductInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return string.IsNullOrWhiteSpace(informational)
                ? "0.0.0"
                : informational.Split('+', 2)[0];
        }
    }
}
