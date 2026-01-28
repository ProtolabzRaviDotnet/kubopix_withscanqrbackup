using MiniApp.Helpers;
using MiniApp.Services;
using MiniApp.ViewModels;
using MiniApp.Wrapper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
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
using System.Windows.Threading;
using static System.Net.WebRequestMethods;

namespace MiniApp.Views
{
    /// <summary>
    /// Interaction logic for LoginView.xaml
    /// </summary>
    public partial class LoginView : Page
    {
        private string? _printerDeviceId;
        private string? _connectedPrinterName;
        private string? _printerName;
        private string? _printerQRCode;
        private decimal _printerPrice;
        private decimal _sessionprinterPrice;
        private static string? _jwtToken;
        private Int64 _shopLoginId;
        private Int64 _printerId;
        private int _printerStatus;
        private int _sessionprinterStatus;
        private int _printerPaymentEnabled;
        private string _rawStatus = "";

        private DispatcherTimer? _printerRetryTimer;
        private bool _isCheckingPrinter = false;

        private static string? _currentSessionPassword; // store password for session
        private static string? _currentSessionPrinterName; // store printer name for session
        private static Int64 _currentSessionShopLoginId; // store ShopLoginId for session
        private static Int64 _currentSessionPrinterId; // store printerId for session
        private static int _currentSessionPrinterPaymentEnabled; // store paymentenabled for session

        public LoginView()
        {
            InitializeComponent();
            Loaded += LoginView_Loaded;
        }

        // Get Current Session Values
        public static string? GetCurrentSessionPassword() => _currentSessionPassword;
        public static string? GetCurrentSessionPrinterName() => _currentSessionPrinterName;
        public static Int64 GetCurrentSessionShopLoginId() => _currentSessionShopLoginId;
        public static Int64 GetCurrentSessionPrinterId() => _currentSessionPrinterId;
        public static int GetCurrentSessionPrinterPaymentEnabled() => _currentSessionPrinterPaymentEnabled;

        // Login View Loaded
        private async void LoginView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {

                LoadingOverlay.Visibility = Visibility.Visible;
                LoginCard.Visibility = Visibility.Collapsed;

                var printerResult = await CheckPrinterOnStart();

                LoadingOverlay.Visibility = Visibility.Collapsed;

                if (!printerResult.IsReady)
                {
                    // Printer issue → Login page with error
                    txtPrinterStatus.Text = $"Printer not ready : {_rawStatus}";
                    btnLogin.IsEnabled = false;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    LoginCard.Visibility = Visibility.Visible;

                    StartSilentPrinterRetry(); //Re-Render The Printer DLL Status
                    return;
                }

                // Printer OK
                _printerDeviceId = printerResult.DeviceId;
                _connectedPrinterName = printerResult.PrinterName;

                txtPrinterStatus.Text = $"Printer is Ready (Device ID: {_printerDeviceId})";

                //AUTO LOGIN CHECK (AFTER PRINTER OK)
                var session = MiniAppSessionManager.Load();

                if (session != null)
                {
                    bool tokenValid = await VerifyToken(session.Token);

                    var livePrinter = await GetLivePrinterState(session.Token, session.ShopLoginId, session.PrinterId);

                    if (livePrinter == null)
                    {
                        MiniAppSessionManager.Clear();
                        MessageBox.Show("Printer not available.", "Error");
                        return;
                    }

                    // LIVE DATA (SERVER TRUTH)
                    _printerName = livePrinter.PrinterName;
                    _printerStatus = livePrinter.PrinterStatus;
                    _printerPaymentEnabled = livePrinter.PrinterPaymentEnabled;
                    _printerPrice = livePrinter.PrinterPrice;

                    if (tokenValid)
                    {
                        _jwtToken = session.Token;
                        _shopLoginId = session.ShopLoginId;
                        _printerId = session.PrinterId;
                        _sessionprinterPrice = session.PrinterPrice;
                        _sessionprinterStatus = session.PrinterStatus;

                        // Restore password (if using)
                        _currentSessionPassword = session.Password;
                        _currentSessionPrinterName = session.PrinterName;

                        _currentSessionShopLoginId = session.ShopLoginId;
                        _currentSessionPrinterId = session.PrinterId;
                        _currentSessionPrinterPaymentEnabled = session.PrinterPaymentEnabled;

                        var qrDetails = await FetchQrDetailsForMiniApp(_printerId);

                        NavigationService.Navigate(
                          new ScanQrView(
                              _jwtToken,
                              _shopLoginId,
                              _printerId,
                              _printerName,
                              qrDetails.qrUrl,
                              _printerPrice,
                              _printerStatus,
                              qrDetails.expiry,
                              _printerPaymentEnabled
                          ));

                        return; // SUCCESS AUTO LOGIN
                    }
                    else
                    {
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        LoginCard.Visibility = Visibility.Visible;
                        MiniAppSessionManager.Clear(); // invalid token
                    }
                }

                //SHOW LOGIN PAGE (AS BEFORE)
                btnLogin.IsEnabled = true;
                LoadingOverlay.Visibility = Visibility.Collapsed;
                LoginCard.Visibility = Visibility.Visible;

            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                txtPrinterStatus.Text = $"Error checking printer: {ex.Message}";
                btnLogin.IsEnabled = false;
            }
        }

