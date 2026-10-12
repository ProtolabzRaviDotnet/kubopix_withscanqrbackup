using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
using System.Globalization;
using Path = System.IO.Path;
using kuboPix.Helpers;
using kuboPix.Services;

namespace kuboPix.Views
{





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

        private bool _isNavigating;
        private bool _isLoaded;



        public AttractVideoView(
            string jwtToken,
            long shopLoginId,
            long printerId,
            string printerName,
            string qrUrl,
            string expiry,
            decimal printerPrice,
            int printerStatus,
            int printerPaymentEnabled)
        {
            InitializeComponent();

            _jwtToken = jwtToken;
            _shopLoginId = shopLoginId;
            _printerId = printerId;
            _printerName = printerName;
            _qrCodeData = qrUrl;
            _printerPrice = printerPrice;
            _printerPaymentEnabled = printerPaymentEnabled;
            _printerStatus = printerStatus;

            // Parse QR expiry safely.
            string expiryClean = expiry?.Replace(" UTC", "") ?? "";

            if (!DateTime.TryParse(
                expiryClean,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out DateTime parsedExpiry))
            {
                parsedExpiry = DateTime.UtcNow.AddMinutes(10);
            }

            _qrExpiry = parsedExpiry.ToUniversalTime();

            // Kiosk input handling.
            PreviewMouseDown += ReturnToQr;
            PreviewTouchDown += ReturnToQr;
            PreviewKeyDown += ReturnToQr;



            // Page lifecycle.
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            Focusable = true;
            Focus();
        }

        // ---------------------------------------------------------
        // PAGE LOADED
        // ---------------------------------------------------------

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded)
                return;

            _isLoaded = true;
            _isNavigating = false;

            try
            {
                KuboPixContentService.OnIdleVideoUpdated
                    -= HandleIdleVideoUpdated;

                KuboPixContentService.OnIdleVideoUpdated
                    += HandleIdleVideoUpdated;

                PlayCachedVideoIfExists();

                // Download/synchronize dynamic content if no video
                // is currently cached.
                if (GetCachedVideoPath() == null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await KuboPixContentService
                                .SyncContentAsync(_jwtToken);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Log(
                                "[VIDEO] Content sync error: " +
                                ex.Message);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log(
                    "[VIDEO] Load error: " + ex.Message);
            }
        }

        // ---------------------------------------------------------
        // PLAY DYNAMIC CACHED VIDEO
        // ---------------------------------------------------------

        private void PlayCachedVideoIfExists()
        {
            try
            {
                if (!_isLoaded || _isNavigating)
                    return;

                string? cachedPath = GetCachedVideoPath();

                if (string.IsNullOrWhiteSpace(cachedPath) ||
                    !File.Exists(cachedPath))
                {
                   

                    return;
                }

                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        try
                        {
                            if (!_isLoaded || _isNavigating)
                                return;

                         

                            LoopVideo.Stop();
                            LoopVideo.Source = new Uri(
                                cachedPath,
                                UriKind.Absolute);

                            LoopVideo.Position = TimeSpan.Zero;
                            LoopVideo.Play();
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Log(
                                "[VIDEO] Playback start error: " +
                                ex.Message);
                        }
                    }),
                    DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                AppLogger.Log(
                    "[VIDEO] Cached video error: " + ex.Message);
            }
        }

        // Called when the content service updates the idle video.
        private void HandleIdleVideoUpdated(string videoPath)
        {
            if (!_isLoaded || _isNavigating)
                return;

            PlayCachedVideoIfExists();
        }

        // ---------------------------------------------------------
        // VIDEO OPENED
        // ---------------------------------------------------------

        private void LoopVideo_MediaEnded(object? sender, RoutedEventArgs e)


        {
            try
            {
                if (!_isLoaded || _isNavigating)
                    return;

                LoopVideo.Position = TimeSpan.Zero;
                LoopVideo.Play();
            }
            catch (Exception ex)
            {
                AppLogger.Log(
                    "[VIDEO] Loop error: " + ex.Message);
            }
        }
        // ---------------------------------------------------------
        // DYNAMIC IDLE VIDEO SERVICE
        // ---------------------------------------------------------

        public static string? GetCachedVideoPath()
        {
            return KuboPixContentService.GetCachedIdleVideoPath();
        }

        public static async Task EnsureIdleVideoAsync(
            string? jwtToken = null)
        {
            string? token = jwtToken ??
                kuboPixSessionManager.Load()?.Token;

            if (!string.IsNullOrWhiteSpace(token))
            {
                await KuboPixContentService
                    .SyncContentAsync(token);
            }
        }

        // ---------------------------------------------------------
        // PAGE UNLOADED: CLEANUP
        // ---------------------------------------------------------

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;

            try
            {
                KuboPixContentService.OnIdleVideoUpdated
                    -= HandleIdleVideoUpdated;

                LoopVideo.Stop();
                LoopVideo.Close();
                LoopVideo.Source = null;

               
            }
            catch (Exception ex)
            {
                AppLogger.Log(
                    "[VIDEO] Cleanup error: " + ex.Message);
            }
        }

        // ---------------------------------------------------------
        // TOUCH / MOUSE / KEYBOARD NAVIGATION
        // ---------------------------------------------------------

        private void ReturnToQr(object? sender, EventArgs e)
        {
            if (_isNavigating)
                return;

            _isNavigating = true;

            if (e is RoutedEventArgs routedEventArgs)
                routedEventArgs.Handled = true;

            // Stop playback immediately when leaving this page.
            try
            {
                LoopVideo.Stop();
            }
            catch (Exception ex)
            {
                AppLogger.Log(
                    "[VIDEO] Stop before navigation error: " +
                    ex.Message);
            }

            // Prevent the same input from triggering navigation again.
            PreviewMouseDown -= ReturnToQr;
            PreviewTouchDown -= ReturnToQr;
            PreviewKeyDown -= ReturnToQr;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                  
                    NavigationService?.Navigate(new Capture());
                }
                catch (Exception ex)
                {
                    _isNavigating = false;

                    AppLogger.Log(
                        "[NAVIGATION] Capture navigation error: " +
                        ex.Message);
                }
            }), DispatcherPriority.Background);
        }

        private void LoopVideo_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            AppLogger.Log(
   "[VIDEO] Playback failed: " +
   (e.ErrorException?.Message ?? "Unknown error"));
        }
    }
    
}

