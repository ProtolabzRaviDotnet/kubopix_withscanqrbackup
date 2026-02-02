using MiniApp.Helpers;
using MiniApp.Services;
using MiniApp.ViewModels;
using MiniApp.Wrapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QRCoder;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace MiniApp.Views
{
    /// <summary>
    /// Interaction logic for ScanQrView.xaml
    /// </summary>
    public partial class ScanQrView : Page
    {
        // PRIVATE FIELDS
        private string _qrCodeData;
        private decimal _printerPrice;
        private string _jwtToken;
        private long _shopLoginId;
        private long _printerId;
        private string _printerName;
        private int _printerStatus;
        private int _printerPaymentEnabled;

        private DateTime _qrExpiry;

        private MiniApp.Services.OrderQueueService _orderQueue;

        private DispatcherTimer? _expiryTimer;

        // IDLE TIMER
        private DispatcherTimer? _idleTimer;
        private const int IdleSeconds = 300; // 5 minutes
        private DispatcherTimer? _printerStatusTimer;

        // CONSTRUCTOR
        public ScanQrView(string jwtToken, long shopLoginId, long printerId,string printerName, string qrUrl, decimal printerPrice,int printerStatus,string expiry,int printerPaymentEnabled)
        {
            InitializeComponent();

            _jwtToken = jwtToken;
            _shopLoginId = shopLoginId;
            _printerId = printerId;
            _printerName = printerName;
            _printerStatus = printerStatus;
            _printerPrice = printerPrice;

            _qrCodeData = qrUrl;
            _printerPaymentEnabled = printerPaymentEnabled;

            // Robust expiry parse
            DateTime parsedExpiry;
            string expiryClean = expiry?.Replace(" UTC", "") ?? "";
            if (!DateTime.TryParse(expiryClean, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsedExpiry))
            {
                AppLogger.Log($"Expiry parse failed for '{expiry}'. Defaulting to now+10min.");
                parsedExpiry = DateTime.UtcNow.AddMinutes(10);
            }
            _qrExpiry = parsedExpiry.ToUniversalTime();

            // Set up idle timer
            //SetupIdleTimer();

            // Event listeners for user input
            this.PreviewMouseDown += ResetIdleTimer;
            this.PreviewTouchDown += ResetIdleTimer;
            this.PreviewKeyDown += ResetIdleTimer;

            // Create Queue Service (DO NOT start here, start in Loaded)
            _orderQueue = new MiniApp.Services.OrderQueueService(
                _jwtToken,
                _shopLoginId,
                _printerId,
                _printerName,
                _printerStatus,
                _qrCodeData,
                _printerPrice,
                _qrExpiry,
                _printerPaymentEnabled
            );

            Loaded += ScanQrView_Loaded;
            Unloaded += ScanQrView_Unloaded;
        }

        // ADMIN EXIT BUTTON CLICK
        private async void AdminExitButton_Click(object sender, RoutedEventArgs e)
        {
            var passwordDialog = new PasswordDialog();
            passwordDialog.Owner = Window.GetWindow(this);

            bool? result = passwordDialog.ShowDialog();
            if (result != true)
                return;

            // ACTION BASED LOGIC 
            if (passwordDialog.SelectedAction == "EXIT")
            {
                // MAKE PRINTER OFFLINE BEFORE EXIT
                if (_printerId > 0 && _shopLoginId > 0)
                {
                    await OrderQueueService.SetPrinterOffline(
                        _printerId,
                        _shopLoginId
                    );
                }

                // Close only Mini-App
                Application.Current.MainWindow.Close();
            }
            else if (passwordDialog.SelectedAction == "SHUTDOWN")
            {
                // MAKE PRINTER OFFLINE BEFORE  SHUTDOWN
                if (_printerId > 0 && _shopLoginId > 0)
                {
                    await OrderQueueService.SetPrinterOffline(
                        _printerId,
                        _shopLoginId
                    );
                }

                // Shutdown Windows
                System.Diagnostics.Process.Start("shutdown", "/s /t 0");
            }
            else if (passwordDialog.SelectedAction == "MAINTENANCE")
            {
                var maintenancePage = new UnderMaintenancePage(false);
                NavigationService?.Navigate(maintenancePage);
            }
            else if (passwordDialog.SelectedAction == "LOGOUTPROFILE")
            {
                // MAKE PRINTER OFFLINE BEFORE EXIT
                if (_printerId > 0 && _shopLoginId > 0)
                {
                    await OrderQueueService.SetPrinterOffline(
                        _printerId,
                        _shopLoginId
                    );
                }

                MiniAppSessionManager.Clear();
                var LoginPage = new LoginView();
                NavigationService?.Navigate(LoginPage);
            }
            else if (passwordDialog.SelectedAction == "PRINT_FAILED_JOBS")
            {
                await RetryFailedPrintJobsAsync();
            }
        }

        // SCAN QR VIEW LOADED 
        private void ScanQrView_Loaded(object sender, RoutedEventArgs e)
        {
            _ = RefreshLivePrinterState();

            StartPrinterStatusPolling();
            GenerateQR(_qrCodeData);
            StartExpiryChecker();

            SetPriceText();

            // Start print queue - use the instance created in constructor (do not create duplicate)
            _orderQueue.StartQueue();

            // START IDLE TIMER
            SetupIdleTimer();

            // ANY INPUT RESETS TIMER
            this.PreviewMouseDown += ResetIdleTimer;
            this.PreviewTouchDown += ResetIdleTimer;
            this.PreviewKeyDown += ResetIdleTimer;
        }

        // SCAN QR VIEW UNLOADED
        private void ScanQrView_Unloaded(object sender, RoutedEventArgs e)
        {
            _printerStatusTimer?.Stop();
            _printerStatusTimer = null;

            _expiryTimer?.Stop();
            _idleTimer?.Stop();
        }

        // QR CODE GENERATION AND EXPIRY CHECKER
        private void GenerateQR(string data)
        {
            QRCodeGenerator qr = new QRCodeGenerator();
            QRCodeData qrData = qr.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
            QRCode code = new QRCode(qrData);

            using (Bitmap bmp = code.GetGraphic(20))
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);

                BitmapImage img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = new MemoryStream(ms.ToArray());
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();

                DisplayImage(img);
            }
        }

        // EXPIRY CHECKER SETUP
        private void StartExpiryChecker()
        {
            _expiryTimer = new DispatcherTimer();
            _expiryTimer.Interval = TimeSpan.FromSeconds(10);
            _expiryTimer.Tick += ExpiryTimer_Tick;
            _expiryTimer.Start();
        }

        // EXPIRY TIMER TICK
        private async void ExpiryTimer_Tick(object? sender, EventArgs e)
        {
            try
            {

                if (DateTime.UtcNow >= _qrExpiry)
                {
                    _expiryTimer?.Stop();

                    // QR expired → fetch new one
                    var qrDetails = await FetchQrDetailsAgain();

                    _qrCodeData = qrDetails.qrUrl;

                    DateTime parsedExpiry;
                    string expiryClean = qrDetails.expiry?.Replace(" UTC", "") ?? "";
                    if (!DateTime.TryParse(expiryClean, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsedExpiry))
                    {
                        AppLogger.Log($"Expiry parse failed for fetched qr '{qrDetails.expiry}'. Defaulting to now+10min.");
                        parsedExpiry = DateTime.UtcNow.AddMinutes(10);
                    }
                    _qrExpiry = parsedExpiry.ToUniversalTime();

                    // Refresh QR on screen
                    GenerateQR(_qrCodeData);

                    _expiryTimer?.Start(); // Start again
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"QR refresh failed: {ex}");
                _expiryTimer?.Start(); // Start again
            }
        }

        // FETCH QR DETAILS AGAIN
        private async Task<dynamic> FetchQrDetailsAgain()
        {
            return await LoginView.FetchQrDetailsForMiniApp(_printerId);
        }

        // ------------------ IDLE TIMER SETUP ------------------
        private void SetupIdleTimer()
        {
            _idleTimer = new DispatcherTimer();
            _idleTimer.Interval = TimeSpan.FromSeconds(IdleSeconds);
            _idleTimer.Tick += IdleTimer_Tick;
            _idleTimer.Start();
        }
        private void ResetIdleTimer(object? sender, EventArgs e)
        {
            _idleTimer?.Stop();
            _idleTimer?.Start();
        }
        private void IdleTimer_Tick(object? sender, EventArgs e)
        {
            _idleTimer?.Stop();  // Stop the idle timer when it triggers

            // Check if NavigationService is initialized
            if (NavigationService != null)
            {
                // Navigate to AttractVideoView
                NavigationService.Navigate(new AttractVideoView(
                    _jwtToken,
                    _shopLoginId,
                    _printerId,
                    _printerName,
                    _qrCodeData,
                    _qrExpiry.ToString("yyyy-MM-dd HH:mm:ss"),
                    _printerPrice,
                    _printerStatus,
                    _printerPaymentEnabled
                ));
            }
            else
            {
                // Log error if NavigationService is not initialized or null
                //AppLogger.Log("NavigationService is not initialized, cannot navigate to AttractVideoView.");
            }
        }

        // DISPLAY IMAGE
        private void DisplayImage(BitmapImage img)
        {
            qrDynamicImage1.Source = img;
        }

        // SET PRICE TEXT
        private void SetPriceText()
        {
            if (_printerPaymentEnabled == 1)
            {
                decimal price = _printerPrice;

                priceValueText.Text = price % 1 == 0
                    ? $"{price:0}.-"
                    : $"{price:0.00}";

                priceText.Visibility = Visibility.Visible;
                starImage.Visibility = Visibility.Visible;
            }
            else
            {
                priceText.Visibility = Visibility.Collapsed;
                starImage.Visibility = Visibility.Collapsed;
            }
        }

        // CONTINUE BUTTON CLICK (IF NEEDED)
        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            // intentionally left blank or navigate to PrintProgress if required
        }

        // LOGO CLICK - TRIGGERS ADMIN EXIT
        private void Logo_Click(object sender, MouseButtonEventArgs e)
        {
            AdminExitButton_Click(sender, e);  // Trigger the existing exit logic when the logo is clicked
        }

        // PRINTER STATUS POLLING AND UI UPDATES
        private void StartPrinterStatusPolling()
        {
            _printerStatusTimer = new DispatcherTimer();
            _printerStatusTimer.Interval = TimeSpan.FromSeconds(3); // every 3 sec
            _printerStatusTimer.Tick += (s, e) =>
            {
                UpdatePrinterStatusFromSDK();
            };
            _printerStatusTimer.Start();
        }

        // UPDATE PRINTER STATUS FROM SDK
        private async void UpdatePrinterStatusFromSDK()
        {
            Int64 printerId = LoginView.GetCurrentSessionPrinterId();
            Int64 shoploginId = LoginView.GetCurrentSessionShopLoginId();

            try
            {
                string printerName = LoginView.GetCurrentSessionPrinterName() ?? string.Empty;

                if (string.IsNullOrEmpty(printerName))
                {
                    //var ErrorPage = new PrintErrorView("Printer not found. Please ensure the printer is connected and try again.");
                    var ErrorPage = new PrintErrorView(
                               "Printer not found. Please ensure the printer is connected and try again.",
                               null,   // failedOrder
                               null,   // orderPaymentTypeId
                               true    // isSdkPrinterError
                           );

                    NavigationService?.Navigate(ErrorPage);
                    return;
                }

                // DLL raw status & warning
                string status = SeaoryPrinterPrint.CheckPrinterStatus(printerName);
                string warning = SeaoryPrinterPrint.CheckPrinterWarning(printerName);


                // Check if printer is busy (0x000000AA) → show Busy 
                if ((!string.IsNullOrEmpty(status) && status.Contains("0x000000AA")) ||
             (!string.IsNullOrEmpty(warning) && warning.Contains("0x000000AA")))
                {
                    SetPrinterBusyUI(); 
                    return; 
                }

                // Use existing _orderQueue instance instead of creating new one
                string parsedMessage = _orderQueue.GetMessageFromErrorCodes(status, warning) ?? "";

                // Determine UI status
                if (status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                {
                    SetPrinterReadyUI();
                }
                else
                {
                    SetPrinterNotReadyUI(parsedMessage);

                    if (printerId > 0 && shoploginId > 0)
                    {
                        await OrderQueueService.SetPrinterOffline(
                            printerId,
                            shoploginId
                        );
                    }

                    //var ErrorPage = new PrintErrorView(parsedMessage);
                    var ErrorPage = new PrintErrorView(
                    parsedMessage,
                    null,   // failedOrder
                    null,   // orderPaymentTypeId
                    true    // isSdkPrinterError
                );
                    NavigationService?.Navigate(ErrorPage);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error updating printer status: {ex.Message}");
                SetPrinterNotReadyUI("Not Connected");

                if (printerId > 0 && shoploginId > 0)
                {
                    await OrderQueueService.SetPrinterOffline(
                        printerId,
                        shoploginId
                    );
                }

                // Exception occurred → also go to login page

                //var ErrorPage = new PrintErrorView($"Error updating printer status: {ex.Message}");
                // SDK Printer error → flag = true
                var ErrorPage = new PrintErrorView(
                    $"Error updating printer status: {ex.Message}",
                    null,
                    null,
                    true
                );
                NavigationService?.Navigate(ErrorPage);
            }
        }

        // PRINTER STATUS UI UPDATES
        private void SetPrinterReadyUI()
        {
            PrinterStatusText.Text = "READY";
            PrinterStatusText.Foreground = Brushes.LimeGreen;

            PrinterStatusLight.Fill = Brushes.LimeGreen;
            PrinterStatusLight.Effect = new DropShadowEffect
            {
                Color = Colors.LimeGreen,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.9
            };

            PrinterStatusBorder.Background =
                new SolidColorBrush(Color.FromArgb(50, 0, 255, 0));
        }

        // PRINTER NOT READY UI
        private void SetPrinterNotReadyUI(string message)
        {
            PrinterStatusText.Text = message;
            PrinterStatusText.Foreground = Brushes.Red;

            PrinterStatusLight.Fill = Brushes.Red;
            PrinterStatusLight.Effect = new DropShadowEffect
            {
                Color = Colors.Red,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.9
            };

            PrinterStatusBorder.Background =
                new SolidColorBrush(Color.FromArgb(50, 255, 0, 0));
        }

        // PRINTER BUSY UI
        private void SetPrinterBusyUI()
        {
            PrinterStatusText.Text = "BUSY";
            PrinterStatusText.Foreground = Brushes.Goldenrod; // yellow text

            PrinterStatusLight.Fill = Brushes.Goldenrod;
            PrinterStatusLight.Effect = new DropShadowEffect
            {
                Color = Colors.Goldenrod,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.9
            };

            PrinterStatusBorder.Background =
                new SolidColorBrush(Color.FromArgb(50, 255, 215, 0)); // light yellow background
        }

        // LOG OUT BUTTON CLICK
        private void LogOutButton_TouchDown(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            LogOutButton_Click_Login(sender, new RoutedEventArgs(Button.ClickEvent));
        }

        // LOG OUT BUTTON CLICK HANDLER
        private async void LogOutButton_Click_Login(object sender, RoutedEventArgs e)
        {
            var dialog = new ConfirmLogOutDialog();
            dialog.Owner = Window.GetWindow(this);

            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                MiniAppSessionManager.Clear();

                if (_printerId > 0 && _shopLoginId > 0)
                {
                    await OrderQueueService.SetPrinterOffline(
                        _printerId,
                        _shopLoginId
                    );
                }

                var LoginPage = new LoginView();
                NavigationService?.Navigate(LoginPage);
            }
        }

        // RETRY FAILED PRINT JOBS --Last Order Print Only
        private async Task RetryFailedPrintJobsAsync()
        {
            try
            {
                var lastJob = FailedPrintSession.GetAll()?.LastOrDefault();

                if (lastJob == null)
                {
                    AppLogger.Log("No failed print jobs available.");
                    return;
                }

                // SAFETY CHECK — BACKEND REFUND PROTECTION
                if (lastJob.OrderStatus == 3 && lastJob.OrderPaymentTypeId == 3) // 3 = Refunded // OrderPaymentTypeId 3 = Twint
                {
                    FailedPrintSession.Remove(lastJob.OrderId);
                    AppLogger.Log(
                        $"Order {lastJob.OrderId} already refunded by backend. Retry blocked."
                    );
                    return;
                }

                AppLogger.Log($"Retrying LAST failed Order {lastJob.OrderId}");


                // success/fail
                var isSuccess = await _orderQueue.ProcessSingleOrderAsync(lastJob, isRetry: true);

                if (isSuccess)
                {
                    FailedPrintSession.Remove(lastJob.OrderId);
                    AppLogger.Log($"Retry completed successfully for LAST failed Order {lastJob.OrderId}");
                }
                else
                {
                    AppLogger.Log($"Retry FAILED again for Order {lastJob.OrderId}, keeping it in FailedPrintSession");
                }

            }
            catch (Exception ex)
            {
                AppLogger.Log($"RetryFailedPrintJobsAsync Exception: {ex}");
            }
        }

        // Refresh Live Printer State
        private async Task RefreshLivePrinterState()
        {
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _jwtToken);

                HttpResponseMessage resp = await client.GetAsync(
                    $"{AppConfig.ApiBaseUrl}api/get/printerstate?shopLoginId={_shopLoginId}&printerId={_printerId}");

                if (!resp.IsSuccessStatusCode)
                    return;

                string json = await resp.Content.ReadAsStringAsync();

                JObject? result = JsonConvert.DeserializeObject<JObject>(json);
                if (result == null)
                    return;

                if ((int?)result["status"] != 1)
                    return;

                JToken? dataToken = result["data"];
                if (dataToken == null)
                    return;

                PrinterState? printer =
                    dataToken.ToObject<PrinterState>();

                if (printer == null)
                    return;

                _printerPrice = printer.PrinterPrice;
                _printerPaymentEnabled = printer.PrinterPaymentEnabled;
                _printerStatus = printer.PrinterStatus;
                _printerName = printer.PrinterName;
            }
            catch (Exception ex)
            {
                AppLogger.Log("RefreshLivePrinterState error: " + ex.Message);
            }
        }


    }
}
