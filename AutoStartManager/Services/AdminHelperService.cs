using System.Diagnostics;

namespace AutoStartManager.Services;

public static class AdminHelperService
{
    private const string HKLM_RunPath = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool DeleteRegistryValue(string valueName)
    {
        return RunElevated("reg", $"delete \"{HKLM_RunPath}\" /v \"{valueName}\" /f");
    }

    public static bool SetRegistryValue(string valueName, string valueData)
    {
        return RunElevated("reg", $"add \"{HKLM_RunPath}\" /v \"{valueName}\" /d \"{valueData}\" /f");
    }

    public static bool SetTaskEnabled(string taskPath, bool enabled)
    {
        var flag = enabled ? "ENABLE" : "DISABLE";
        return RunElevated("schtasks", $"/Change /TN \"{taskPath}\" /{flag}");
    }

    public static bool DeleteTask(string taskPath)
    {
        return RunElevated("schtasks", $"/Delete /TN \"{taskPath}\" /F");
    }

    private static bool RunElevated(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
