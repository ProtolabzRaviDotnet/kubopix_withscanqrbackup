using com.epson.label.driver;
using kuboPix.Services;
using kuboPix.Wrapper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

namespace kuboPix.Views
{
    /// <summary>
    /// Interaction logic for PasswordDialog.xaml
    /// </summary>
    public partial class PasswordDialog : Window
    {
        // PROPERTIES
        public string? SelectedAction { get; private set; }
        public string? Password { get; private set; }

        private DispatcherTimer? _inactivityTimer;

        // CONSTRUCTOR
        public PasswordDialog()
        {
            InitializeComponent();
            // Match the dialog's size to the UI scale (see Helpers/DialogScaler.cs).
            SourceInitialized += (_, _) => kuboPix.Helpers.DialogScaler.ScaleToOwner(this);
        }

        // EVENTS
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = StartImageMonitorAsync();

            // Call helper function
            _ = CheckAndRemoveRefundedOrderAsync();

            PasswordBox.Focus();

            string PrinterName = LoginView.GetCurrentSessionPrinterName() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(PrinterName))
            {
                LoadRibbonCount(PrinterName);
            }

            // ---------------------------------------------

            // FAILED PRINT JOB OPTION
            if (FailedPrintSession.HasFailedJobs)
            {
                rbPrintFailedJobs.Content = $"Print Last Failed Job";

                rbPrintFailedJobs.Visibility = Visibility.Visible;
            }
        }

        // ------------------ INACTIVITY TIMER SETUP ------------------
        private void SetupInactivityTimer()
        {
            _inactivityTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _inactivityTimer.Tick += InactivityTimer_Tick;
            _inactivityTimer.Start();

            this.PreviewMouseMove += ResetInactivityTimer;
            this.PreviewMouseDown += ResetInactivityTimer;
            this.PreviewKeyDown += ResetInactivityTimer;
            this.TouchDown += ResetInactivityTimer;
            this.Closed += PasswordDialog_Closed;
        }

        private void ResetInactivityTimer(object? sender, EventArgs e)
        {
            if (_inactivityTimer != null)
            {
                _inactivityTimer.Stop();
                _inactivityTimer.Start();
            }
        }

        private void InactivityTimer_Tick(object? sender, EventArgs e)
        {
            StopInactivityTimer();
            try
            {
                DialogResult = false;
            }
            catch { }
            Close();
        }

        private void PasswordDialog_Closed(object? sender, EventArgs e)
        {
            StopInactivityTimer();
        }

        private void StopInactivityTimer()
        {
            if (_inactivityTimer != null)
            {
                _inactivityTimer.Stop();
                _inactivityTimer.Tick -= InactivityTimer_Tick;
                _inactivityTimer = null;
            }

            this.PreviewMouseMove -= ResetInactivityTimer;
            this.PreviewMouseDown -= ResetInactivityTimer;
            this.PreviewKeyDown -= ResetInactivityTimer;
            this.TouchDown -= ResetInactivityTimer;
            this.Closed -= PasswordDialog_Closed;
        }

        // Load Ribbon Count
        private void LoadRibbonCount(string printer)
        {
            string brand = LoginView.SelectedPrinterBrand ?? "Seaory";
            if (brand.Equals("Epson", StringComparison.OrdinalIgnoreCase))
            {
                string inkStatus = "Unknown";
                ENS2Control ensControl = ENS2Control.GetInstance();
                try
                {
                    ensControl.Initialize(printer, "");
                    inkStatus = ensControl.GetInkRemaining();
                }
                catch (Exception ex)
                {
                    inkStatus = $"Error: {ex.Message}";
                }
                finally
                {
                    try { ensControl.Release(); } catch { }
                }

                lblRibbon.Text = $"Ink Remaining : {inkStatus}";
            }
            else
            {
                string ribbon = SeaoryPrinterAPI.GetPrinterInfo(printer, SeaoryPrinterAPI.INFO_RIBBON_COUNT);
                lblRibbon.Text = $"Ribbon Remaining : {ribbon}";
            }
        }

        // OK BUTTON CLICK
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string input = PasswordBox.Password;
            string actualPassword = LoginView.GetCurrentSessionPassword() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                MessageBox.Show("Please enter password!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (input != actualPassword)
            {
                MessageBox.Show("Wrong Password!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // return action type
            if (rbCloseApp.IsChecked == true)
                SelectedAction = "EXIT";

            else if(rbShutdown.IsChecked == true)
                SelectedAction = "SHUTDOWN";

            else if(rbMaintenanceMode.IsChecked == true)
                SelectedAction = "MAINTENANCE";

            else if(rbLogoutProfile.IsChecked == true)
                SelectedAction = "LOGOUTPROFILE";

            else if (rbPrintFailedJobs.IsChecked == true)
                SelectedAction = "PRINT_FAILED_JOBS";

            DialogResult = true;
            Close();
        }

        // CANCEL BUTTON CLICK
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async Task CheckAndRemoveRefundedOrderAsync()
        {
            if (!FailedPrintSession.HasFailedJobs)
                return;

            var lastJob = FailedPrintSession.GetAll()?.LastOrDefault();
            if (lastJob == null)
                return;

            var orderDetail = await LoginView.GetOrderDetailByOrderId(lastJob.OrderId);
            if (orderDetail != null && orderDetail.OrderStatus == 3 && orderDetail.OrderPaymentTypeId == 3)
            {
                rbPrintFailedJobs.Visibility = Visibility.Collapsed;

                FailedPrintSession.Remove(lastJob.OrderId);
                AppLogger.Log($"Refunded order {lastJob.OrderId} removed before showing retry option.");
            }
        }

        private async Task StartImageMonitorAsync()
        {
            while (true)
            {
                try
                {
                    if (!FailedPrintSession.HasFailedJobs)
                    {
                        await Task.Delay(2000);
                        continue;
                    }

                    var lastJob = FailedPrintSession.GetAll().LastOrDefault();

                    if (lastJob == null)
                    {
                        await Task.Delay(2000);
                        continue;
                    }

                    using var httpClient = new HttpClient();

                    var response = await httpClient.GetAsync(lastJob.Image);

                    // IMAGE DELETED / EXPIRED 
                    if (!response.IsSuccessStatusCode)
                    {
                        FailedPrintSession.Remove(lastJob.OrderId);

                        Dispatcher.Invoke(() =>
                        {
                            rbPrintFailedJobs.Visibility = Visibility.Collapsed;
                        });

                        AppLogger.Log($"Expired failed job removed. OrderId: {lastJob.OrderId}");

                        break;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"StartImageMonitorAsync Error: {ex.Message}");
                }

                // CHECK EVERY 2 SEC
                await Task.Delay(2000);
            }
        }
    }
}
