using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AutoStartManager.Models;
using Microsoft.Win32;

namespace AutoStartManager.Services;

public class StartupService
{
    private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public List<StartupItem> GetAll()
    {
        var items = new List<StartupItem>();
        items.AddRange(GetRegistryItems());
        items.AddRange(GetStartupFolderItems());
        return items;
    }

    private List<StartupItem> GetRegistryItems()
    {
        var items = new List<StartupItem>();
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath);
        if (key == null) return items;

        foreach (var valueName in key.GetValueNames())
        {
            var value = key.GetValue(valueName)?.ToString();
            if (string.IsNullOrEmpty(value)) continue;

            var isEnabled = !valueName.StartsWith("disabled_");

            items.Add(new StartupItem
            {
                Name = isEnabled ? valueName : valueName.Substring(9),
                Path = value,
                TargetPath = ResolveTarget(value),
                Source = StartupSource.Registry,
                IsEnabled = isEnabled
            });
        }
        return items;
    }

    private List<StartupItem> GetStartupFolderItems()
    {
        var items = new List<StartupItem>();
        var startupPath = GetStartupFolderPath();
        if (!Directory.Exists(startupPath)) return items;

        foreach (var file in Directory.GetFiles(startupPath, "*.lnk"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var isEnabled = !fileName.StartsWith("disabled_");
            var target = ResolveShortcut(file);

            items.Add(new StartupItem
            {
                Name = isEnabled ? fileName : fileName.Substring(9),
                Path = file,
                TargetPath = target,
                Source = StartupSource.StartupFolder,
                IsEnabled = isEnabled
            });
        }
        return items;
    }

    public void Delete(StartupItem item)
    {
        if (item.Source == StartupSource.Registry)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
            key?.DeleteValue(item.Name, throwOnMissingValue: false);
        }
        else
        {
            if (File.Exists(item.Path))
                File.Delete(item.Path);
            var disabledPath = GetDisabledPath(item.Path);
            if (File.Exists(disabledPath))
                File.Delete(disabledPath);
        }
    }

    public void SetEnabled(StartupItem item, bool enabled)
    {
        if (item.Source == StartupSource.Registry)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
            if (key == null) return;

            if (enabled)
            {
                var disabledName = item.Name.StartsWith("disabled_")
                    ? item.Name
                    : "disabled_" + item.Name;
                var cleanName = item.Name.StartsWith("disabled_")
                    ? item.Name.Substring(9)
                    : item.Name;

                key.DeleteValue(disabledName, throwOnMissingValue: false);
                key.SetValue(cleanName, item.Path);
                item.Name = cleanName;
            }
            else
            {
                var disabledName = item.Name.StartsWith("disabled_")
                    ? item.Name
                    : "disabled_" + item.Name;
                var cleanName = item.Name.StartsWith("disabled_")
                    ? item.Name.Substring(9)
                    : item.Name;

                key.DeleteValue(cleanName, throwOnMissingValue: false);
                key.SetValue(disabledName, item.Path);
                item.Name = disabledName;
            }
        }
        else
        {
            var dir = Path.GetDirectoryName(item.Path)!;
            var fileName = Path.GetFileNameWithoutExtension(item.Path);
            var ext = Path.GetExtension(item.Path);

            var cleanFileName = fileName.StartsWith("disabled_")
                ? fileName.Substring(9)
                : fileName;
            var disabledFileName = fileName.StartsWith("disabled_")
                ? fileName
                : "disabled_" + fileName;

            var cleanPath = Path.Combine(dir, cleanFileName + ext);
            var disabledPath = Path.Combine(dir, disabledFileName + ext);

            if (enabled)
            {
                if (File.Exists(disabledPath))
                    File.Move(disabledPath, cleanPath);
                item.Path = cleanPath;
                item.Name = cleanFileName;
            }
            else
            {
                if (File.Exists(cleanPath))
                    File.Move(cleanPath, disabledPath);
                item.Path = disabledPath;
                item.Name = disabledFileName;
            }
        }
        item.IsEnabled = enabled;
    }

    public void Add(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".exe" && ext != ".lnk")
            throw new ArgumentException("Only .exe and .lnk files are supported.");

        var target = ext == ".lnk" ? ResolveShortcut(filePath) : filePath;
        if (string.IsNullOrEmpty(target)) target = filePath;
        var name = Path.GetFileNameWithoutExtension(filePath);

        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true)
            ?? throw new InvalidOperationException("Cannot open registry key.");
        if (key.GetValue(name) != null)
            throw new InvalidOperationException($"\"{name}\" is already in the startup list.");
        key.SetValue(name, target);
    }

    public static string GetStartupFolderPath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    }

    private static string GetDisabledPath(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var file = Path.GetFileName(path);
        if (file.StartsWith("disabled_"))
            return path;
        return Path.Combine(dir, "disabled_" + file);
    }

    private static string ResolveTarget(string path)
    {
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return ResolveShortcut(path);
        return path;
    }

    public static string ResolveShortcut(string shortcutPath)
    {
        try
        {
            var shellLink = new ShellLink();
            ((IPersistFile)shellLink).Load(shortcutPath, 0);
            ((IShellLink)shellLink).Resolve(IntPtr.Zero, 0x0001);
            var targetPath = new StringBuilder(260);
            var pfd = new WIN32_FIND_DATA();
            ((IShellLink)shellLink).GetPath(targetPath, targetPath.Capacity, out pfd, 0);
            return targetPath.ToString();
        }
        catch
        {
            return shortcutPath;
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon, out int pwFlags);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out WIN32_FIND_DATA pfd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink : IShellLinkW { }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile(out string ppszFileName);
    }
}
