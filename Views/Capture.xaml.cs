using System;
using System.Collections.Generic;
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
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using com.epson.label.driver;
using kuboPix.Helpers;
using kuboPix.ViewModels;
using kuboPix.Services;
using kuboPix.Wrapper;

namespace kuboPix.Views
{
    /// <summary>
    /// Interaction logic for Capture.xaml
    /// </summary>
    public partial class Capture : Page
    {
        private string _pendingAction = "";
        private DispatcherTimer? _idleTimer;
        private const int IdleSeconds = 300; // 5 minutes

        // Printer status polling
        private DispatcherTimer? _printerStatusTimer;
        private int _lastReportedStatus = -1; // -1 = unknown on startup
        private DateTime _lastStatusReportTime = DateTime.MinValue;
        private OrderQueueService? _statusHelper;

        public Capture()
        {
            InitializeComponent();
            txtAppVersion.Text = AppConfig.AppVersion;

            // Event listeners for user input to reset idle timer
            this.PreviewMouseDown += ResetIdleTimer;
            this.PreviewTouchDown += ResetIdleTimer;
            this.PreviewKeyDown += ResetIdleTimer;

            this.Loaded += Capture_Loaded;
            this.Unloaded += Capture_Unloaded;
        }

        private void Capture_Loaded(object sender, RoutedEventArgs e)
        {
            SetupIdleTimer();

            // Load and listen for dynamic start screen background (ContentTypeId 1)
            LoadStartBackground();
            KuboPixContentService.OnStartBackgroundUpdated += ContentService_OnStartBackgroundUpdated;

            // Sync dynamic content (StartBackground, IdleVideo, PrivacyPolicy) in background
            var session = kuboPixSessionManager.Load();
            if (session != null && !string.IsNullOrWhiteSpace(session.Token))
            {
                _ = KuboPixContentService.SyncContentAsync(session.Token);
            }
            else
            {
                _ = AttractVideoView.EnsureIdleVideoAsync();
            }

            // Start polling printer status so errors (cover open, jam, offline)
            // are detected and shown to the user immediately on the Capture page,
            // and the web panel is updated with the current ready / not ready status.
            StartPrinterStatusPolling();
        }

        private void Capture_Unloaded(object sender, RoutedEventArgs e)
        {
            _idleTimer?.Stop();
            KuboPixContentService.OnStartBackgroundUpdated -= ContentService_OnStartBackgroundUpdated;
            StopPrinterStatusPolling();
        }

        private void LoadStartBackground()
        {
            try
            {
                var cachedBg = KuboPixContentService.GetCachedStartBackground();
                if (cachedBg != null)
                {
                    imgStartBackground.Source = cachedBg;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[Capture] Error applying start background: {ex.Message}");
            }
        }

        private void ContentService_OnStartBackgroundUpdated(BitmapImage? newBg)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    imgStartBackground.Source = newBg;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[Capture] Error updating start background from event: {ex.Message}");
            }
        }

        // ─── PRINTER STATUS POLLING ──────────────────────────────────────────────

