using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AutoStartManager.Services;
using AutoStartManager.ViewModels;

namespace AutoStartManager;

public partial class MainWindow
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        ViewModel.ToastRequested += ShowToast;
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastBorder.BeginAnimation(UIElement.OpacityProperty, null);
        ToastBorder.Opacity = 0;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500));
        fadeOut.BeginTime = TimeSpan.FromSeconds(4);

        var storyboard = new Storyboard();
        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(fadeOut);
        Storyboard.SetTarget(fadeIn, ToastBorder);
        Storyboard.SetTarget(fadeOut, ToastBorder);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));

        storyboard.Begin();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var pos = WindowStateService.Load();
        if (!double.IsNaN(pos.Left)) Left = pos.Left;
        if (!double.IsNaN(pos.Top)) Top = pos.Top;
        if (pos.Width > 0) Width = pos.Width;
        if (pos.Height > 0) Height = pos.Height;
        if (pos.IsMaximized) WindowState = System.Windows.WindowState.Maximized;
        Topmost = PinToggle.IsChecked == true;
        AppSearchBox.Focus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        WindowStateService.Save(new WindowPosition
        {
            Left = Left,
            Top = Top,
            Width = Width,
            Height = Height,
            IsMaximized = WindowState == System.Windows.WindowState.Maximized
        });
    }

    private void DropZone_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            ViewModel.AddFile(files[0]);
        }
    }

    private void DropZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel.BrowseAndAddCommand.Execute(null);
    }

    private void AppSearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => ViewModel.AppSearchText = string.Empty);
    }

    private void PinToggle_Click(object sender, RoutedEventArgs e)
    {
        Topmost = PinToggle.IsChecked == true;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
    }

    private void AddInstalledApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Models.InstalledApp app)
        {
            ViewModel.AddInstalledApp(app);
            ViewModel.AppSearchText = string.Empty;
            AppSearchBox.Text = string.Empty;
            AppSearchBox.Focus();
        }
    }
}
