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

            DateTime parsedExpiry;
            string expiryClean = expiry?.Replace(" UTC", "") ?? "";
            if (!DateTime.TryParse(expiryClean, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out parsedExpiry))
            {
                parsedExpiry = DateTime.UtcNow.AddMinutes(10);
            }
            _qrExpiry = parsedExpiry.ToUniversalTime();

            _printerStatus = printerStatus;

            // Detect touch/mouse to return
            this.PreviewMouseDown += ReturnToQr;
            this.PreviewTouchDown += ReturnToQr;
            this.PreviewKeyDown += ReturnToQr;

            // Load video file
            string videoPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets/CoverVideo.mp4"
            );

            LoopVideo.Source = new Uri(videoPath, UriKind.Absolute);
            LoopVideo.Play();
        }

        private void LoopVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            LoopVideo.Position = TimeSpan.Zero;
            LoopVideo.Play();
        }

        private void ReturnToQr(object? sender, EventArgs e)
        {
            try
            {
                // Prefer going back so the existing ScanQrView instance (and its queue) remains intact.
                if (NavigationService != null && NavigationService.CanGoBack)
                {
                    NavigationService.GoBack();
                    return;
                }

                // Fallback: create with same params
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
            }
            catch (Exception ex)
            {
                AppLogger.Log("ReturnToQr error: " + ex.Message);

                // Last-resort fallback
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
            }
        }
    }
}

