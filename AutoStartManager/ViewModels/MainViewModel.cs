using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using AutoStartManager.Models;
using AutoStartManager.Services;
using Microsoft.Win32;

namespace AutoStartManager.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly StartupService _startupService = new();
    private readonly AppIndexService _appIndexService = new();
    private string _searchText = string.Empty;
    private string _appSearchText = string.Empty;
    private ICollectionView? _filteredView;
    private ICollectionView? _installedAppsView;
    private readonly DispatcherTimer _searchTimer;

    public event Action<string>? ToastRequested;

    public ObservableCollection<StartupItem> Items { get; } = new();
    public ObservableCollection<InstalledApp> InstalledApps { get; } = new();

    public ICollectionView? FilteredView
    {
        get => _filteredView;
        set => SetProperty(ref _filteredView, value);
    }

    public ICollectionView? InstalledAppsView
    {
        get => _installedAppsView;
        set => SetProperty(ref _installedAppsView, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                FilteredView?.Refresh();
                OnPropertyChanged(nameof(HasSearchText));
            }
        }
    }

    public string AppSearchText
    {
        get => _appSearchText;
        set
        {
            if (SetProperty(ref _appSearchText, value))
            {
                _searchTimer.Stop();
                _searchTimer.Start();
                OnPropertyChanged(nameof(HasAppSearchText));
            }
        }
    }

    public bool HasSearchText => !string.IsNullOrEmpty(_searchText);
    public bool HasAppSearchText => !string.IsNullOrEmpty(_appSearchText);

    public ICommand DeleteCommand { get; }
    public ICommand OpenInExplorerCommand { get; }
    public ICommand ToggleCommand { get; }
    public ICommand BrowseAndAddCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ClearAppSearchCommand { get; }
    public ICommand AddInstalledAppCommand { get; }

    public MainViewModel()
    {
        DeleteCommand = new RelayCommand<StartupItem>(DeleteItem);
        OpenInExplorerCommand = new RelayCommand<StartupItem>(OpenItemInExplorer);
        ToggleCommand = new RelayCommand<StartupItem>(ToggleItem);
        BrowseAndAddCommand = new RelayCommand(BrowseAndAdd);
        RefreshCommand = new RelayCommand(Refresh);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        ClearAppSearchCommand = new RelayCommand(ClearAppSearch);
        AddInstalledAppCommand = new RelayCommand<InstalledApp>(AddInstalledApp);

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchTimer.Tick += (s, e) =>
        {
            _searchTimer.Stop();
            InstalledAppsView?.Refresh();
        };

        LoadItems();
        LoadInstalledAppsAsync();
    }

    public void LoadItems()
    {
        Items.Clear();
        foreach (var item in _startupService.GetAll().OrderBy(i => i.Name))
            Items.Add(item);

        FilteredView = null;
        FilteredView = CollectionViewSource.GetDefaultView(Items);
        FilteredView.Filter = FilterPredicate;
        FilteredView.Refresh();
    }

    private async void LoadInstalledAppsAsync()
    {
        var apps = await Task.Run(() => _appIndexService.GetAllInstalledApps());
        InstalledApps.Clear();
        foreach (var app in apps)
            InstalledApps.Add(app);

        InstalledAppsView = CollectionViewSource.GetDefaultView(InstalledApps);
        InstalledAppsView.Filter = AppFilterPredicate;
    }

    private bool FilterPredicate(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;
        if (obj is StartupItem item)
            return item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private bool AppFilterPredicate(object obj)
    {
        if (string.IsNullOrWhiteSpace(AppSearchText))
            return false;
        if (obj is InstalledApp app)
            return app.Name.Contains(AppSearchText, StringComparison.OrdinalIgnoreCase) ||
                   app.TargetPath.Contains(AppSearchText, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    internal void AddInstalledApp(InstalledApp? app)
    {
        if (app == null) return;
        try
        {
            if (Items.Any(i => i.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ToastRequested?.Invoke($"\"{app.Name}\" is already in the startup list.");
                return;
            }
            _startupService.Add(app.Path);
            LoadItems();
            AppSearchText = string.Empty;
            ToastRequested?.Invoke($"\"{app.Name}\" added to startup.");
        }
        catch (Exception ex)
        {
            ToastRequested?.Invoke($"Failed to add \"{app.Name}\": {ex.Message}");
        }
    }

    private void DeleteItem(StartupItem? item)
    {
        if (item == null) return;
        _startupService.Delete(item);
        Items.Remove(item);
        ToastRequested?.Invoke($"\"{item.Name}\" removed from startup.");
    }

    private void OpenItemInExplorer(StartupItem? item)
    {
        if (item == null) return;

        var target = !string.IsNullOrEmpty(item.TargetPath) ? item.TargetPath : item.Path;
        FileExplorerService.OpenInExplorer(target);
    }

    private void ToggleItem(StartupItem? item)
    {
        if (item == null) return;
        _startupService.SetEnabled(item, !item.IsEnabled);
    }

    private void BrowseAndAdd()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select an application to add to startup",
            Filter = "Executable files (*.exe)|*.exe|Shortcut files (*.lnk)|*.lnk|All files (*.*)|*.*",
            Multiselect = false,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        if (dialog.ShowDialog() == true)
        {
            AddFile(dialog.FileName);
        }
    }

    public void AddFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        if (!File.Exists(filePath)) return;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".exe" && ext != ".lnk")
        {
            ToastRequested?.Invoke("Please select an executable (.exe) or shortcut (.lnk) file.");
            return;
        }

        var name = Path.GetFileNameWithoutExtension(filePath);
        if (Items.Any(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ToastRequested?.Invoke($"\"{name}\" is already in the startup list.");
            return;
        }

        try
        {
            _startupService.Add(filePath);
            LoadItems();
            ToastRequested?.Invoke($"\"{name}\" added to startup.");
        }
        catch (Exception ex)
        {
            ToastRequested?.Invoke($"Failed to add \"{name}\": {ex.Message}");
        }
    }

    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    private void ClearAppSearch()
    {
        AppSearchText = string.Empty;
    }

    private void Refresh()
    {
        LoadItems();
    }
}