        private void StartPrinterStatusPolling()
        {
            // Immediate check on startup/navigation to Capture
            _ = CheckPrinterStatusAsync();

            _printerStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3) // check SDK every 3 seconds
            };
            _printerStatusTimer.Tick += (s, e) => _ = CheckPrinterStatusAsync();
            _printerStatusTimer.Start();
        }

        private void StopPrinterStatusPolling()
        {
            _printerStatusTimer?.Stop();
            _printerStatusTimer = null;
        }

        private async Task CheckPrinterStatusAsync()
        {
            try
            {
                var session = kuboPixSessionManager.Load();
                if (session == null) return;

                long printerId = session.PrinterId > 0 ? session.PrinterId : LoginView.GetCurrentSessionPrinterId();
                long shopLoginId = session.ShopLoginId > 0 ? session.ShopLoginId : LoginView.GetCurrentSessionShopLoginId();
                string token = session.Token;

                string printerName = LoginView.GetCurrentSessionPrinterName()
                                     ?? session.PrinterName
                                     ?? string.Empty;
                if (string.IsNullOrEmpty(printerName)) return;

                string brand = session.PrinterBrand
                               ?? LoginView.SelectedPrinterBrand
                               ?? "Seaory";

                string status = "";
                string parsedMessage = "";

                if (_statusHelper == null)
                {
                    _statusHelper = new OrderQueueService(
                        token, shopLoginId, printerId,
                        printerName, session.PrinterStatus, "", session.PrinterPrice,
                        DateTime.UtcNow, session.PrinterPaymentEnabled, brand);
                }

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
                        ensControl.GetPrinterFullStatus(out sc, out ec, out wc);
                        queryOk = true;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"[Capture] Epson status query error: {ex.Message}");
                    }
                    finally
                    {
                        try { ensControl.Release(); } catch { }
                    }

                    if (queryOk)
                    {
                        // Busy = printer working, not an error
                        if (sc == StatusCode.ST_Busy || sc == StatusCode.ST_Wait ||
                            sc == StatusCode.ST_Cleaning || sc == StatusCode.ST_TestPrint)
                            return;

                        status = (sc == StatusCode.ST_Idle) ? "Ready" : sc.ToString().Replace("ST_", "");
                        parsedMessage = _statusHelper.GetEpsonMessageFromErrorCodes(sc, ec, wc) ?? "";
                    }
                    else
                    {
                        status = "Error";
                        parsedMessage = "Printer connection failed. Please check the printer cable and power.";
                    }
                }
                else // Seaory
                {
                    status = SeaoryPrinterPrint.CheckPrinterStatus(printerName);
                    string warning = SeaoryPrinterPrint.CheckPrinterWarning(printerName);

                    // Busy = printer working, not an error
                    if ((!string.IsNullOrEmpty(status) && status.Contains("0x000000AA")) ||
                        (!string.IsNullOrEmpty(warning) && warning.Contains("0x000000AA")))
                        return;

                    parsedMessage = _statusHelper.GetMessageFromErrorCodes(status, warning) ?? "";
                }

                bool isReady = status.Equals("Ready", StringComparison.OrdinalIgnoreCase);

                if (isReady)
                {
                    // Report online on startup / state change, or heartbeat every 45s to keep web panel Kiosk Status online
                    if (_lastReportedStatus != 1 || (DateTime.UtcNow - _lastStatusReportTime) >= TimeSpan.FromSeconds(45))
                    {
                        _lastReportedStatus = 1;
                        _lastStatusReportTime = DateTime.UtcNow;

                        if (printerId > 0 && shopLoginId > 0)
                        {
                            try
                            {
                                await UpdatePrinterStatus(printerId, shopLoginId, 1, "Your printer is online and ready for printing.", token);

                                // 2. Touch pending orders — this is what the backend kiosk monitor uses to clear IsOffline to 0
                                using var client = new HttpClient();
                                if (!string.IsNullOrWhiteSpace(token))
                                    client.DefaultRequestHeaders.Authorization =
                                        new AuthenticationHeaderValue("Bearer", token);
                                string pendingUrl = $"{AppConfig.ApiBaseUrl}api/get/pendingorders?shopLoginId={shopLoginId}&printerId={printerId}";
                                await client.GetAsync(pendingUrl);
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Log($"[Capture] Error reporting printer online to server: {ex.Message}");
                            }
                        }
                    }
                }
                else
                {
                    // Printer has an error (cover open, jam, offline, etc.)
                    // Report dynamic error status code to server and navigate to PrintErrorView
                    int errStatusCode = _statusHelper?.CurrentPrinterStatus ?? 0;
                    if (errStatusCode == 1)
                    {
                        errStatusCode = 0;
                    }

                    if (_lastReportedStatus != errStatusCode)
                    {
                        _lastReportedStatus = errStatusCode;
                        _lastStatusReportTime = DateTime.UtcNow;
                        AppLogger.Log($"[Capture] Printer error detected: {parsedMessage} (status={errStatusCode}). Reporting to server and navigating to PrintErrorView.");

                        if (printerId > 0 && shopLoginId > 0)
                        {
                            await UpdatePrinterStatus(printerId, shopLoginId, errStatusCode, string.IsNullOrWhiteSpace(parsedMessage) ? "Printer not ready" : parsedMessage, token);
                        }

                        StopPrinterStatusPolling(); // stop before navigating away

                        NavigationService?.Navigate(new PrintErrorView(
                            parsedMessage,
                            null,           // no failed order
                            null,           // no payment type
                            true            // isSdkPrinterError = true → shows minimal UI
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[Capture] CheckPrinterStatusAsync error: {ex.Message}");

                int catchStatusCode = _statusHelper?.CurrentPrinterStatus ?? 0;
                if (catchStatusCode == 1)
                {
                    catchStatusCode = 0;
                }

                if (_lastReportedStatus != catchStatusCode)
                {
                    _lastReportedStatus = catchStatusCode;
                    _lastStatusReportTime = DateTime.UtcNow;
                    try
                    {
                        var session = kuboPixSessionManager.Load();
                        long printerId = (session != null && session.PrinterId > 0) ? session.PrinterId : LoginView.GetCurrentSessionPrinterId();
                        long shopLoginId = (session != null && session.ShopLoginId > 0) ? session.ShopLoginId : LoginView.GetCurrentSessionShopLoginId();
                        string token = session?.Token ?? "";
                        if (printerId > 0 && shopLoginId > 0)
                        {
                            await UpdatePrinterStatus(printerId, shopLoginId, catchStatusCode, ex.Message, token);
                        }
                    }
                    catch { }
                }
            }
        }

        private async Task UpdatePrinterStatus(long printerId, long shopLoginId, int statusCode, string message, string token = "")
        {
            if (printerId <= 0 || shopLoginId <= 0) return;

            if (_statusHelper != null)
            {
                await _statusHelper.UpdatePrinterStatus(printerId, shopLoginId, statusCode, message);
            }
            else
            {
                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        if (!string.IsNullOrWhiteSpace(token))
                            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        string url = $"{AppConfig.ApiBaseUrl}api/update/printerstatus?printerId={printerId}&shopLoginId={shopLoginId}&status={statusCode}&message={Uri.EscapeDataString(message ?? "")}";
                        await client.GetAsync(url);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[Capture] Error updating printer status: {ex.Message}");
                }
            }
        }

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
            _idleTimer?.Stop();

            if (NavigationService != null)
            {
                var session = kuboPixSessionManager.Load();
                if (session != null)
                {
                    NavigationService.Navigate(new AttractVideoView(
                        session.Token,
                        session.ShopLoginId,
                        session.PrinterId,
                        session.PrinterName ?? "",
                        "", // qrUrl empty because we are in Capture and haven't fetched it yet
                        "", // expiry empty
                        session.PrinterPrice,
                        session.PrinterStatus,
                        session.PrinterPaymentEnabled
                    ));
                }
                else
                {
                    NavigationService.Navigate(new AttractVideoView("", 0, 0, "", "", "", 0, 0, 0));
                }
            }
        }

        private void TakeLivePhoto_Click(object sender, RoutedEventArgs e)
        {
            _pendingAction = "Camera";
            TermsPopupOverlay.Visibility = Visibility.Visible;
        }

        private void UploadFromPhone_Click(object sender, RoutedEventArgs e)
        {
            _pendingAction = "Upload";
            TermsPopupOverlay.Visibility = Visibility.Visible;
        }

        private void CancelTerms_Click(object sender, RoutedEventArgs e)
        {
            TermsPopupOverlay.Visibility = Visibility.Collapsed;
        }

        private async void AcceptTerms_Click(object sender, RoutedEventArgs e)
        {
            TermsPopupOverlay.Visibility = Visibility.Collapsed;

            if (_pendingAction == "Camera")
            {
                try
                {
                    var session = kuboPixSessionManager.Load();
                    if (session != null && !string.IsNullOrEmpty(session.Token))
                    {
                        var printerState = await LoginView.GetLivePrinterState(session.Token, session.ShopLoginId, session.PrinterId);
                        if (printerState != null)
                        {
                            session.PrinterPaymentEnabled = printerState.PrinterPaymentEnabled;
                            session.PrinterPrice = printerState.PrinterPrice;
                            session.PrinterStatus = printerState.PrinterStatus;
                            kuboPixSessionManager.Save(session);
                            LoginView.SetCurrentSessionPrinterPaymentEnabled(printerState.PrinterPaymentEnabled);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Capture Camera sync printerState error: " + ex.Message);
                }

                NavigationService?.Navigate(new Camera());
            }
            else if (_pendingAction == "Upload")
            {
                try
                {
                    var session = kuboPixSessionManager.Load();
                    if (session == null)
                    {
                        MessageBox.Show("No active session found. Please log in again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    dynamic? qrDetails = await LoginView.FetchQrDetailsForkuboPix(session.PrinterId);
                    if (qrDetails == null || qrDetails is bool || qrDetails?.ToString() == "False")
                    {
                        MessageBox.Show("Failed to fetch QR details from server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var printerState = await LoginView.GetLivePrinterState(session.Token, session.ShopLoginId, session.PrinterId);
                    if (printerState == null)
                    {
                        MessageBox.Show("Failed to check printer state from server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Sync updated printer settings to session
                    session.PrinterPaymentEnabled = printerState.PrinterPaymentEnabled;
                    session.PrinterPrice = printerState.PrinterPrice;
                    session.PrinterStatus = printerState.PrinterStatus;
                    kuboPixSessionManager.Save(session);
                    LoginView.SetCurrentSessionPrinterPaymentEnabled(printerState.PrinterPaymentEnabled);

                    string qrUrl = (string)(qrDetails?.qrUrl ?? "");
                    string expiry = (string)(qrDetails?.expiry ?? "");

                    NavigationService?.Navigate(
                        new ScanQrView(
                            session.Token,
                            session.ShopLoginId,
                            session.PrinterId,
                            printerState.PrinterName,
                            qrUrl,
                            printerState.PrinterPrice,
                            printerState.PrinterStatus,
                            expiry,
                            printerState.PrinterPaymentEnabled
                        )
                    );
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading upload page: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void TermsLink_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(new TermsAndConditions());
        }

        // LOGO CLICK - TRIGGERS ADMIN EXIT
        private void Logo_Click(object sender, MouseButtonEventArgs e)
        {
            AdminExitButton_Click(sender, e);
        }

        // ADMIN EXIT BUTTON CLICK
        private async void AdminExitButton_Click(object sender, RoutedEventArgs e)
        {
            var passwordDialog = new PasswordDialog();
            passwordDialog.Owner = Window.GetWindow(this);
            kuboPix.Helpers.DialogScaler.CenterOnElement(passwordDialog, this);

            bool? result = passwordDialog.ShowDialog();
            if (result != true)
                return;

            var session = kuboPixSessionManager.Load();
            long printerId = session?.PrinterId ?? 0;
            long shopLoginId = session?.ShopLoginId ?? 0;

            // ACTION BASED LOGIC 
            if (passwordDialog.SelectedAction == "EXIT")
            {
                // MAKE PRINTER OFFLINE BEFORE EXIT
                if (printerId > 0 && shopLoginId > 0)
                {
                    await OrderQueueService.SetPrinterOffline(
                        printerId,
                        shopLoginId
                    );
                }

                // Close only Mini-App
                Application.Current.MainWindow.Close();
            }
            else if (passwordDialog.SelectedAction == "SHUTDOWN")
            {
                // MAKE PRINTER OFFLINE BEFORE SHUTDOWN
                if (printerId > 0 && shopLoginId > 0)
                {
                    await OrderQueueService.SetPrinterOffline(
                        printerId,
                        shopLoginId
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
                if (printerId > 0 && shopLoginId > 0)
                {
                    await OrderQueueService.SetPrinterOffline(
                        printerId,
                        shopLoginId
                    );
                }

                kuboPixSessionManager.Clear();
                var LoginPage = new LoginView();
                NavigationService?.Navigate(LoginPage);
            }
            else if (passwordDialog.SelectedAction == "PRINT_FAILED_JOBS")
            {
                await RetryFailedPrintJobsAsync();
            }
        }

        // RETRY FAILED PRINT JOBS --Last Order Print Only
        private async Task RetryFailedPrintJobsAsync()
        {
            try
            {
                var session = kuboPixSessionManager.Load();
                if (session == null)
                {
                    AppLogger.Log("No active session found for retry.");
                    return;
                }

                var lastJob = FailedPrintSession.GetAll()?.LastOrDefault();

                if (lastJob == null)
                {
                    AppLogger.Log("No failed print jobs available.");
                    return;
                }

                // Await the async API call
                var orderDetail = await LoginView.GetOrderDetailByOrderId(lastJob.OrderId);

                if (orderDetail == null)
                {
                    AppLogger.Log($"Order details not found for Order ID {lastJob.OrderId}. Retry aborted.");
                    return;
                }

                // SAFETY CHECK — BACKEND REFUND PROTECTION
                if (orderDetail.OrderStatus == 3 && orderDetail.OrderPaymentTypeId == 3) // 3 = Refunded // OrderPaymentTypeId 3 = Twint
                {
                    FailedPrintSession.Remove(lastJob.OrderId);
                    AppLogger.Log(
                        $"Order {lastJob.OrderId} already refunded by backend. Retry blocked."
                    );
                    return;
                }

                AppLogger.Log($"Retrying LAST failed Order {lastJob.OrderId} from Capture Page");

                using (var orderQueue = new OrderQueueService(
                    session.Token,
                    session.ShopLoginId,
                    session.PrinterId,
                    session.PrinterName ?? "",
                    session.PrinterStatus,
                    "", // qrUrl empty because we are retrying a failed print job
                    session.PrinterPrice,
                    DateTime.UtcNow, // expiry dummy
                    session.PrinterPaymentEnabled,
                    session.PrinterBrand
                ))
                {
                    // success/fail
                    var isSuccess = await orderQueue.ProcessSingleOrderAsync(lastJob, isRetry: true);

                    if (isSuccess)
                    {
                        FailedPrintSession.Remove(lastJob.OrderId);
                        PrintPreview.DeleteCapturedPhoto(lastJob.Image);
                        AppLogger.Log($"Retry completed successfully for LAST failed Order {lastJob.OrderId} from Capture Page");
                    }
                    else
                    {
                        AppLogger.Log($"Retry FAILED again for Order {lastJob.OrderId} from Capture Page, keeping it in FailedPrintSession");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"RetryFailedPrintJobsAsync Exception: {ex}");
            }
        }
    }
}
