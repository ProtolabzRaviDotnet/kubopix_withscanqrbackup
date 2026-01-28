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

namespace MiniApp.Views
{
    /// <summary>
    /// Interaction logic for ConfirmExitDialogForUnderMaintenancePage.xaml
    /// </summary>
    public partial class ConfirmExitDialogForUnderMaintenancePage : Window
    {
        public ConfirmExitDialogForUnderMaintenancePage()
        {
            InitializeComponent();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            string input = PasswordBox.Password;
            string? actualPassword = LoginView.GetCurrentSessionPassword();

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

            this.DialogResult = true;
            this.Close();
        }

        private void OnButtonTouchDown(object sender, TouchEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            if (button.Name == "CancelButton")
            {
                this.DialogResult = false;
            }
            else if (button.Name == "ExitButton")
            {
                this.DialogResult = true;
            }

            this.Close();  // Dialog close 
            e.Handled = true;  // multiple taps avoid
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            PasswordPlaceholder.Visibility =
                string.IsNullOrEmpty(PasswordBox.Password)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

    }
}
