using com.epson.label.driver;
using kuboPix.Helpers;
using kuboPix.Services;
using kuboPix.ViewModels;
using kuboPix.Wrapper;
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
using System.Windows.Threading;

namespace kuboPix.Views
{
    /// <summary>
    /// Interaction logic for PrintErrorView.xaml
    /// </summary>
    public partial class PrintErrorView : Page
    {
        // Properties
        private DispatcherTimer? _refundTimer;
        private DateTime _refundDeadline;
        private PendingOrder? _failedOrder;

        private string _errorMessage;

        private DispatcherTimer? _printerRetryTimer;

        private readonly OrderQueueService? _orderQueueService;

        private bool _refundTriggered = false; // for safety

        private bool _isSdkPrinterError = false;     // Scan QR printer error flag

        private DispatcherTimer? _maintenanceDelayTimer;

        private bool _isCurrentErrorTwint = false; // NEW: Only current error TWINT flag

        private bool _printFailureTrackedOnce = false;

        // Constructor
        public PrintErrorView(string errorMessage, PendingOrder? failedOrder = null, int? orderPaymentTypeId = null, bool isSdkPrinterError = false)
        {
            AppLogger.Log($"PrintErrorView initialized with errorMessage: {errorMessage}");

            InitializeComponent();

            _errorMessage = errorMessage;
            ErrorMessageText.Text = errorMessage;

            _failedOrder = failedOrder ?? FailedPrintSession.GetAll().LastOrDefault();

            _isSdkPrinterError = isSdkPrinterError;

            // Determine if this error is a current TWINT order
            _isCurrentErrorTwint = _failedOrder != null &&
                                   _failedOrder.IsPaidOrder == 1 &&
                                   _failedOrder.OrderPaymentTypeId == 3 &&
                                   !string.IsNullOrEmpty(_failedOrder.PaymentIntentId) &&
                                   failedOrder != null; // only for the actual current order


            if (_isCurrentErrorTwint)
            {
                RefundTimerPanel.Visibility = Visibility.Visible;
                NoCharges.Visibility = Visibility.Collapsed;

                _refundDeadline = DateTime.UtcNow.AddMinutes(10);
                StartRefundTimer();
            }
            // STRIPE
            else if (orderPaymentTypeId == 1)
            {
                NoCharges.Visibility = Visibility.Visible;
                RefundTimerPanel.Visibility = Visibility.Collapsed;
            }
            // FREE
            else if (orderPaymentTypeId == 2)
            {
                NoCharges.Visibility = Visibility.Collapsed;
                RefundTimerPanel.Visibility = Visibility.Collapsed;
            }

            // SESSION / SERVICES
            var session = kuboPixSessionManager.Load();
            if (session == null)
            {
                AppLogger.Log("kuboPix session missing in PrintErrorView");
                return;
            }

            _orderQueueService = new OrderQueueService(
                session.Token,
                session.ShopLoginId,
                session.PrinterId,
                session.PrinterName,
                session.PrinterStatus,
                string.Empty,
                session.PrinterPrice,
                DateTime.MinValue,
                session.PrinterPaymentEnabled,
                session.PrinterBrand ?? LoginView.SelectedPrinterBrand
            );

            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
            _isSdkPrinterError = isSdkPrinterError;

            // ---- AFTER OrderQueueService initialization ----
            if (_isCurrentErrorTwint)
            {
                _ = TrackPrintFailureImmediately();
            }

        }

        // Page Events and Methods
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            ErrorMessageText.Text = _errorMessage;
            StartPrinterRecoveryWatcher();

            // COLLAPSE specific UI for Scan QR printer errors
            if (_isSdkPrinterError)
            {
                PrintingFailedTitle.Visibility = Visibility.Collapsed;
                PrintingFailedInstruction.Visibility = Visibility.Collapsed;
                RefundTimerPanel.Visibility = Visibility.Collapsed;
            }

            // NON-TWINT errors → show maintenance after 1 min
            if (!IsTwintRefundCase())
            {
                StartMaintenanceDelayTimer();
            }

        }

        // Unload Events and Methods
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _printerRetryTimer?.Stop();
            _printerRetryTimer = null;

            _refundTimer?.Stop();
            _refundTimer = null;