        // DLL-based printer check
        private Task<(bool IsReady, string? DeviceId, string? PrinterName)> CheckPrinterOnStart()
        {
            return Task.Run<(bool IsReady, string? DeviceId, string? PrinterName)>(() =>
            {
                try
                {
                    var physicalPrinters = PrinterSettings.InstalledPrinters
                        .Cast<string>()
                        .Where(printerName =>
                        {
                            var printerSettings = new PrinterSettings { PrinterName = printerName };
                            return printerSettings.IsValid && printerSettings.IsDefaultPrinter;
                        })
                        .ToList();

                    foreach (var name in physicalPrinters)
                    {
                        try
                        {
                            var (friendlyStatus, rawStatus) = SeaoryPrinterAPI.GetPrinterStatus(name);

                            _rawStatus = friendlyStatus;

                            if (!string.IsNullOrEmpty(friendlyStatus) &&
                                friendlyStatus.IndexOf("Ready", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                string deviceId = SeaoryPrinterAPI.GetPrinterInfo(
                                    name,
                                    SeaoryPrinterAPI.INFO_SERIAL_NUMBER
                                );

                                return (true, deviceId, name);
                            }

                        }
                        catch
                        {
                            // Ignore individual printer error
                        }
                    }

                    return (false, null, null);
                }
                catch
                {
                    return (false, null, null);
                }
            });
        }

        // Retry printer check silently
        private void StartSilentPrinterRetry()
        {
            if (_printerRetryTimer != null)
                return;

            _printerRetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };

            _printerRetryTimer.Tick += PrinterRetryTimer_Tick;
            _printerRetryTimer.Start();
        }

        // Printer retry timer tick
        private async void PrinterRetryTimer_Tick(object? sender, EventArgs e)
        {
            if (_isCheckingPrinter)
                return; // previous check still running

            _isCheckingPrinter = true;

            try
            {
                var result = await CheckPrinterOnStart();

                if (result.IsReady)
                {
                    _printerRetryTimer?.Stop();
                    _printerRetryTimer = null;

                    _printerDeviceId = result.DeviceId;
                    _connectedPrinterName = result.PrinterName;

                    txtPrinterStatus.Text =
                        $"Printer is Ready (Device ID: {_printerDeviceId})";

                    btnLogin.IsEnabled = true;
                }
                else
                {
                    txtPrinterStatus.Text =
                        $"Printer not ready : {_rawStatus}";
                }
            }
            catch (Exception ex)
            {
                txtPrinterStatus.Text = $"Printer check error: {ex.Message}";
            }
            finally
            {
                _isCheckingPrinter = false;
            }
        }

