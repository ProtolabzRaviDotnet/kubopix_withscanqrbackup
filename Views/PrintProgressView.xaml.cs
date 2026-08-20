using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace kuboPix.Views
{
    /// <summary>
    /// Interaction logic for PrintProgressView.xaml
    /// </summary>
    public partial class PrintProgressView : Page
    {
        // Private members
        private int seconds = 30;
        private DispatcherTimer? timer;
        private bool timerCompleted = false;
        // Initialized here (not in StartTimer/Page_Loaded) so WaitForCompletionAsync()
        // called from a background thread can never race against Page_Loaded firing late.
        private readonly TaskCompletionSource<bool> timerCompletionSource =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Constructor
        public PrintProgressView()
        {
            InitializeComponent();
        }

        // Page Loaded event handler
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeVideo();
            StartTimer();
            // Memory fix (M-3): release the video when this page leaves the screen.
            // Only touches the MediaElement — the countdown timer / completion signal are untouched.
            Unloaded -= Page_Unloaded;
            Unloaded += Page_Unloaded;
        }

        // Release the MediaElement so its video buffers are freed promptly (M-3).
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoaderVideo.MediaEnded -= LoaderVideo_MediaEnded;
                LoaderVideo.MediaFailed -= LoaderVideo_MediaFailed;
                LoaderVideo.Stop();
                LoaderVideo.Close();
                LoaderVideo.Source = null;
            }
            catch (Exception ex)
            {
                AppLogger.Log("PrintProgressView video cleanup error: " + ex.Message);
            }
        }

        // Initialize and play the video
        private void InitializeVideo()
        {
            try
            {
                string videoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "printprogress.mp4");

                if (File.Exists(videoPath))
                {
                    LoaderVideo.Source = new Uri(videoPath, UriKind.Absolute);
                    LoaderVideo.Play();
                }
                else
                {
                    LoaderVideo.Source = new Uri("/Assets/printprogress.mp4", UriKind.Relative);
                    LoaderVideo.Play();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Video initialization failed: {ex.Message}");
            }
        }

        // Start the countdown timer
        private void StartTimer()
        {
            timerCompleted = false;
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        // Timer tick event handler
        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (seconds > 0)
            {
                seconds--;
                TimerText.Text = "00:" + seconds.ToString("D2");
                progressBar.Value = ((30.0 - seconds) / 30.0) * 100.0;
            }
            else
            {
                progressBar.Value = 100.0;
                timer?.Stop();
                timerCompleted = true;
                timerCompletionSource.TrySetResult(true);
                LoaderVideo.Stop();

                // ── BYPASS MODE: auto-navigate to PrintSuccessView ─────────────
                // In production, the caller (ScanQrView/OrderQueueService) awaits
                // WaitForCompletionAsync() and handles navigation itself.
                if (App.BypassPrinterForDemo)
                {
                    NavigationService?.Navigate(new PrintSuccessView());
                }
            }
        }

        // Video event handlers
        private void LoaderVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            LoaderVideo.Position = TimeSpan.Zero;
            LoaderVideo.Play();
        }

        // Video media failed event handler
        private void LoaderVideo_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            AppLogger.Log($"Video playback failed: {e.ErrorException?.Message}");
        }

        // Return true if the timer has completed
        public bool IsTimerCompleted()
        {
            return timerCompleted;
        }

        // Stop the timer forcefully
        public void StopTimer()
        {
            try
            {
                if (timer != null)
                {
                    timer.Stop();
                }

                timerCompleted = true;
                timerCompletionSource.TrySetResult(true);

                if (LoaderVideo != null)
                    LoaderVideo.Stop();

                AppLogger.Log("PrintProgress timer stopped forcefully due to print error.");
            }
            catch (Exception ex)
            {
                AppLogger.Log("StopTimer Exception: " + ex.Message);
            }
        }

        // Wait for the progress timer to complete
        public Task WaitForCompletionAsync()
        {
            return timerCompletionSource.Task;
        }

    }



}
