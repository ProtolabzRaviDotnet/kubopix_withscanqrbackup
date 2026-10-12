using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using QRCoder;
using kuboPix.Helpers;
using kuboPix.Services;
using kuboPix.ViewModels;

namespace kuboPix.Views
{
    /// <summary>
    /// Interaction logic for Checkout.xaml
    /// QR Payment screen conforming to the KuboPix Mini App QR Payment API specification.
    /// </summary>
    public partial class Checkout : Page
    {
        private readonly string _capturedPhotoPath = "";
        private readonly string _selectedFrame = "none";
        private readonly string _selectedBackground = "none";
        private readonly long _frameWatermarkId = 0;
        private readonly long _logoWatermarkId = 0;

        // In-memory payment session credentials (Rule 3: never log or expose)
        private long _sessionId = 0;
        private string _kioskSecret = string.Empty;

        private readonly PaymentSessionService _paymentService;
        private DispatcherTimer? _countdownTimer;
        private DispatcherTimer? _statusPollingTimer;
        private int _remainingSeconds = 600;

        // Atomic lock to guarantee order creation and print pipeline executes exactly once
        private int _isOrderProcessing = 0;
        private bool _isNavigatingAway = false;
        private bool _isSignalRActive = false;

        public Checkout()
        {
            InitializeComponent();
            _paymentService = new PaymentSessionService();
        }

        public Checkout(string imagePath, string selectedFrame, string selectedBackground) 
            : this(imagePath, selectedFrame, selectedBackground, 0, 0)
        {
        }

        public Checkout(string imagePath, string selectedFrame, string selectedBackground, long frameWatermarkId, long logoWatermarkId) 
            : this()
        {
            _capturedPhotoPath = imagePath ?? string.Empty;
            _selectedFrame = selectedFrame ?? "none";
            _selectedBackground = selectedBackground ?? "none";
            _frameWatermarkId = frameWatermarkId;
            _logoWatermarkId = logoWatermarkId;

            Loaded += Page_Loaded;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_capturedPhotoPath) || !File.Exists(_capturedPhotoPath))
            {
                MessageBox.Show("No photo found. Please capture a photo first.", 
                                "Photo Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                NavigateBack();
                return;
            }

            var session = kuboPixSessionManager.Load();
            if (session == null || string.IsNullOrEmpty(session.Token))
            {
                MessageBox.Show("Session expired. Please log in again.", 
                                "Session Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                NavigateBack();
                return;
            }

            await StartPaymentSessionAsync(session);
        }

        /// <summary>
        /// Step 2 & 3: Starts the payment session, renders the QR code, and connects SignalR
        /// </summary>
        private async Task StartPaymentSessionAsync(kuboPixSession session)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            TxtLoadingTitle.Text = "Generating Payment QR Code...";
            TxtLoadingSubtitle.Text = "Connecting to payment server, please wait a moment.";

            // 1. POST api/miniapp/payment/session
            var response = await _paymentService.CreateSessionAsync(session.Token, _logoWatermarkId, _frameWatermarkId);

            if (response == null || response.status != 1 || response.data == null)
            {
                string errMsg = !string.IsNullOrWhiteSpace(response?.message) 
                    ? response.message 
                    : "Unable to start payment session.";
                
                TxtLoadingTitle.Text = "Payment Initialization Failed";
                TxtLoadingSubtitle.Text = errMsg;

                await Task.Delay(3500);
                NavigateBack();
                return;
            }

            // Store credentials strictly in memory
            _sessionId = response.data.SessionId;
            _kioskSecret = response.data.KioskSecret;

            // Display Amount & Currency
            TxtAmount.Text = response.data.Amount.ToString("0.00");
            TxtCurrency.Text = $" {response.data.Currency}";

            // Render QR code
            RenderQrCode(response.data.QrUrl);

            // Display initial server message
            ApplyStatusTheme("pending", response.message);

            // Hide loading overlay
            LoadingOverlay.Visibility = Visibility.Collapsed;

            // Initialize Countdown
            _remainingSeconds = response.data.ExpiresInSeconds > 0 ? response.data.ExpiresInSeconds : 600;
            StartCountdownTimer();

            // Connect SignalR hub (Step 3)
            await InitializeSignalRAsync(session);
        }