            _maintenanceDelayTimer?.Stop();
            _maintenanceDelayTimer = null;
        }

        // Print Error Exit Button Click
        private async void PrintErrorExitButton_Click(object sender, RoutedEventArgs e)
        
        {
            // MAKE PRINTER OFFLINE BEFORE EXIT / SHUTDOWN
            Int64 printerId = LoginView.GetCurrentSessionPrinterId();
            Int64 shoploginId = LoginView.GetCurrentSessionShopLoginId();

            var errorPageDailog = new ConfirmExitDialogForErrorPage();
            errorPageDailog.Owner = Window.GetWindow(this);

            bool? result = errorPageDailog.ShowDialog();
            if (result != true)
                return;

            // ACTION BASED LOGIC 
            if (errorPageDailog.SelectedAction == "EXIT")
            {
                // MAKE PRINTER OFFLINE BEFORE EXIT / SHUTDOWN
                if (printerId > 0 && shoploginId > 0)
                {
                    await OrderQueueService.SetPrinterOffline(
                        printerId,
                        shoploginId
                    );
                }

                // Close only Mini-App
                Application.Current.MainWindow.Close();
            }
            else if (errorPageDailog.SelectedAction == "MAINTENANCE")
            {
                var maintenancePage = new UnderMaintenancePage(fromPrinterError: true);
                NavigationService?.Navigate(maintenancePage);
            }
        }

        // Printer Recovery Watcher
        private void StartPrinterRecoveryWatcher()
        {
            if (_printerRetryTimer == null)
            {
                _printerRetryTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                _printerRetryTimer.Tick += async (s, e) => await CheckPrinterRecoveryAsync();
            }

            _printerRetryTimer.Start();
        }

        // Check Printer Recovery
        private async Task CheckPrinterRecoveryAsync()
        {
            try
            {
                var session = kuboPixSessionManager.Load();
                if (session == null) return;

                string printerName = session.PrinterName;
                if (string.IsNullOrEmpty(printerName)) return;

                string brand = session.PrinterBrand ?? LoginView.SelectedPrinterBrand ?? "Seaory";
                string status = "";

                if (brand == "Epson")
                {
                    ENS2Control ensControl = ENS2Control.GetInstance();
                    try
                    {
                        ensControl.Initialize(printerName, "");
                        StatusCode sc = ensControl.GetStatusInformation();

                        if (sc == StatusCode.ST_Idle)
                        {
                            status = "Ready";
                        }
                        else
                        {
                            status = sc.ToString().Replace("ST_", "");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"Epson CheckPrinterRecovery error: {ex.Message}");
                        status = "Error";
                    }
                    finally
                    {
                        ensControl.Release();
                    }
                }
                else
                {
                    status = SeaoryPrinterPrint.CheckPrinterStatus(printerName);
                    string warning = SeaoryPrinterPrint.CheckPrinterWarning(printerName);
                }

                if (!status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                    return;

                // HIDE MAINTENANCE OVERLAY
                Dispatcher.Invoke(() =>
                {
                    HideMaintenanceOverlay();
                });


                _printerRetryTimer?.Stop();

                await HandlePrinterRecovered(session);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Printer recovery check failed: {ex}");
                
                if (_printerRetryTimer != null && !_printerRetryTimer.IsEnabled)
                    _printerRetryTimer.Start();
            }
        }

        // Handle Printer Recovered
        private async Task HandlePrinterRecovered(kuboPixSession session)
        {
            NavigationService?.Navigate(new Capture());
            await Task.CompletedTask;
        }

        // Logo Click Error Handler
        private void Logo_Click_Error(object sender, MouseButtonEventArgs e)
        {
            PrintErrorExitButton_Click(sender, e);  // Trigger the existing exit logic when the logo is clicked
        }

        // Refund Timer Logic
        private void StartRefundTimer()
        {
            //_ = TrackPrintFailureImmediately();

            _refundDeadline = DateTime.Now.AddMinutes(10); // FIXED

            _refundTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _refundTimer.Tick += async (s, e) =>
            {
                var remaining = _refundDeadline - DateTime.Now; // FIXED

                if (remaining <= TimeSpan.Zero)
                {
                    _refundTimer.Stop();

                    bool refundSuccess = await RefundTwintIfRequired();

                    if (refundSuccess)
                    {
                        RefundTimerText.Text = "Your refund has been made";
                    }
                    else
                    {
                        RefundTimerText.Text = "Refund failed !\nPlease contact support.";
                        RefundTimerText.Foreground = Brushes.Red;
                        RefundTimerText.TextWrapping = TextWrapping.Wrap;
                        RefundTimerText.TextAlignment = TextAlignment.Center;
                    }

                    StartMaintenanceDelayTimer();
                    return;
                }

                int totalSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
                int minutes = totalSeconds / 60;
                int seconds = totalSeconds % 60;

                RefundTimerText.Text = $"Refund in {minutes:D2}:{seconds:D2}";
            };

            _refundTimer.Start();
        }

        // Maintenance Delay Timer
        private void StartMaintenanceDelayTimer()
        {
            _maintenanceDelayTimer?.Stop();

            _maintenanceDelayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };

            _maintenanceDelayTimer.Tick += (s, e) =>
            {
                _maintenanceDelayTimer.Stop();

                Dispatcher.Invoke(() =>
                {
                    ShowMaintenanceOverlay();
                });
            };

            _maintenanceDelayTimer.Start();
        }

        // Refund TWINT If Required
        private async Task<bool> RefundTwintIfRequired()
        {
            if (_failedOrder == null || _refundTriggered)
                return false;

            if (_orderQueueService == null)
            {
                AppLogger.Log("OrderQueueService is null. Refund skipped.");
                return false;
            }

            _refundTriggered = true;

            bool refundSuccess =
                await _orderQueueService.RefundTwintPaymentAsync(_failedOrder, 0);

            if (refundSuccess)
            {
                FailedPrintSession.Remove(_failedOrder.OrderId);

                AppLogger.Log($"TWINT refund successful for Order {_failedOrder.OrderId}");

                return true;
            }

            AppLogger.Log($"TWINT refund FAILED for Order {_failedOrder.OrderId}");

            // Important:
            // Allow retry if refund failed
            _refundTriggered = false;

            return false;
        }

        // Maintenance Overlay Methods
        private void ShowMaintenanceOverlay()
        {
            MaintenanceOverlay.Visibility = Visibility.Visible;
        }

        // Hide Maintenance Overlay
        private void HideMaintenanceOverlay()
        {
            MaintenanceOverlay.Visibility = Visibility.Collapsed;
        }

        // Check if it's a TWINT Refund Case
        private bool IsTwintRefundCase()
        {
            return _failedOrder != null &&
                   _failedOrder.IsPaidOrder == 1 &&
                   _failedOrder.OrderPaymentTypeId == 3 &&
                   !string.IsNullOrEmpty(_failedOrder.PaymentIntentId);
        }

        // Overlay CLICK - TRIGGERS Hide the overlay
        private void Overlay_Click(object sender, MouseButtonEventArgs e)
        {
            HideMaintenanceOverlay();
        }

        // Track Print Failure Immediately
        private async Task TrackPrintFailureImmediately()
        {
            if (_printFailureTrackedOnce)

                return;

            if (_orderQueueService == null)

                return;

            if (_failedOrder == null)

                return;


            // Only For Twint Paid Order
            if (_failedOrder.IsPaidOrder != 1 || _failedOrder.OrderPaymentTypeId != 3 || string.IsNullOrEmpty(_failedOrder.PaymentIntentId))

                return;

            _printFailureTrackedOnce = true; // SET BEFORE await

            await _orderQueueService.TrackPrintFailureAsync(_failedOrder);
        }

        // Retry Button Click Handler
        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppLogger.Log("RetryButton_Click — manual printer check triggered");
                await CheckPrinterRecoveryAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"RetryButton_Click error: {ex.Message}");
            }
        }

        // Refund TWINT If Required -- Working Old
        //private async Task RefundTwintIfRequired()
        //{
        //    if (_failedOrder == null)
        //        return;

        //    if (_refundTriggered) //double refund protection
        //        return;

        //    if (_orderQueueService == null)  
        //    {
        //        AppLogger.Log("OrderQueueService is null. Refund skipped.");
        //        return;
        //    }

        //    // ONLY paid + TWINT
        //    if (_failedOrder.IsPaidOrder != 1 ||
        //        _failedOrder.OrderPaymentTypeId != 3 ||
        //        string.IsNullOrEmpty(_failedOrder.PaymentIntentId))
        //        return;

        //    _refundTriggered = true;

        //    await _orderQueueService.RefundTwintPaymentAsync(_failedOrder, 0);

        //    FailedPrintSession.Remove(_failedOrder.OrderId); // cleanup

        //    AppLogger.Log(
        //        $"TWINT refund triggered for Order {_failedOrder.OrderId}"
        //    );
        //}


    }
}
