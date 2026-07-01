using System.Diagnostics;
using System.IO;

namespace AutoStartManager.Services;

public static class FileExplorerService
{
    public static void OpenInExplorer(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch { }
    }

    public static void OpenFileLocation(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch { }
    }
}
