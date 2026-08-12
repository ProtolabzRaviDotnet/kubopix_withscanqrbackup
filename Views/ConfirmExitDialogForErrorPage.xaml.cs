using kuboPix.Services;
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
using System.Windows.Shapes;

namespace kuboPix.Views
{
    /// <summary>
    /// Interaction logic for ConfirmExitDialogForErrorPage.xaml
    /// </summary>
    public partial class ConfirmExitDialogForErrorPage : Window
    {
        public string? SelectedAction { get; private set; }
        public string? Password { get; private set; }

        public ConfirmExitDialogForErrorPage()
        {
            InitializeComponent();
            // Match the dialog's size to the UI scale (see Helpers/DialogScaler.cs).
            SourceInitialized += (_, _) => kuboPix.Helpers.DialogScaler.ScaleToOwner(this);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

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

            if (rbMaintenanceMode.IsChecked == true)
                SelectedAction = "MAINTENANCE";

            DialogResult = true;
            Close();
        }
    }
}
