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
using kuboPix.Helpers;
using kuboPix.ViewModels;
using kuboPix.Services;

namespace kuboPix.Views
{
    /// <summary>
    /// Interaction logic for Capture.xaml
    /// </summary>
    public partial class Capture : Page
    {
        private string _pendingAction = "";
        private DispatcherTimer? _idleTimer;
        private const int IdleSeconds = 60; // 5 minutes

        public Capture()
        {
            InitializeComponent();

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
        }

        private void Capture_Unloaded(object sender, RoutedEventArgs e)
        {
            _idleTimer?.Stop();
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

                    dynamic qrDetails = await LoginView.FetchQrDetailsForkuboPix(session.PrinterId);
                    if (qrDetails == null || qrDetails is bool || qrDetails.ToString() == "False")
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

                    string qrUrl = qrDetails.qrUrl;
                    string expiry = qrDetails.expiry;

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
