namespace AutoStartManager.Models;

public enum StartupSource
{
    Registry,
    StartupFolder,
    RegistryLocalMachine,
    ScheduledTask
}

public class StartupItem : ObservableObject
{
    private string _name = string.Empty;
    private string _path = string.Empty;
    private string _targetPath = string.Empty;
    private StartupSource _source;
    private bool _isEnabled = true;
    private bool _requiresAdmin;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public string TargetPath
    {
        get => _targetPath;
        set => SetProperty(ref _targetPath, value);
    }

    public StartupSource Source
    {
        get => _source;
        set => SetProperty(ref _source, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool RequiresAdmin
    {
        get => _requiresAdmin;
        set => SetProperty(ref _requiresAdmin, value);
    }

    public string SourceDisplay => Source switch
    {
        StartupSource.Registry => "Registry (HKCU)",
        StartupSource.StartupFolder => "Startup Folder",
        StartupSource.RegistryLocalMachine => "Registry (HKLM)",
        StartupSource.ScheduledTask => "Scheduled Task",
        _ => "Unknown"
    };
}

public class ObservableObject : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}
