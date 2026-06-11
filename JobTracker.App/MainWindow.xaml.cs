using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace JobTracker.App;

/// <summary>Interaction logic for MainWindow.xaml</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens job posting URLs in the system's default browser.
    /// Bound to the RequestNavigate event on each row's Hyperlink.
    /// </summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
