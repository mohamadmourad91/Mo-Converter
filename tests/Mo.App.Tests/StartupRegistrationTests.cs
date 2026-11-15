using System.IO;
using Mo.App.Services;

namespace Mo.App.Tests;

public class StartupRegistrationTests
{
    [Fact]
    public void A_normal_test_run_is_not_a_store_package()
    {
        // Tests run unpackaged, so the registry route is used; the packaged route needs the real Store package.
        Assert.False(StartupRegistration.IsPackaged);
    }

    [Fact]
    public void The_startup_task_id_matches_the_manifest_template()
    {
        var root = AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
        {
            var manifest = Path.Combine(dir.FullName, "build", "msix", "AppxManifest.template.xml");
            if (!File.Exists(manifest)) continue;
            Assert.Contains($"TaskId=\"{StartupRegistration.StartupTaskId}\"", File.ReadAllText(manifest));
            return;
        }
        throw new InvalidOperationException("Manifest template not found.");
    }
}
