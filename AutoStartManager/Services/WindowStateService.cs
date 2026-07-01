using System.IO;
using System.Text.Json;

namespace AutoStartManager.Services;

public class WindowPosition
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 550;
    public double Height { get; set; } = 600;
    public bool IsMaximized { get; set; }
}

public static class WindowStateService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoStartManager",
        "window.json");

    public static WindowPosition Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new WindowPosition();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<WindowPosition>(json) ?? new WindowPosition();
        }
        catch
        {
            return new WindowPosition();
        }
    }

    public static void Save(WindowPosition state)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(state);
            File.WriteAllText(FilePath, json);
        }
        catch { }
    }
}
