using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BioSAK.Services;

namespace BioSAK.Pages
{
    public partial class TcgaDataDownloadWindow : Window
    {
        private readonly TcgaDownloadService _downloader = new();
        private CancellationTokenSource _cts;
        private bool _downloadCompleted = false;

        /// <summary>Returns true if download completed successfully.</summary>
        public bool DownloadCompleted => _downloadCompleted;

        public TcgaDataDownloadWindow()
        {
            InitializeComponent();
        }

        // ── Start / Cancel button ─────────────────────────────────

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                // Download in progress → cancel
                _cts.Cancel();
                return;
            }

            await StartDownloadAsync();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            DialogResult = false;
            Close();
        }

        // ── Download logic ────────────────────────────────────────

        private async Task StartDownloadAsync()
        {
            _cts = new CancellationTokenSource();

            ActionButton.Content = "⏹  Cancel Download";
            ActionButton.Background = System.Windows.Media.Brushes.OrangeRed;
            SkipButton.IsEnabled = false;

            var progress = new Progress<DownloadProgress>(p =>
            {
                MainProgress.Value = p.Percent;
                PercentText.Text = $"{p.Percent}%";
                StatusText.Text = p.StatusText;

                if (p.TotalFiles > 0)
                    FileCountText.Text = $"Files: {p.DoneFiles} / {p.TotalFiles}";
            });

            try
            {
                await _downloader.DownloadAllAsync(progress, _cts.Token);

                _downloadCompleted = true;
                StatusText.Text = "✅ All TCGA data downloaded successfully!";
                FileCountText.Text = "Closing window...";

                ActionButton.Content = "✔  Done";
                ActionButton.Background = System.Windows.Media.Brushes.SeaGreen;
                ActionButton.IsEnabled = false;
                SkipButton.IsEnabled = false;

                // Auto-close after 1.5 seconds
                await Task.Delay(1500);
                DialogResult = true;
                Close();
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "⚠️  Download cancelled.";
                FileCountText.Text = "Partially downloaded files are retained. You can resume later.";
                ResetButtons();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Download failed: {ex.Message}";
                FileCountText.Text = "Please check your internet connection and try again.";
                ResetButtons();
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void ResetButtons()
        {
            ActionButton.Content = "▶  Retry Download";
            ActionButton.Background = System.Windows.Media.Brushes.SteelBlue;
            ActionButton.IsEnabled = true;
            SkipButton.IsEnabled = true;
        }

        // ── Cancel download on window close ──────────────────────

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            base.OnClosed(e);
        }
    }
}