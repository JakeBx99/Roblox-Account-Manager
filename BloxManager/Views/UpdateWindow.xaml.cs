using System.Windows;
using System.Windows.Input;

namespace BloxManager.Views
{
    public partial class UpdateWindow : Window
    {
        public bool ShouldUpdate { get; private set; }

        public UpdateWindow(string currentVersion, string latestVersion)
        {
            InitializeComponent();
            StatusText.Text = $"A new version of BloxManager is available: {latestVersion}\nCurrent version: {currentVersion}\n\nWould you like to update now?";
        }

        private void OnUpdateNow(object sender, RoutedEventArgs e)
        {
            ShouldUpdate = true;
            ButtonArea.Visibility = Visibility.Collapsed;
            ProgressArea.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Visible;
            StatusText.Text = "Downloading update...";
            // We do not close the window here, we wait for the download to finish
            // The viewmodel will update the progress
        }

        private void OnUpdateLater(object sender, RoutedEventArgs e)
        {
            ShouldUpdate = false;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            // Optional: Support cancellation
            // For now, just close
            ShouldUpdate = false;
            Close();
        }

        public void UpdateProgress(double percentage)
        {
            Dispatcher.Invoke(() =>
            {
                DownloadProgress.Value = percentage;
                ProgressText.Text = $"{percentage:F1}%";
            });
        }
        
        // Allow dragging the window
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }
    }
}
