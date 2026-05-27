using System;
using System.Collections.Generic;
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
using System.IO;
using Path = System.IO.Path;

namespace MiniApp.Views
{
    /// <summary>
    /// Interaction logic for AttractVideoView.xaml
    /// </summary>
    public partial class AttractVideoView : Page
    {
        private string _jwtToken;
        private long _shopLoginId;
        private long _printerId;
        private string _printerName;
        private string _qrCodeData;
        private decimal _printerPrice;
        private DateTime _qrExpiry;
        private int _printerStatus;
        private int _printerPaymentEnabled;

        private bool _isNavigating = false;
        public AttractVideoView(string jwtToken, long shopLoginId, long printerId,
            string printerName, string qrUrl, string expiry, decimal printerPrice,
            int printerStatus, int printerPaymentEnabled)
        {
            InitializeComponent();

            _jwtToken = jwtToken;
            _shopLoginId = shopLoginId;
            _printerId = printerId;
            _printerName = printerName;
            _qrCodeData = qrUrl;
            _printerPrice = printerPrice;
            _printerPaymentEnabled = printerPaymentEnabled;

            // Parse expiry safely
            DateTime parsedExpiry;
            string expiryClean = expiry?.Replace(" UTC", "") ?? "";
            if (!DateTime.TryParse(expiryClean,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out parsedExpiry))
            {
                parsedExpiry = DateTime.UtcNow.AddMinutes(10);
            }
            _qrExpiry = parsedExpiry.ToUniversalTime();

            _printerStatus = printerStatus;

            // Input handling
            this.PreviewMouseDown += ReturnToQr;
            this.PreviewTouchDown += ReturnToQr;
            this.PreviewKeyDown += ReturnToQr;

            // Lifecycle events
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;

            this.Focusable = true;
            this.Focus();
        }

        //VIDEO START
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string videoPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets/CoverVideo.mp4"
                );

                LoopVideo.Source = new Uri(videoPath, UriKind.Absolute);
                LoopVideo.Position = TimeSpan.Zero;
                LoopVideo.Play();

                //AppLogger.Log("[VIDEO] Started");
            }
            catch (Exception ex)
            {
                AppLogger.Log("[VIDEO] Load error: " + ex.Message);
            }
        }

        //LOOP VIDEO
        private void LoopVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoopVideo.Position = TimeSpan.Zero;
                LoopVideo.Play();
            }
            catch (Exception ex)
            {
                AppLogger.Log("[VIDEO] Loop error: " + ex.Message);
            }
        }

        //HANDLE VIDEO FAILURE
        private void LoopVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            AppLogger.Log("[VIDEO] FAILED: " + e.ErrorException?.Message);
        }

        // MOST IMPORTANT: CLEANUP (prevents random freeze)
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoopVideo.Stop();
                LoopVideo.Source = null;
                //AppLogger.Log("[VIDEO] Cleaned up");
            }
            catch (Exception ex)
            {
                AppLogger.Log("[VIDEO] Cleanup error: " + ex.Message);
            }
        }

        // TOUCH / CLICK NAVIGATION
        private void ReturnToQr(object? sender, EventArgs e)
        {
            try
            {

                // Guard against multiple simultaneous triggers (Touch + Mouse)
                if (_isNavigating)
                {
                    //AppLogger.Log("ReturnToQr: Navigation already in progress.");
                    return;
                }

                _isNavigating = true;

                try
                {
                    // Mark the event as handled to stop further processing by Windows
                    if (e is RoutedEventArgs re)
                    {
                        re.Handled = true;
                        //AppLogger.Log("ReturnToQr: Routed event handled.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("ReturnToQr: Error while handling RoutedEventArgs - 2" + ex);
                }

                try
                {
                    // Immediately unsubscribe from events to prevent re-entry
                    this.PreviewMouseDown -= ReturnToQr;
                    this.PreviewTouchDown -= ReturnToQr;
                    this.PreviewKeyDown -= ReturnToQr;

                    //AppLogger.Log("ReturnToQr: Events unsubscribed successfully.");
                }
                catch (Exception ex)
                {
                    AppLogger.Log("ReturnToQr: Error while unsubscribing events - 3" + ex);
                }

                try
                {
                    //AppLogger.Log("ReturnToQr: Starting navigation to ScanQrView.");

                    NavigationService?.Navigate(new ScanQrView(
                        _jwtToken,
                        _shopLoginId,
                        _printerId,
                        _printerName,
                        _qrCodeData,
                        _printerPrice,
                        _printerStatus,
                        _qrExpiry.ToString("yyyy-MM-dd HH:mm:ss"),
                        _printerPaymentEnabled
                    ));

                    //AppLogger.Log("ReturnToQr: Navigation successful.");
                }
                catch (Exception ex)
                {
                    AppLogger.Log("ReturnToQr: Navigation error - " + ex);

                    // Final fail-safe navigation attempt
                    try
                    {
                        //AppLogger.Log("ReturnToQr: Trying fallback navigation.");

                        NavigationService?.Navigate(new ScanQrView(
                            _jwtToken,
                            _shopLoginId,
                            _printerId,
                            _printerName,
                            _qrCodeData,
                            _printerPrice,
                            _printerStatus,
                            _qrExpiry.ToString("yyyy-MM-dd HH:mm:ss"),
                            _printerPaymentEnabled
                        ));

                        //AppLogger.Log("ReturnToQr: Fallback navigation successful.5");
                    }
                    catch (Exception innerEx)
                    {
                        AppLogger.Log("ReturnToQr: Fallback navigation failed - 4" + innerEx);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("ReturnToQr: Unexpected outer exception - 1 - " + ex);
            }
        }
    }
}

