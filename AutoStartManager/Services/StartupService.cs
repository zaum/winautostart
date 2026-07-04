using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AutoStartManager.Models;
using Microsoft.Win32;

namespace AutoStartManager.Services;

public class StartupService
{
    private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledRegistryPath = @"Software\AutoStartManager\DisabledItems";
    private const string DisabledFolderName = "AutoStartManager_Disabled";

    public List<StartupItem> GetAll()
    {
        var items = new List<StartupItem>();
        items.AddRange(GetRegistryItems());
        items.AddRange(GetStartupFolderItems());
        MigrateOldDisabledEntries(items);
        return items;
    }

    private List<StartupItem> GetRegistryItems()
    {
        var items = new List<StartupItem>();
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath);
        if (key != null)
        {
            foreach (var valueName in key.GetValueNames())
            {
                var value = key.GetValue(valueName)?.ToString();
                if (string.IsNullOrEmpty(value)) continue;
                if (valueName.StartsWith("disabled_")) continue; // old format, migrated in MigrateOldDisabledEntries

                items.Add(new StartupItem
                {
                    Name = valueName,
                    Path = value,
                    TargetPath = ResolveTarget(value),
                    Source = StartupSource.Registry,
                    IsEnabled = true
                });
            }
        }

        using var disabledKey = Registry.CurrentUser.OpenSubKey(DisabledRegistryPath);
        if (disabledKey != null)
        {
            foreach (var valueName in disabledKey.GetValueNames())
            {
                var value = disabledKey.GetValue(valueName)?.ToString();
                if (string.IsNullOrEmpty(value)) continue;

                items.Add(new StartupItem
                {
                    Name = valueName,
                    Path = value,
                    TargetPath = ResolveTarget(value),
                    Source = StartupSource.Registry,
                    IsEnabled = false
                });
            }
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
            if (fileName.StartsWith("disabled_")) continue; // old format, migrated
            var target = ResolveShortcut(file);

            items.Add(new StartupItem
            {
                Name = fileName,
                Path = file,
                TargetPath = target,
                Source = StartupSource.StartupFolder,
                IsEnabled = true
            });
        }

        var disabledPath = GetStartupDisabledFolderPath();
        if (Directory.Exists(disabledPath))
        {
            foreach (var file in Directory.GetFiles(disabledPath, "*.lnk"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var target = ResolveShortcut(file);

                items.Add(new StartupItem
                {
                    Name = fileName,
                    Path = file,
                    TargetPath = target,
                    Source = StartupSource.StartupFolder,
                    IsEnabled = false
                });
            }
        }

        return items;
    }

    private static string GetStartupDisabledFolderPath()
    {
        return Path.Combine(GetStartupFolderPath(), DisabledFolderName);
    }

    private void MigrateOldDisabledEntries(List<StartupItem> currentItems)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
        if (key != null)
        {
            foreach (var valueName in key.GetValueNames())
            {
                if (!valueName.StartsWith("disabled_")) continue;
                var cleanName = valueName.Substring(9);
                var value = key.GetValue(valueName)?.ToString();
                if (string.IsNullOrEmpty(value)) continue;

                using var disabledKey = Registry.CurrentUser.CreateSubKey(DisabledRegistryPath);
                disabledKey?.SetValue(cleanName, value);
                key.DeleteValue(valueName, throwOnMissingValue: false);

                if (!currentItems.Any(i => i.Name == cleanName && i.Source == StartupSource.Registry))
                {
                    currentItems.Add(new StartupItem
                    {
                        Name = cleanName,
                        Path = value,
                        TargetPath = ResolveTarget(value),
                        Source = StartupSource.Registry,
                        IsEnabled = false
                    });
                }
            }
        }

        var startupPath = GetStartupFolderPath();
        var disabledFolder = GetStartupDisabledFolderPath();
        if (!Directory.Exists(startupPath)) return;

        foreach (var file in Directory.GetFiles(startupPath, "disabled_*.lnk"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!fileName.StartsWith("disabled_")) continue;
            var cleanName = fileName.Substring(9);
            var ext = Path.GetExtension(file);
            var cleanPath = Path.Combine(startupPath, cleanName + ext);

            Directory.CreateDirectory(disabledFolder);
            var destPath = Path.Combine(disabledFolder, cleanName + ext);
            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(file, destPath);

            var target = ResolveShortcut(destPath);
            if (!currentItems.Any(i => i.Name == cleanName && i.Source == StartupSource.StartupFolder))
            {
                currentItems.Add(new StartupItem
                {
                    Name = cleanName,
                    Path = destPath,
                    TargetPath = target,
                    Source = StartupSource.StartupFolder,
                    IsEnabled = false
                });
            }
        }
    }

    public void Delete(StartupItem item)
    {
        if (item.Source == StartupSource.Registry)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
            key?.DeleteValue(item.Name, throwOnMissingValue: false);
            using var disabledKey = Registry.CurrentUser.OpenSubKey(DisabledRegistryPath, writable: true);
            disabledKey?.DeleteValue(item.Name, throwOnMissingValue: false);
        }
        else
        {
            if (File.Exists(item.Path))
                File.Delete(item.Path);
            var disabledFolder = GetStartupDisabledFolderPath();
            var disabledFilePath = Path.Combine(disabledFolder, Path.GetFileName(item.Path));
            if (File.Exists(disabledFilePath))
                File.Delete(disabledFilePath);
        }
    }

    public void SetEnabled(StartupItem item, bool enabled)
    {
        if (item.Source == StartupSource.Registry)
        {
            if (enabled)
            {
                using var disabledKey = Registry.CurrentUser.OpenSubKey(DisabledRegistryPath, writable: true);
                disabledKey?.DeleteValue(item.Name, throwOnMissingValue: false);

                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
                key?.SetValue(item.Name, item.Path);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
                key?.DeleteValue(item.Name, throwOnMissingValue: false);

                using var disabledKey = Registry.CurrentUser.CreateSubKey(DisabledRegistryPath);
                disabledKey?.SetValue(item.Name, item.Path);
            }
        }
        else
        {
            var startupPath = GetStartupFolderPath();
            var disabledFolder = GetStartupDisabledFolderPath();
            var fileName = Path.GetFileName(item.Path);
            var startupFilePath = Path.Combine(startupPath, fileName);
            var disabledFilePath = Path.Combine(disabledFolder, fileName);

            if (enabled)
            {
                if (File.Exists(disabledFilePath))
                {
                    Directory.CreateDirectory(startupPath);
                    File.Move(disabledFilePath, startupFilePath, overwrite: true);
                }
                item.Path = startupFilePath;
            }
            else
            {
                if (File.Exists(startupFilePath))
                {
                    Directory.CreateDirectory(disabledFolder);
                    File.Move(startupFilePath, disabledFilePath, overwrite: true);
                }
                item.Path = disabledFilePath;
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
