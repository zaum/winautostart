using System.IO;
using AutoStartManager.Models;

namespace AutoStartManager.Services;

public class AppIndexService
{
    public List<InstalledApp> GetAllInstalledApps()
    {
        var apps = new List<InstalledApp>();
        var startMenuPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        };

        foreach (var dir in startMenuPaths)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    var target = StartupService.ResolveShortcut(lnk);
                    apps.Add(new InstalledApp
                    {
                        Name = name,
                        Path = lnk,
                        TargetPath = string.IsNullOrEmpty(target) ? lnk : target
                    });
                }
                catch
                {
                }
            }
        }

        return [.. apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
