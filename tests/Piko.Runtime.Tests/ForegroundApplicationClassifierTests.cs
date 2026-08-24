using Piko.Context.Situations;
using Piko.Context.Windows.Observation;

namespace Piko.Runtime.Tests;

public sealed class ForegroundApplicationClassifierTests
{
    private readonly ForegroundApplicationClassifier _classifier = new();

    [Theory]
    [InlineData("Code.exe", ApplicationCategory.Development)]
    [InlineData("devenv", ApplicationCategory.Development)]
    [InlineData("ms-teams.exe", ApplicationCategory.Communication)]
    [InlineData("VLC.exe", ApplicationCategory.Media)]
    [InlineData("EXCEL.EXE", ApplicationCategory.Office)]
    [InlineData("unknown-app.exe", ApplicationCategory.General)]
    [InlineData(null, ApplicationCategory.Unknown)]
    public void Classify_DoesNotNeedWindowTitles(string? processName, ApplicationCategory expected)
    {
        Assert.Equal(expected, _classifier.Classify(processName));
    }
}
