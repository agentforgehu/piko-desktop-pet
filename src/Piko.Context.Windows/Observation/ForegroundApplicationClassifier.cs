using Piko.Context.Situations;

namespace Piko.Context.Windows.Observation;

public sealed class ForegroundApplicationClassifier
{
    private static readonly IReadOnlyDictionary<string, ApplicationCategory> Known =
        new Dictionary<string, ApplicationCategory>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = ApplicationCategory.Development,
            ["code-insiders"] = ApplicationCategory.Development,
            ["devenv"] = ApplicationCategory.Development,
            ["rider64"] = ApplicationCategory.Development,
            ["idea64"] = ApplicationCategory.Development,
            ["pycharm64"] = ApplicationCategory.Development,
            ["webstorm64"] = ApplicationCategory.Development,
            ["clion64"] = ApplicationCategory.Development,
            ["androidstudio64"] = ApplicationCategory.Development,
            ["ms-teams"] = ApplicationCategory.Communication,
            ["teams"] = ApplicationCategory.Communication,
            ["zoom"] = ApplicationCategory.Communication,
            ["skype"] = ApplicationCategory.Communication,
            ["slack"] = ApplicationCategory.Communication,
            ["discord"] = ApplicationCategory.Communication,
            ["vlc"] = ApplicationCategory.Media,
            ["wmplayer"] = ApplicationCategory.Media,
            ["spotify"] = ApplicationCategory.Media,
            ["potplayermini64"] = ApplicationCategory.Media,
            ["winword"] = ApplicationCategory.Office,
            ["excel"] = ApplicationCategory.Office,
            ["powerpnt"] = ApplicationCategory.Office,
            ["onenote"] = ApplicationCategory.Office,
            ["outlook"] = ApplicationCategory.Office
        };

    public ApplicationCategory Classify(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return ApplicationCategory.Unknown;
        }

        var normalized = Path.GetFileNameWithoutExtension(processName.Trim());
        return Known.GetValueOrDefault(normalized, ApplicationCategory.General);
    }
}
