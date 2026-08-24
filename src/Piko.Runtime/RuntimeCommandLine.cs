namespace Piko.Runtime;

internal sealed record RuntimeCommandLine(
    bool SmokeTest,
    bool StabilityTest,
    int StabilityDurationSeconds,
    bool HealthCheck,
    bool Stop,
    string? DataDirectory,
    string? PipeName)
{
    public static RuntimeCommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var smokeTest = false;
        var stabilityTest = false;
        var stabilityDurationSeconds = 1800;
        var stabilityDurationSpecified = false;
        var healthCheck = false;
        var stop = false;
        string? dataDirectory = null;
        string? pipeName = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--smoke-test", StringComparison.OrdinalIgnoreCase))
            {
                smokeTest = true;
                continue;
            }

            if (string.Equals(argument, "--health-check", StringComparison.OrdinalIgnoreCase))
            {
                healthCheck = true;
                continue;
            }

            if (string.Equals(argument, "--stability-test", StringComparison.OrdinalIgnoreCase))
            {
                stabilityTest = true;
                continue;
            }

            if (string.Equals(argument, "--duration-seconds", StringComparison.OrdinalIgnoreCase))
            {
                stabilityDurationSpecified = true;
                var rawDuration = ReadValue(args, ref index, argument);
                if (!int.TryParse(rawDuration, out stabilityDurationSeconds) ||
                    stabilityDurationSeconds is < 10 or > 86_400)
                {
                    throw new ArgumentException("Stability duration must be between 10 and 86400 seconds.");
                }
                continue;
            }

            if (string.Equals(argument, "--stop", StringComparison.OrdinalIgnoreCase))
            {
                stop = true;
                continue;
            }

            if (string.Equals(argument, "--data-dir", StringComparison.OrdinalIgnoreCase))
            {
                dataDirectory = ReadValue(args, ref index, argument);
                continue;
            }

            if (string.Equals(argument, "--pipe-name", StringComparison.OrdinalIgnoreCase))
            {
                pipeName = ReadValue(args, ref index, argument);
                continue;
            }

            throw new ArgumentException($"Unknown runtime argument: {argument}");
        }

        if ((smokeTest ? 1 : 0) + (stabilityTest ? 1 : 0) + (healthCheck ? 1 : 0) + (stop ? 1 : 0) > 1)
        {
            throw new ArgumentException("Smoke test, stability test, health check, and stop cannot be combined.");
        }

        if (!stabilityTest && stabilityDurationSpecified)
        {
            throw new ArgumentException("--duration-seconds requires --stability-test.");
        }

        return new RuntimeCommandLine(
            smokeTest,
            stabilityTest,
            stabilityDurationSeconds,
            healthCheck,
            stop,
            dataDirectory,
            pipeName);
    }

    private static string ReadValue(string[] args, ref int index, string argument)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"Missing value for {argument}.");
        }

        index++;
        return args[index];
    }
}
