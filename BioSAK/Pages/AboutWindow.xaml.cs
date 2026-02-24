using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace BioSAK
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            // Set version from assembly
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version != null)
                {
                    VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";
                }
            }
            catch
            {
                VersionText.Text = "Version 1.0.0";
            }
        }

        private void EmailLink_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Clipboard.SetText("bowtobyd@gmail.com");
                MessageBox.Show("Email address copied to clipboard!", "Copied",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not copy email: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void GithubLink_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/bowyung/BioXAK",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open browser: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}