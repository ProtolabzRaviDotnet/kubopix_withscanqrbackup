using MiniApp.Services;
using MiniApp.Wrapper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            PasswordBox.Focus();

            string PrinterName = LoginView.GetCurrentSessionPrinterName() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(PrinterName))
            {
                LoadRibbonCount(PrinterName);
            }

            // FAILED PRINT JOB OPTION
            if (FailedPrintSession.HasFailedJobs)
            {
                rbPrintFailedJobs.Content =
                    $"Print Last Failed Job";

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
    }
}
