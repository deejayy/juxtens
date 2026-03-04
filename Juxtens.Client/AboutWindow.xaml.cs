using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace Juxtens.Client;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        LoadVersionInfo();
    }

    private void LoadVersionInfo()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var infoVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            VersionTextBlock.Text = $"Version {infoVersion ?? "0.0.0.0"}";

            var currentYear = DateTime.Now.Year;
            CopyrightTextBlock.Text = $"© {currentYear} DeeJayy. All rights reserved.";
        }
        catch
        {
            VersionTextBlock.Text = "Version 0.0.0.0";
            CopyrightTextBlock.Text = "© 2026 DeeJayy. All rights reserved.";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void GitHubHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch
        {
        }
    }
}
