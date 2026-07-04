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
        items.AddRange(GetHKLMItems());
        try
        {
            var task = Task.Run(() => GetScheduledTaskItems());
            if (task.Wait(TimeSpan.FromSeconds(10)))
                items.AddRange(task.Result);
        }
        catch
        {
        }
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
                if (valueName.StartsWith("disabled_")) continue;

                var exePath = ExtractExecutablePath(value);
                items.Add(new StartupItem
                {
                    Name = valueName,
                    Path = exePath,
                    TargetPath = ResolveTarget(exePath),
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

                var exePath = ExtractExecutablePath(value);
                items.Add(new StartupItem
                {
                    Name = valueName,
                    Path = exePath,
                    TargetPath = ResolveTarget(exePath),
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
            if (fileName.StartsWith("disabled_")) continue;
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

    private List<StartupItem> GetHKLMItems()
    {
        var items = new List<StartupItem>();
        ReadRegistryHive(items, Registry.LocalMachine, RegistryRunPath);
        ReadRegistryHive(items, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run");
        return items;
    }

    private static void ReadRegistryHive(List<StartupItem> items, RegistryKey hive, string path)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key == null) return;

            foreach (var valueName in key.GetValueNames())
            {
                var value = key.GetValue(valueName)?.ToString();
                if (string.IsNullOrEmpty(value)) continue;

                var exePath = ExtractExecutablePath(value);
                items.Add(new StartupItem
                {
                    Name = valueName,
                    Path = exePath,
                    TargetPath = ResolveTarget(exePath),
                    Source = StartupSource.RegistryLocalMachine,
                    IsEnabled = true,
                    RequiresAdmin = true
                });
            }
        }
        catch
        {
        }
    }

    private List<StartupItem> GetScheduledTaskItems()
    {
        var items = new List<StartupItem>();
        try
        {
            Type? schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) return items;

            dynamic? scheduler = Activator.CreateInstance(schedulerType);
            if (scheduler == null) return items;

            scheduler.Connect();
            dynamic? folder = scheduler.GetFolder("\\");
            if (folder == null) return items;

            EnumerateFolderTasks(folder, items);
        }
        catch
        {
        }

        return items;
    }

    private void EnumerateFolderTasks(dynamic folder, List<StartupItem> items)
    {
        try
        {
            dynamic? taskCollection = folder.GetTasks(1);
            if (taskCollection != null)
            {
                foreach (dynamic task in taskCollection)
                {
                    try
                    {
                        dynamic? definition = task.Definition;
                        if (definition == null) continue;

                        dynamic? triggers = definition.Triggers;
                        if (triggers == null) continue;

                        bool hasStartupTrigger = false;
                        foreach (dynamic trigger in triggers)
                        {
                            int triggerType = (int)trigger.Type;
                            if (triggerType == 8 || triggerType == 9 || triggerType == 11)
                            {
                                hasStartupTrigger = true;
                                break;
                            }
                        }

                        if (!hasStartupTrigger) continue;

                        string taskPath = (string)task.Path;
                        string taskName = (string)task.Name;
                        bool isEnabled = (bool)task.Enabled;

                        string targetPath = "";
                        try
                        {
                            dynamic? actions = definition.Actions;
                            if (actions != null && actions.Count > 0)
                                targetPath = (string)actions[0].Path ?? "";
                        }
                        catch
                        {
                        }

                        items.Add(new StartupItem
                        {
                            Name = taskName,
                            Path = taskPath,
                            TargetPath = targetPath,
                            Source = StartupSource.ScheduledTask,
                            IsEnabled = isEnabled,
                            RequiresAdmin = false
                        });
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }

        try
        {
            dynamic? subFolders = folder.GetFolders(0);
            if (subFolders != null)
            {
                foreach (dynamic subFolder in subFolders)
                {
                    string subPath = (string)subFolder.Path;
                    if (subPath.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase))
                        continue;
                    EnumerateFolderTasks(subFolder, items);
                }
            }
        }
        catch
        {
        }
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

                var exePath = ExtractExecutablePath(value);
                if (!currentItems.Any(i => i.Name == cleanName && i.Source == StartupSource.Registry))
                {
                    currentItems.Add(new StartupItem
                    {
                        Name = cleanName,
                        Path = exePath,
                        TargetPath = ResolveTarget(exePath),
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
        switch (item.Source)
        {
            case StartupSource.Registry:
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
                key?.DeleteValue(item.Name, throwOnMissingValue: false);
                using var disabledKey = Registry.CurrentUser.OpenSubKey(DisabledRegistryPath, writable: true);
                disabledKey?.DeleteValue(item.Name, throwOnMissingValue: false);
                break;
            }
            case StartupSource.StartupFolder:
            {
                if (File.Exists(item.Path))
                    File.Delete(item.Path);
                var disabledFolder = GetStartupDisabledFolderPath();
                var disabledFilePath = Path.Combine(disabledFolder, Path.GetFileName(item.Path));
                if (File.Exists(disabledFilePath))
                    File.Delete(disabledFilePath);
                break;
            }
            case StartupSource.RegistryLocalMachine:
            {
                AdminHelperService.DeleteRegistryValue(item.Name);
                break;
            }
            case StartupSource.ScheduledTask:
            {
                if (!TryDeleteTaskCom(item.Path))
                    AdminHelperService.DeleteTask(item.Path);
                break;
            }
        }
    }

    public void SetEnabled(StartupItem item, bool enabled)
    {
        switch (item.Source)
        {
            case StartupSource.Registry:
                SetRegistryEnabled(item, enabled);
                break;
            case StartupSource.StartupFolder:
                SetStartupFolderEnabled(item, enabled);
                break;
            case StartupSource.RegistryLocalMachine:
                if (enabled)
                    AdminHelperService.SetRegistryValue(item.Name, item.Path);
                else
                    AdminHelperService.DeleteRegistryValue(item.Name);
                break;
            case StartupSource.ScheduledTask:
                if (!TrySetTaskEnabledCom(item.Path, enabled))
                    AdminHelperService.SetTaskEnabled(item.Path, enabled);
                break;
        }
        item.IsEnabled = enabled;
    }

    private void SetRegistryEnabled(StartupItem item, bool enabled)
    {
        if (enabled)
        {
            using var disabledKey = Registry.CurrentUser.OpenSubKey(DisabledRegistryPath, writable: true);
            disabledKey?.DeleteValue(item.Name, throwOnMissingValue: false);

            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
            key?.SetValue(item.Name, QuotePath(item.Path));
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
            key?.DeleteValue(item.Name, throwOnMissingValue: false);

            using var disabledKey = Registry.CurrentUser.CreateSubKey(DisabledRegistryPath);
            disabledKey?.SetValue(item.Name, QuotePath(item.Path));
        }
    }

    private void SetStartupFolderEnabled(StartupItem item, bool enabled)
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

    private static bool TrySetTaskEnabledCom(string taskPath, bool enabled)
    {
        try
        {
            Type? schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) return false;

            dynamic? scheduler = Activator.CreateInstance(schedulerType);
            if (scheduler == null) return false;

            scheduler.Connect();
            dynamic? folder = scheduler.GetFolder("\\");
            if (folder == null) return false;

            dynamic? task = folder.GetTask(taskPath);
            if (task == null) return false;

            task.Enabled = enabled;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeleteTaskCom(string taskPath)
    {
        try
        {
            Type? schedulerType = Type.GetTypeFromProgID("Schedule.Service");
            if (schedulerType == null) return false;

            dynamic? scheduler = Activator.CreateInstance(schedulerType);
            if (scheduler == null) return false;

            scheduler.Connect();
            dynamic? folder = scheduler.GetFolder("\\");
            if (folder == null) return false;

            folder.DeleteTask(taskPath, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string QuotePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.Contains(' ') && !path.StartsWith("\""))
            return "\"" + path + "\"";
        return path;
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
        key.SetValue(name, QuotePath(target));
    }

    public static string GetStartupFolderPath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    }

    private static string ExtractExecutablePath(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        value = value.Trim();

        if (value.StartsWith("\""))
        {
            var endQuote = value.IndexOf('"', 1);
            if (endQuote > 0)
                return value.Substring(1, endQuote - 1);
        }

        return value;
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
