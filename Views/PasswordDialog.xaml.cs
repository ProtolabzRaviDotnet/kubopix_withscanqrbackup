using MiniApp.Services;
using MiniApp.Wrapper;
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

namespace MiniApp.Views
{
    /// <summary>
    /// Interaction logic for PasswordDialog.xaml
    /// </summary>
    public partial class PasswordDialog : Window
    {
        // PROPERTIES
        public string? SelectedAction { get; private set; }
        public string? Password { get; private set; }

        // CONSTRUCTOR
        public PasswordDialog()
        {
            InitializeComponent();
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

        // Load Ribbon Count
        private void LoadRibbonCount(string printer)
        {
            string ribbon = SeaoryPrinterAPI.GetPrinterInfo(printer, SeaoryPrinterAPI.INFO_RIBBON_COUNT);
            lblRibbon.Text = $"Ribbon Remaining : {ribbon}";
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
