using System.Windows;

namespace StemForge.App;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Theme.WindowChrome.ApplySkeerChrome(this);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
