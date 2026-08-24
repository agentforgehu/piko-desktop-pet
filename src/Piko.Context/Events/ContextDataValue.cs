using System.Globalization;

namespace Piko.Context.Events;

public sealed record ContextDataValue(string Value, DataSensitivity Sensitivity = DataSensitivity.Low)
{
    public static ContextDataValue From(bool value, DataSensitivity sensitivity = DataSensitivity.Low) =>
        new(value ? "true" : "false", sensitivity);

    public static ContextDataValue From(int value, DataSensitivity sensitivity = DataSensitivity.Low) =>
        new(value.ToString(CultureInfo.InvariantCulture), sensitivity);

    public static ContextDataValue From(double value, DataSensitivity sensitivity = DataSensitivity.Low) =>
        new(value.ToString("R", CultureInfo.InvariantCulture), sensitivity);
}
