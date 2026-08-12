using com.epson.label.driver;
using kuboPix.Helpers;
using kuboPix.Services;
using kuboPix.ViewModels;
using kuboPix.Wrapper;
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

namespace kuboPix.Views
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

        private kuboPix.Services.OrderQueueService _orderQueue;

        private DispatcherTimer? _expiryTimer;

        // IDLE TIMER
        private DispatcherTimer? _idleTimer;
        private const int IdleSeconds = 300; // 5 minutes
        private DispatcherTimer? _printerStatusTimer;

        private bool _isFetchingQr = false;
        private int _lastReportedStatus = -1; // -1 means unknown/unreported status on startup


        // CONSTRUCTOR
        public ScanQrView(string jwtToken, long shopLoginId, long printerId, string printerName, string qrUrl, decimal printerPrice, int printerStatus, string expiry, int printerPaymentEnabled)
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
            _orderQueue = new kuboPix.Services.OrderQueueService(
                _jwtToken,
                _shopLoginId,
                _printerId,
                LoginView.GetCurrentSessionPrinterName() ?? _printerName,
                _printerStatus,
                _qrCodeData,
                _printerPrice,
                _qrExpiry,
                _printerPaymentEnabled,
                LoginView.SelectedPrinterBrand
            );

            Loaded += ScanQrView_Loaded;
            Unloaded += ScanQrView_Unloaded;
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
        }

        // SCAN QR VIEW UNLOADED
        private void ScanQrView_Unloaded(object sender, RoutedEventArgs e)
        {
            _printerStatusTimer?.Stop();
            _printerStatusTimer = null;

            _expiryTimer?.Stop();
            _idleTimer?.Stop();

            // Stop this view's order-queue + printer-monitor loops so they don't leak.
            // Each ScanQrView owns its own OrderQueueService; without this, every new
            // ScanQrView (created after each print / screensaver return) left the previous
            // queue running forever → N concurrent polling loops sharing static state.
            // Cancelling here does not abort an in-flight print (the print awaits don't
            // honor the token); the current order finishes and the fresh ScanQrView on
            // return starts exactly one new queue.
            _orderQueue?.StopQueue();
        }

        // QR CODE GENERATION AND EXPIRY CHECKER
        private void GenerateQR(string data)
        {
            try
            {
                //AppLogger.Log($"[QR] Raw data: '{data}'");

                if (string.IsNullOrWhiteSpace(data))
                {
                    AppLogger.Log("[QR] Skipped: null/empty");
                    return;
                }

                data = data.Trim();

                if (data.Length < 10 ||
                    data.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                    data.Equals("undefined", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Log($"[QR] Skipped: invalid data -> '{data}'");
                    return;
                }

                QRCodeGenerator qrGenerator = new QRCodeGenerator();
                QRCodeData qrData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);

                if (qrData == null || qrData.ModuleMatrix == null || qrData.ModuleMatrix.Count == 0)
                {
                    AppLogger.Log("[QR] Failed: empty matrix");
                    return;
                }

                using (QRCode code = new QRCode(qrData))
                using (Bitmap bmp = code.GetGraphic(10))
                {
                    if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0)
                    {
                        AppLogger.Log("[QR] Failed: invalid bitmap");
                        return;
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);

                        if (ms.Length == 0)
                        {
                            AppLogger.Log("[QR] Failed: empty stream");
                            return;
                        }

                        BitmapImage img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.StreamSource = new MemoryStream(ms.ToArray());
                        img.EndInit();
                        img.Freeze();

                        DisplayImage(img);

                        //AppLogger.Log("[QR] SUCCESS");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[QR] ERROR: {ex.Message}");
                AppLogger.Log($"[QR] STACK: {ex.StackTrace}");
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
            if (_isFetchingQr) return; //  prevent parallel calls

            try
            {
                if (DateTime.UtcNow >= _qrExpiry)
                {
                    _isFetchingQr = true;

                    //AppLogger.Log("[QR REFRESH] Expired — fetching new QR");

                    _expiryTimer?.Stop();

                    var qrDetails = await FetchQrDetailsAgain();

                    //AppLogger.Log($"[QR REFRESH] API qrUrl: '{qrDetails?.qrUrl}'");
                    //AppLogger.Log($"[QR REFRESH] API expiry: '{qrDetails?.expiry}'");

                    string? newQr = qrDetails?.qrUrl?.Trim();

                    // VALID QR
                    if (!string.IsNullOrWhiteSpace(newQr) &&
                        newQr.Length > 10 &&
                        !newQr.Equals("null", StringComparison.OrdinalIgnoreCase))
                    {
                        _qrCodeData = newQr;

                        //AppLogger.Log("[QR REFRESH] New QR accepted");

                        GenerateQR(_qrCodeData);

                        // expiry update
                        DateTime parsedExpiry;
                        string expiryClean = qrDetails?.expiry?.Replace(" UTC", "") ?? "";

                        if (!DateTime.TryParse(
                                expiryClean,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                out parsedExpiry))
                        {
                            AppLogger.Log($"[QR REFRESH] Expiry parse failed: '{qrDetails?.expiry}'");
                            parsedExpiry = DateTime.UtcNow.AddMinutes(10);
                        }

                        _qrExpiry = parsedExpiry.ToUniversalTime();

                        //AppLogger.Log($"[QR REFRESH] Next expiry set: {_qrExpiry}");

                        _expiryTimer!.Interval = TimeSpan.FromSeconds(10); // back to normal
                    }
                    else
                    {
                        // INVALID → FAST RETRY
                        //AppLogger.Log("[QR REFRESH] Invalid QR → retry in 2 sec");

                        _expiryTimer!.Interval = TimeSpan.FromSeconds(2);
                    }

                    _expiryTimer?.Start();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[QR REFRESH] ERROR: {ex.Message}");
                AppLogger.Log($"[QR REFRESH] STACK: {ex.StackTrace}");

                // ERROR → FAST RETRY
                if (_expiryTimer != null)
                {
                    _expiryTimer.Interval = TimeSpan.FromSeconds(2);
                    _expiryTimer.Start();
                }
            }
            finally
            {
                _isFetchingQr = false; // release lock
            }
        }

        // FETCH QR DETAILS AGAIN
        private async Task<dynamic> FetchQrDetailsAgain()
        {
            return await LoginView.FetchQrDetailsForkuboPix(_printerId);
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
                string status = "";
                string warning = "";
                string parsedMessage = "";

                var session = kuboPixSessionManager.Load();
                string brand = session?.PrinterBrand ?? LoginView.SelectedPrinterBrand ?? "Seaory";

                if (brand == "Epson")
                {
                    StatusCode sc = StatusCode.ST_Idle;
                    ErrorCode ec = ErrorCode.ERR_FatalError;
                    WARNING_INFO wc = new WARNING_INFO();
                    bool queryOk = false;

                    ENS2Control ensControl = ENS2Control.GetInstance();
                    try
                    {
                        ensControl.Initialize(printerName, "");
                        sc = ensControl.GetStatusInformation();
                        ec = ensControl.GetErrorInformation();
                        wc = ensControl.GetWarningInformation();
                        queryOk = true;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"Epson ScanQrView query error: {ex.Message}");
                    }
                    finally
                    {
                        ensControl.Release();
                    }

                    if (queryOk)
                    {
                        if (sc == StatusCode.ST_Busy || sc == StatusCode.ST_Wait || sc == StatusCode.ST_Cleaning || sc == StatusCode.ST_TestPrint)
                        {
                            SetPrinterBusyUI();
                            return;
                        }
                        status = (sc == StatusCode.ST_Idle) ? "Ready" : sc.ToString().Replace("ST_", "");
                        parsedMessage = _orderQueue.GetEpsonMessageFromErrorCodes(sc, ec, wc) ?? "";
                        warning = $"InkLow={wc.InkLowWarning}, Other={wc.OtherWarning}";
                    }

                    else
                    {
                        status = "Error";
                        parsedMessage = "Printer connection failed";
                    }
                }
                else
                {
                    status = SeaoryPrinterPrint.CheckPrinterStatus(printerName);
                    warning = SeaoryPrinterPrint.CheckPrinterWarning(printerName);
                    parsedMessage = _orderQueue.GetMessageFromErrorCodes(status, warning) ?? "";

                    // Check if printer is busy (0x000000AA) → show Busy 
                    if ((!string.IsNullOrEmpty(status) && status.Contains("0x000000AA")) ||
                        (!string.IsNullOrEmpty(warning) && warning.Contains("0x000000AA")))
                    {
                        SetPrinterBusyUI();
                        return;
                    }
                }

                // Determine UI status
                if (status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                {
                    SetPrinterReadyUI();

                    if (_lastReportedStatus != 1)
                    {
                        _lastReportedStatus = 1;
                        if (printerId > 0 && shoploginId > 0)
                        {
                            try
                            {
                                using (HttpClient client = new HttpClient())
                                {
                                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
                                    string url = $"{AppConfig.ApiBaseUrl}api/update/printerstatus?printerId={printerId}&shopLoginId={shoploginId}&status=1&message={Uri.EscapeDataString("Your printer is online and ready for printing.")}";
                                    await client.GetAsync(url);
                                }
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Log($"Error reporting printer online to server: {ex.Message}");
                            }
                        }
                    }
                }
                else
                {
                    SetPrinterNotReadyUI(parsedMessage);

                    if (_lastReportedStatus != 0)
                    {
                        _lastReportedStatus = 0;
                        if (printerId > 0 && shoploginId > 0)
                        {
                            await OrderQueueService.SetPrinterOffline(
                                printerId,
                                shoploginId
                            );
                        }
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

                if (_lastReportedStatus != 0)
                {
                    _lastReportedStatus = 0;
                    if (printerId > 0 && shoploginId > 0)
                    {
                        await OrderQueueService.SetPrinterOffline(
                            printerId,
                            shoploginId
                        );
                    }
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
                kuboPixSessionManager.Clear();

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

        // Cancel Button Click
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(new Capture());
        }

    }
}