        /// <summary>
        /// Renders QrUrl as a high-contrast QR code image
        /// </summary>
        private void RenderQrCode(string qrUrl)
        {
            if (string.IsNullOrWhiteSpace(qrUrl)) return;

            try
            {
                using var qrGenerator = new QRCodeGenerator();
                using var qrData = qrGenerator.CreateQrCode(qrUrl, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new QRCode(qrData);
                using var bitmap = qrCode.GetGraphic(12, System.Drawing.Color.Black, System.Drawing.Color.White, true);
                using var ms = new MemoryStream();

                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = ms;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                ImgQrCode.Source = bitmapImage;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"RenderQrCode Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Connects to SignalR hub and sets up reconnect and disconnect polling triggers
        /// </summary>
        private async Task InitializeSignalRAsync(kuboPixSession session)
        {
            _paymentService.ConnectionReconnected += OnSignalRReconnected;
            _paymentService.ConnectionClosed += OnSignalRDisconnected;

            bool joined = await _paymentService.ConnectSignalRAsync(
                _sessionId,
                _kioskSecret,
                payload => Dispatcher.Invoke(() => HandlePaymentUpdate(payload)));

            _isSignalRActive = joined;

            if (!joined)
            {
                AppLogger.Log("SignalR join returned false; starting fallback polling check.");
                StartFallbackPollingTimer();
            }
        }

        private void OnSignalRReconnected()
        {
            Dispatcher.Invoke(async () =>
            {
                _isSignalRActive = true;
                StopFallbackPollingTimer();
                await CheckStatusImmediatelyAsync();
            });
        }

        private void OnSignalRDisconnected()
        {
            Dispatcher.Invoke(() =>
            {
                _isSignalRActive = false;
                StartFallbackPollingTimer();
            });
        }

        /// <summary>
        /// Starts 5-second polling timer when SignalR is unavailable
        /// </summary>
        private void StartFallbackPollingTimer()
        {
            if (_statusPollingTimer != null && _statusPollingTimer.IsEnabled) return;

            _statusPollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _statusPollingTimer.Tick += async (s, e) =>
            {
                await CheckStatusImmediatelyAsync();
            };
            _statusPollingTimer.Start();
        }

        private void StopFallbackPollingTimer()
        {
            if (_statusPollingTimer != null)
            {
                _statusPollingTimer.Stop();
                _statusPollingTimer = null;
            }
        }

        private async Task CheckStatusImmediatelyAsync()
        {
            if (_isNavigatingAway || Volatile.Read(ref _isOrderProcessing) == 1) return;

            var session = kuboPixSessionManager.Load();
            if (session == null || _sessionId <= 0 || string.IsNullOrEmpty(_kioskSecret)) return;

            var response = await _paymentService.GetSessionStatusAsync(session.Token, _sessionId, _kioskSecret);
            if (response != null && response.data != null)
            {
                HandlePaymentUpdate(response.data);
            }
            else if (response != null && response.status == -1)
            {
                // Session not found or expired
                ApplyStatusTheme("expired", response.message);
                await Task.Delay(3000);
                NavigateBack();
            }
        }

        /// <summary>
        /// Handles incoming payment status updates from SignalR or polling
        /// </summary>
        private async void HandlePaymentUpdate(PaymentUpdatePayload payload)
        
        {
            if (payload == null || _isNavigatingAway) return;

            string status = (payload.Status ?? string.Empty).ToLowerInvariant().Trim();
            string message = !string.IsNullOrWhiteSpace(payload.Message) 
                ? payload.Message 
                : "Payment status updated.";

            ApplyStatusTheme(status, message);

            switch (status)
            {
                case "paid":
                    // Rule: customer paid. Prevent duplicate triggers and proceed with printing
                    if (Interlocked.CompareExchange(ref _isOrderProcessing, 1, 0) == 0)
                    {
                        StopAllTimers();
                        await ProcessPaidOrderAsync(payload);
                    }
                    break;

                case "failed":
                    // QR code is still valid, user can re-try on phone
                    break;

                case "cancelled":
                case "expired":
                    StopAllTimers();
                    await Task.Delay(3000);
                    NavigateBack();
                    break;

                case "scanned":
                case "action_at_kiosk":
                case "pending":
                case "used":
                default:
                    break;
            }
        }

        /// <summary>
        /// Step 7 — Customer has paid: execute CreateOrder instruction and print photo
        /// </summary>
        private async Task ProcessPaidOrderAsync(PaymentUpdatePayload payload)
        {
            try
            {
                AppLogger.Log($"[QR-PAYMENT] ProcessPaidOrderAsync started. SessionId={_sessionId}, Status={payload?.Status}, OrderId={payload?.OrderId}, PaymentIntentId={payload?.PaymentIntentId}, OrderPaymentTypeId={payload?.OrderPaymentTypeId}, PaymentType={payload?.PaymentType}");

                var session = kuboPixSessionManager.Load();
                if (session == null)
                {
                    AppLogger.Log("[QR-PAYMENT] Active session lost.");
                    MessageBox.Show("Active session lost. Please contact store support.", 
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    NavigateBack();
                    return;
                }

                string printerName = LoginView.GetCurrentSessionPrinterName() ?? session.PrinterName ?? "";
                if (string.IsNullOrEmpty(printerName))
                {
                    AppLogger.Log("[QR-PAYMENT] No active printer configuration found.");
                    MessageBox.Show("No active printer configuration found.", 
                                    "Printer Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    NavigateBack();
                    return;
                }

                long orderId = 0;

                // 1. If OrderId is already present in event payload, use it
                if (payload != null && payload.OrderId.HasValue && payload.OrderId.Value > 0)
                {
                    orderId = payload.OrderId.Value;
                    AppLogger.Log($"[QR-PAYMENT] Using OrderId from payload: {orderId}");
                }
                // 2. Otherwise post the CreateOrder block given by the server (Rule 1)
                else if (payload?.CreateOrder != null)
                {
                    AppLogger.Log($"[QR-PAYMENT] Posting server CreateOrder block to {payload.CreateOrder.Url}...");
                    orderId = await _paymentService.ExecuteCreateOrderAsync(session.Token, payload.CreateOrder);
                    AppLogger.Log($"[QR-PAYMENT] ExecuteCreateOrderAsync returned orderId: {orderId}");
                }

                // 3. Fallback verification if order ID wasn't returned directly
                if (orderId <= 0)
                {
                    AppLogger.Log("[QR-PAYMENT] OrderId is 0; checking session status fallback...");
                    var statusCheck = await _paymentService.GetSessionStatusAsync(session.Token, _sessionId, _kioskSecret);
                    if (statusCheck?.data?.OrderId.HasValue == true && statusCheck.data.OrderId.Value > 0)
                    {
                        orderId = statusCheck.data.OrderId.Value;
                        AppLogger.Log($"[QR-PAYMENT] Fallback session status returned OrderId: {orderId}");
                    }
                }

                if (orderId <= 0)
                {
                    AppLogger.Log("[QR-PAYMENT] Failed to obtain valid OrderId from server.");
                    MessageBox.Show("Payment succeeded, but order registration timed out. Please contact store staff.", 
                                    "Order Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    NavigateBack();
                    return;
                }

                AppLogger.Log($"[QR-PAYMENT] Final order registered. OrderId={orderId}, ShopLoginId={session.ShopLoginId}, PrinterId={session.PrinterId}, PaymentTypeId={payload?.OrderPaymentTypeId}");

                // 4. Dispatch to OrderQueueService for physical printing
                using var oqs = new OrderQueueService(
                    session.Token,
                    session.ShopLoginId,
                    session.PrinterId,
                    printerName,
                    session.PrinterStatus,
                    "",
                    session.PrinterPrice,
                    DateTime.UtcNow,
                    session.PrinterPaymentEnabled,
                    session.PrinterBrand
                );

                var order = new PendingOrder
                {
                    OrderId = orderId,
                    Image = _capturedPhotoPath,
                    ShopLoginId = session.ShopLoginId,
                    PrinterId = session.PrinterId,
                    IsPaidOrder = 1,
                    OrderPaymentTypeId = (payload != null && payload.OrderPaymentTypeId > 0) ? payload.OrderPaymentTypeId : 1,
                    PaymentIntentId = payload?.PaymentIntentId ?? string.Empty
                };

                bool isSuccess = await oqs.ProcessSingleOrderAsync(order, isRetry: false);

                if (isSuccess)
                {
                    // Delete immediately on successful print
                    PrintPreview.DeleteCapturedPhoto(_capturedPhotoPath);
                }
                else
                {
                    // In failed case, delete after 3 minutes to allow operator retry via password dialog
                    PrintPreview.DeleteCapturedPhotoDelayed(_capturedPhotoPath, TimeSpan.FromMinutes(3), order.OrderId);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ProcessPaidOrderAsync Exception: {ex.Message}");
                MessageBox.Show("An unexpected error occurred while processing print job.", 
                                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                NavigateBack();
            }
        }

        /// <summary>
        /// Logs real-time status and message from server
        /// </summary>
        private void ApplyStatusTheme(string status, string message)
        {
            AppLogger.Log($"[QR-PAYMENT] Status: {status}, Message: {message}");
        }

        private void StartCountdownTimer()
        {
            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += CountdownTimer_Tick;
            UpdateCountdownDisplay();
            _countdownTimer.Start();
        }

        private async void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            _remainingSeconds--;
            UpdateCountdownDisplay();

            if (_remainingSeconds <= 0)
            {
                _countdownTimer?.Stop();
                await CancelAndExitAsync("Payment session expired.");
            }
        }

        private void UpdateCountdownDisplay()
        {
            int displaySeconds = Math.Max(0, _remainingSeconds);
            int minutes = displaySeconds / 60;
            int seconds = displaySeconds % 60;
            TxtCountdownTimer.Text = $"Expires in: {minutes:D2}:{seconds:D2}";
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            BackButton.IsEnabled = false;
            await CancelAndExitAsync("Cancelling payment...");
        }

        /// <summary>
        /// Step 6 — Cancel session on Back button or countdown reaching zero.
        /// Safeguard: If status is 'paid', carry on with Step 7 and print!
        /// </summary>
        private async Task CancelAndExitAsync(string promptMessage)
        {
            if (_isNavigatingAway) return;

            // If order processing has already started, do not cancel
            if (Volatile.Read(ref _isOrderProcessing) == 1) return;

            StopAllTimers();
            AppLogger.Log($"[QR-PAYMENT] {promptMessage}");

            var session = kuboPixSessionManager.Load();
            if (session != null && _sessionId > 0 && !string.IsNullOrEmpty(_kioskSecret))
            {
                var cancelResp = await _paymentService.CancelSessionAsync(session.Token, _sessionId, _kioskSecret);

                // CRITICAL RULE (Step 6): If data.Status is "paid", customer paid right before cancel.
                // Do NOT go back. Carry on with printing!
                if (cancelResp?.data != null && 
                    string.Equals(cancelResp.data.Status, "paid", StringComparison.OrdinalIgnoreCase))
                {
                    if (Interlocked.CompareExchange(ref _isOrderProcessing, 1, 0) == 0)
                    {
                        await ProcessPaidOrderAsync(cancelResp.data);
                        return;
                    }
                }
            }

            _isNavigatingAway = true;
            NavigateBack();
        }

        private void NavigateBack()
        {
            StopAllTimers();
            _paymentService.Dispose();

            if (NavigationService != null && NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
            else
            {
                NavigationService?.Navigate(new PrintPreview(_capturedPhotoPath, _selectedFrame, _selectedBackground, _frameWatermarkId, _logoWatermarkId));
            }
        }

        private void StopAllTimers()
        {
            _countdownTimer?.Stop();
            _countdownTimer = null;

            StopFallbackPollingTimer();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            StopAllTimers();
            _paymentService.Dispose();
        }
    }
}