        // Login button click
        private async void Login_Click(object sender, RoutedEventArgs e)
        {

            string email = txtEmail.Text?.Trim() ?? string.Empty;
            string password = txtPassword.Password?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter both email and password.", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_printerDeviceId))
            {
                MessageBox.Show("Printer is not ready or not detected. Please check printer.", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                bool loginSuccess = await AuthenticateUser(email, password, _printerDeviceId);

                LoadingOverlay.Visibility = Visibility.Collapsed;

                if (loginSuccess)
                {

                    _currentSessionPassword = password;


                    // Fetch QR Details
                    var qrDetails = await FetchQrDetailsForMiniApp(_printerId);

                    string qrUrl = qrDetails.qrUrl;
                    string expiry = qrDetails.expiry;

                    // Navigate with QR URL + Expiry
                    var scanQrPage = new ScanQrView(
                        _jwtToken ?? string.Empty,
                        _shopLoginId,
                        _printerId,
                        _printerName ?? string.Empty,
                        qrUrl,
                        _printerPrice,
                        _printerStatus,
                        expiry,
                        _printerPaymentEnabled
                    );

                    NavigationService.Navigate(scanQrPage);

                }
                else
                {
                    MessageBox.Show("Invalid email or password.", "Login Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Fetch QR Details for Mini App
        public static async Task<dynamic> FetchQrDetailsForMiniApp(Int64 printerId)
        {
            using (HttpClient client = new HttpClient())
            {
                string apiUrl = $"{AppConfig.ApiBaseUrl}api/printer/getqrdetailsforminiapp";

                var body = new { printerId = printerId };
                StringContent content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

                HttpResponseMessage resp = await client.PostAsync(apiUrl, content);
                if (!resp.IsSuccessStatusCode) return false;

                string respContent = await resp.Content.ReadAsStringAsync();
                dynamic? result = JsonConvert.DeserializeObject(respContent);
                if (result == null)
                    return false;

                if (result.status != 1)

                    return false;

                return new
                {
                    qrUrl = (string)result.qrUrl,
                    expiry = (string)result.expiry
                };
            }
        }

        // API call with printer DeviceId included Working
        private async Task<bool> AuthenticateUser(string email, string password, string deviceId)
        {
            using (HttpClient client = new HttpClient())
            {
                string apiUrl = $"{AppConfig.ApiBaseUrl}api/miniapp/login";
                var loginData = new { Email = email, Password = password, DeviceId = deviceId };

                string json = JsonConvert.SerializeObject(loginData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await client.PostAsync(apiUrl, content);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return false;

                dynamic? result = JsonConvert.DeserializeObject(responseContent);
                if (result is null)
                {
                    return false;
                }

                if (result.status != true)
                {
                    return false;
                }

                _jwtToken = result.token;
                _shopLoginId = result.user.Id;
                _printerId = result.user.PrinterId;
                _printerName = result.user.PrinterName;
                _printerQRCode = result.user.PrinterQRCode;
                _printerPrice = result.user.PrintingPrice;
                _printerStatus = result.user.PrinterStatus;
                _printerPaymentEnabled = result.user.IsPaymentEnabled ?? 0;

                _currentSessionPassword = password;
                _currentSessionPrinterName = _connectedPrinterName;

                _currentSessionShopLoginId = _shopLoginId;
                _currentSessionPrinterId = _printerId;

                // NEW ADDITION (AUTO LOGIN – 6 MONTH)
                MiniAppSessionManager.Save(new MiniAppSession
                {
                    Token = _jwtToken,
                    ShopLoginId = _shopLoginId,
                    PrinterId = _printerId,
                    Password = password,
                    PrinterName = _connectedPrinterName ?? string.Empty,
                    PrinterPrice = _printerPrice,
                    PrinterStatus = _printerStatus,
                    PrinterPaymentEnabled = _printerPaymentEnabled,
                    SavedAt = DateTime.UtcNow
                });

                return true;
            }
        }

        // Verify JWT Token Validity
        public static async Task<bool> VerifyToken(string token)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var resp = await client.GetAsync(
                    $"{AppConfig.ApiBaseUrl}api/miniapp/checkauth");

                return resp.IsSuccessStatusCode;
            }
        }

        //  Placeholder logic 
        private void txtEmail_TextChanged(object sender, TextChangedEventArgs e)
        {
            txtEmailPlaceholder.Visibility = string.IsNullOrEmpty(txtEmail.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Password Placeholder logic
        private void txtPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            txtPasswordPlaceholder.Visibility = string.IsNullOrEmpty(txtPassword.Password)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Changed: TouchUp instead of Click for button
        private void Login_TouchUp(object sender, TouchEventArgs e)
        {
            e.Handled = true;  // Prevent double-tap firing two clicks
            Login_Click(sender, e);
        }

        // Changed: KeyDown instead of KeyUp for button
        private void Login_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
                Login_Click(sender, e);
        }

        // Changed: TouchDown instead of TouchUp for button
        private void Login_TouchDown(object sender, TouchEventArgs e)
        {
            e.Handled = true;  // Prevent double-firing
            Login_Click(sender, new RoutedEventArgs(Button.ClickEvent));  // Fire Click safely
        }

        // Admin Exit Button TouchDown
        private void AdminExitButton_TouchDown(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            AdminExitButton_Click_Login(sender, new RoutedEventArgs(Button.ClickEvent));
        }

        // Text Input Focus Handlers
        private void OnTextInputGotFocus(object sender, RoutedEventArgs e)
        {

        }

        // Text Input Lost Focus Handler
        private void OnTextInputLostFocus(object sender, RoutedEventArgs e)
        {

        }

        // Text Input Touch Down Handler
        private void OnTextInputTouchDown(object sender, TouchEventArgs e)
        {
            var input = sender as UIElement;
            if (input != null)
            {
                input.Focus();  // Immediate focus on touch down
                Keyboard.Focus(input);  // Ensure keyboard focus
            }
            e.Handled = true;  // Prevent bubbling/multiple events
        }

        // Admin Exit Button Click
        private async void AdminExitButton_Click_Login(object sender, RoutedEventArgs e)
        {
            var dialog = new ConfirmExitDialog();
            dialog.Owner = Window.GetWindow(this);

            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                MiniAppSessionManager.Clear();
                _currentSessionPassword = null;

                if (_printerId > 0 && _shopLoginId > 0)
                {
                    await OrderQueueService.SetPrinterOffline(
                        _printerId,
                        _shopLoginId
                    );
                }
                Application.Current.Shutdown();
            }
        }

        // Maintenance Button TouchDown
        private void MaintenanceButton_TouchDown(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            MaintenanceButton_Click_Login(sender, new RoutedEventArgs(Button.ClickEvent));
        }

        // Maintenance Button Click
        private void MaintenanceButton_Click_Login(object sender, RoutedEventArgs e)
        {
            var maintenancePage = new UnderMaintenancePage();
            NavigationService?.Navigate(maintenancePage);
        }

        // Get Live Printer State
        public static async Task<PrinterState?> GetLivePrinterState(string token, long shopLoginId, long printerId)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var resp = await client.GetAsync(
                    $"{AppConfig.ApiBaseUrl}api/get/printerstate?shopLoginId={shopLoginId}&printerId={printerId}");

                if (!resp.IsSuccessStatusCode)
                    return null;

                var json = await resp.Content.ReadAsStringAsync();

                dynamic? result = JsonConvert.DeserializeObject(json);
                if (result == null || result!.status! != 1)
                    return null;

                return JsonConvert.DeserializeObject<PrinterState>(
                    result!.data!.ToString()
                );
            }
        }

    }
}
