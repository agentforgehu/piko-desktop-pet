using Piko.Context.Interventions;
using Piko.Context.Situations;

namespace Piko.Runtime.Tests;

public sealed class RuntimeStatusStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsHealthWithoutSensitiveContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "piko-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "status.json");
            var store = new RuntimeStatusStore(path);
            var status = new RuntimeStatusSnapshot(
                1,
                "1.0.0-alpha.1",
                42,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                "healthy",
                SituationKind.Coding,
                0.85,
                InterventionKind.None,
                "application.foreground.changed");

            store.Save(status);
            var replay = store.Load();

            Assert.Equal(status, replay);
            var json = File.ReadAllText(path);
            Assert.DoesNotContain("windowTitle", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("filePath", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
