using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StemForge.App.ViewModels;

namespace StemForge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        StartupTrace.Write("MainWindow:ctor:enter");
        _viewModel = new MainViewModel();
        StartupTrace.Write("MainWindow:ctor:viewmodel-ready");

        InitializeComponent();
        StartupTrace.Write("MainWindow:ctor:xaml-loaded");

        DataContext = _viewModel;
        PlaceOnPrimaryScreen();

        Loaded += (_, _) =>
        {
            StartupTrace.Write("MainWindow:loaded");
            Theme.WindowChrome.ApplySkeerChrome(this);
        };
    }

    /// <summary>
    /// Centre the window on the primary display. WindowStartupLocation=CenterScreen centres
    /// on whichever monitor the pointer is on, which on a multi-monitor desk can put most of
    /// the window outside the visible desktop.
    /// </summary>
    private void PlaceOnPrimaryScreen()
    {
        var work = SystemParameters.WorkArea;
        var width = Math.Min(Width, work.Width);
        var height = Math.Min(Height, work.Height);

        Left = work.Left + Math.Max(0, (work.Width - width) / 2);
        Top = work.Top + Math.Max(0, (work.Height - height) / 2);
    }

    /// <summary>Opens a button's own menu below it (the overflow and folder options).</summary>
    private static void OpenMenu(Button button)
    {
        if (button.ContextMenu is not { } menu) return;

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnHousekeepingClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) OpenMenu(button);
    }

    /// <summary>
    /// Folder import options live with the action: recursive import is a property of adding
    /// a folder, not a global setting.
    /// </summary>
    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) OpenMenu(button);
    }

    private void OnDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var window = new DiagnosticsWindow { Owner = this, DataContext = _viewModel };
        window.ShowDialog();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.AddDropped(paths);
        }

        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Shutdown();
        base.OnClosed(e);
    }
}
