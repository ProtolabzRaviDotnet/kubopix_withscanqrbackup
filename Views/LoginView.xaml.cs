using com.epson.label.driver;
using kuboPix.Helpers;
using kuboPix.Services;
using kuboPix.ViewModels;
using kuboPix.Wrapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

namespace kuboPix.Views
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

        public static bool IsOfflineReported { get; set; } = false;
        public static string SelectedPrinterBrand { get; set; } = "Seaory S22K";

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

                //LoadingOverlay.Visibility = Visibility.Visible;
                //LoginCard.Visibility = Visibility.Collapsed;

                // Load existing session (if any) to set the correct SelectedPrinterBrand
                var session = kuboPixSessionManager.Load();
                if (session != null && !string.IsNullOrEmpty(session.PrinterBrand))
                {
                    SelectedPrinterBrand = session.PrinterBrand;
                }
                else
                {
                    SelectedPrinterBrand = "Seaory S22K"; // Default
                }

                var printerResult = await CheckPrinterOnStart();

                LoadingOverlay.Visibility = Visibility.Collapsed;

                if (!printerResult.IsReady)
                {
                    // Printer issue → Login page with error
                    txtPrinterStatus.Text = $"Printer not ready : {_rawStatus}";
                    btnLogin.IsEnabled = false;
                    //LoadingOverlay.Visibility = Visibility.Collapsed;
                    //LoginCard.Visibility = Visibility.Visible;

                    StartSilentPrinterRetry(); //Re-Render The Printer DLL Status
                    return;
                }

                // Printer OK
                _printerDeviceId = printerResult.DeviceId;
                _connectedPrinterName = printerResult.PrinterName;

                txtPrinterStatus.Text = $"Printer is Ready (Device ID: {_printerDeviceId})";

                //AUTO LOGIN CHECK (AFTER PRINTER OK)
                if (session != null)
                {
                    bool tokenValid = await VerifyToken(session.Token);

                    var livePrinter = await GetLivePrinterState(session.Token, session.ShopLoginId, session.PrinterId);

                    if (livePrinter == null)
                    {
                        kuboPixSessionManager.Clear();
                        MessageBox.Show("Printer not available.", "Error");
                        return;
                    }

                    // LIVE DATA (SERVER TRUTH)
                    _printerName = livePrinter.PrinterName;
                    _printerStatus = livePrinter.PrinterStatus;
                    _printerPaymentEnabled = livePrinter.PrinterPaymentEnabled;
                    _printerPrice = livePrinter.PrinterPrice;
                    // Set internal printer brand based on printer name or connected printer name
                    string brandCheckName = _printerName ?? string.Empty;
                    if (!string.IsNullOrEmpty(_connectedPrinterName))
                    {
                        brandCheckName += " " + _connectedPrinterName;
                    }
                    if (brandCheckName.Contains("Epson", StringComparison.OrdinalIgnoreCase) ||
                        brandCheckName.Contains("CW-", StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedPrinterBrand = "Epson";
                    }
                    else
                    {
                        SelectedPrinterBrand = "Seaory";
                    }

                    if (tokenValid)
                    {
                        _jwtToken = session.Token;
                        _shopLoginId = session.ShopLoginId;
                        _printerId = session.PrinterId;
                        _sessionprinterPrice = session.PrinterPrice;
                        _sessionprinterStatus = session.PrinterStatus;

                        // Update session brand on disk to fix any legacy brand mismatches
                        session.PrinterBrand = SelectedPrinterBrand;
                        session.PrinterName = _connectedPrinterName ?? string.Empty;
                        kuboPixSessionManager.Save(session);

                        // Restore password (if using)
                        _currentSessionPassword = session.Password;
                        _currentSessionPrinterName = session.PrinterName;

                        _currentSessionShopLoginId = session.ShopLoginId;
                        _currentSessionPrinterId = session.PrinterId;
                        _currentSessionPrinterPaymentEnabled = session.PrinterPaymentEnabled;

                        NavigationService.Navigate(new Capture());

                        return; // SUCCESS AUTO LOGIN
                    }
                    else
                    {
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        LoginCard.Visibility = Visibility.Visible;
                        kuboPixSessionManager.Clear(); // invalid token
                    }
                }

                //SHOW LOGIN PAGE (AS BEFORE)
                btnLogin.IsEnabled = true;
                //LoadingOverlay.Visibility = Visibility.Collapsed;
                //LoginCard.Visibility = Visibility.Visible;

            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                txtPrinterStatus.Text = $"Error checking printer: {ex.Message}";
                btnLogin.IsEnabled = false;
            }
        }

        private Task<(bool IsReady, string? DeviceId, string? PrinterName)> CheckPrinterOnStart()
        {
            return Task.Run<(bool IsReady, string? DeviceId, string? PrinterName)>(() =>
            {
                try
                {
                    var physicalPrinters = PrinterSettings.InstalledPrinters
                        .Cast<string>()
                        .ToList();

                    string preferredBrand = SelectedPrinterBrand ?? "Seaory";
                    string otherBrand = preferredBrand == "Epson" ? "Seaory" : "Epson";

                    // 1. Try preferred brand
                    var result = CheckPrinterBrandInternal(preferredBrand, physicalPrinters);
                    if (result.IsReady)
                    {
                        SelectedPrinterBrand = preferredBrand;
                        _rawStatus = result.RawStatus;
                        return (true, result.DeviceId, result.PrinterName);
                    }

                   

                    // 2. Try the other brand
                    var otherResult = CheckPrinterBrandInternal(otherBrand, physicalPrinters);
                    if (otherResult.IsReady)
                    {
                        SelectedPrinterBrand = otherBrand;
                        _rawStatus = otherResult.RawStatus;
                        return (true, otherResult.DeviceId, otherResult.PrinterName);
                    }

                    // If neither is ready, decide which error status to show based on what printer was found.
                    if (otherResult.PrinterName != null)
                    {
                        SelectedPrinterBrand = otherBrand;
                        _rawStatus = otherResult.RawStatus;
                    }
                    else if (result.PrinterName != null)
                    {
                        SelectedPrinterBrand = preferredBrand;
                        _rawStatus = result.RawStatus;
                    }
                    else
                    {
                        _rawStatus = result.RawStatus; // Default to preferred brand's error status (e.g. "Not Detected")
                    }

                    // AppLogger.Log($"CheckPrinterOnStart failed. Determined brand: {SelectedPrinterBrand}, Status: {_rawStatus}");
                    return (false, null, null);
                }
                catch (Exception ex)
                {
                    _rawStatus = $"System Error: {ex.Message}";
                    AppLogger.Log($"CheckPrinterOnStart system error: {ex}");
                    return (false, null, null);
                }
            });
        }

        private (bool IsReady, string? DeviceId, string? PrinterName, string RawStatus) CheckPrinterBrandInternal(string brand, List<string> physicalPrinters)
        {
            try
            {
                if (brand == "Epson")
                {
                    string? firstEpsonName = null;
                    string? firstEpsonStatus = null;
                    foreach (var name in physicalPrinters)
                    {
                        if (!IsEpsonPrinter(name))
                            continue;
                        try
                        {
                            if (IsEpsonPrinter(name))
                            {
                                ENS2Control ensControl = ENS2Control.GetInstance();
                                try
                                {
                                    ensControl.Initialize(name, "");
                                    StatusCode sc = ensControl.GetStatusInformation();
                                    string serial = ensControl.GetSerialNumber();

                                    string statusStr = sc.ToString().Replace("ST_", "");

                                    if (firstEpsonName == null)
                                    {
                                        firstEpsonName = name;
                                        firstEpsonStatus = statusStr;
                                    }

                                    if (sc == StatusCode.ST_Idle)
                                    {
                                        AppLogger.Log($"Epson printer '{name}' is ready. Serial: {serial}");
                                        return (true, serial, name, "Ready");
                                    }
                                    else
                                    {
                                        AppLogger.Log($"Epson printer '{name}' is not Ready. Status: {statusStr}");
                                    }
                                }
                                finally
                                {
                                    ensControl.Release();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            string errStr = $"Epson Error: {ex.Message}";
                            AppLogger.Log($"Error checking Epson printer '{name}': {ex}");
                            if (firstEpsonName == null)
                            {
                                firstEpsonName = name;
                                firstEpsonStatus = errStr;
                            }
                        }
                    }

                    if (firstEpsonName != null)
                    {
                        return (false, null, firstEpsonName, firstEpsonStatus ?? "Not Detected");
                    }
                }
                else // Seaory
                {
                    var defaultPrinters = physicalPrinters
                        .Where(printerName =>
                        {
                            var printerSettings = new PrinterSettings { PrinterName = printerName };
                            return printerSettings.IsValid && printerSettings.IsDefaultPrinter;
                        })
                        .ToList();
                    string? firstSeaoryName = null;
                    string? firstSeaoryStatus = null;

                    foreach (var name in defaultPrinters)
                    {
                        if (IsEpsonPrinter(name))
                            continue;
                        try
                        {
                            var (friendlyStatus, rawStatus) = SeaoryPrinterAPI.GetPrinterStatus(name);

                            if (firstSeaoryName == null)
                            {
                                firstSeaoryName = name;
                                firstSeaoryStatus = friendlyStatus;
                            }

                            if (!string.IsNullOrEmpty(friendlyStatus) &&
                                friendlyStatus.IndexOf("Ready", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                string deviceId = SeaoryPrinterAPI.GetPrinterInfo(
                                    name,
                                    SeaoryPrinterAPI.INFO_SERIAL_NUMBER
                                );
                                AppLogger.Log($"Seaory printer '{name}' is ready. Serial: {deviceId}");

                                return (true, deviceId, name, friendlyStatus);
                            }
                            else
                            {

                                AppLogger.Log($"Seaory printer '{name}' is not Ready. Status: {friendlyStatus}");

                            }
                        }
                        catch (Exception ex)
                        {
                            string errStr = $"Seaory Error: {ex.Message}";
                            AppLogger.Log($"Error checking Seaory printer '{name}': {ex}");
                            if (firstSeaoryName == null)
                            {
                                firstSeaoryName = name;
                                firstSeaoryStatus = errStr;
                            }
                        }
                    }
                    if (firstSeaoryName != null)
                    {
                        return (false, null, firstSeaoryName, firstSeaoryStatus ?? "Not Detected");
                    }


                }

            }
            catch (Exception ex)
            {
                AppLogger.Log($"CheckPrinterBrandInternal error for {brand}: {ex}");
                return (false, null, null, $"System Error: {ex.Message}");
            }

            return (false, null, null, "Not Detected");
        }

        private static bool IsEpsonPrinter(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   (name.Contains("EPSON", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CW-", StringComparison.OrdinalIgnoreCase));
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
                LoginCard.Visibility = Visibility.Collapsed;

                bool loginSuccess = await AuthenticateUser(email, password, _printerDeviceId);

                LoadingOverlay.Visibility = Visibility.Collapsed;

                if (loginSuccess)
                {

                    _currentSessionPassword = password;


                    NavigationService.Navigate(new Capture());

                }
                else
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    LoginCard.Visibility = Visibility.Visible;
                    MessageBox.Show("Invalid email or password.", "Login Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                LoginCard.Visibility = Visibility.Visible;
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Fetch QR Details for Mini App
        public static async Task<dynamic> FetchQrDetailsForkuboPix(Int64 printerId)
        {
            try
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
            catch (Exception ex)
            {
                AppLogger.Log($"FetchQrDetailsForkuboPix Error:: {ex}");
                return false;
            }
        }

        // API call with printer DeviceId included Working
        private async Task<bool> AuthenticateUser(string email, string password, string deviceId)
        {
            try
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

                    // Set internal printer brand based on printer name or connected printer name
                    string brandCheckName = _printerName ?? string.Empty;
                    if (!string.IsNullOrEmpty(_connectedPrinterName))
                    {
                        brandCheckName += " " + _connectedPrinterName;
                    }

                    if (brandCheckName.Contains("Epson", StringComparison.OrdinalIgnoreCase) ||
                        brandCheckName.Contains("CW-", StringComparison.OrdinalIgnoreCase))
                    {
                    SelectedPrinterBrand = "Epson";
                    }
            
                else
                {
                    SelectedPrinterBrand = "Seaory";
                }
                _printerQRCode = result.user.PrinterQRCode;
                _printerPrice = result.user.PrintingPrice;
                _printerStatus = result.user.PrinterStatus;
                _printerPaymentEnabled = result.user.IsPaymentEnabled ?? 0;

                _currentSessionPassword = password;
                _currentSessionPrinterName = _connectedPrinterName;

                _currentSessionShopLoginId = _shopLoginId;
                _currentSessionPrinterId = _printerId;

                // NEW ADDITION (AUTO LOGIN – 6 MONTH)
                kuboPixSessionManager.Save(new kuboPixSession
                {
                    Token = _jwtToken,
                    ShopLoginId = _shopLoginId,
                    PrinterId = _printerId,
                    Password = password,
                    PrinterName = _connectedPrinterName ?? string.Empty,
                    PrinterPrice = _printerPrice,
                    PrinterStatus = _printerStatus,
                    PrinterPaymentEnabled = _printerPaymentEnabled,
                    PrinterBrand = SelectedPrinterBrand,
                    SavedAt = DateTime.UtcNow
                });

                return true;
            }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"AuthenticateUser Login Error:: {ex.Message}");
                return false;
            }
        }

        // Verify JWT Token Validity
        public static async Task<bool> VerifyToken(string token)
        {
            try
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
            catch (Exception ex)
            {
                AppLogger.Log($"VerifyToken Login Error:: {ex}");
                return false;
            }
        }

        //  Placeholder logic 
        //private void txtEmail_TextChanged(object sender, TextChangedEventArgs e)
        //{
        //    txtEmailPlaceholder.Visibility = string.IsNullOrEmpty(txtEmail.Text)
        //        ? Visibility.Visible
        //        : Visibility.Collapsed;
        //}

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
                kuboPixSessionManager.Clear();
                _currentSessionPassword = null;

                if (_printerId > 0 && _shopLoginId > 0)
                {
                    // Mark as reported to prevent App.xaml.cs from calling it again
                    IsOfflineReported = true;

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
            try
            {
                using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var resp = await client.GetAsync($"{AppConfig.ApiBaseUrl}api/get/printerstate?shopLoginId={shopLoginId}&printerId={printerId}");

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
            catch (Exception ex)
            {
                // log error
                AppLogger.Log($"GetLivePrinterState Error:: {ex.Message}");
                return null;
            }
        }
              
        //Make Printer Offline Printer Id ShopLogin Id
        public static async Task<OrderDetail?> GetOrderDetailByOrderId(Int64 orderId)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string url = $"{AppConfig.ApiBaseUrl}api/get/orderdetails?orderId={orderId}";

                    AppLogger.Log("Calling GetOrderDetailByOrderId API: " + url);

                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                        return null;

                    var json = await response.Content.ReadAsStringAsync();

                    var apiResponse = JsonConvert.DeserializeObject<JObject>(json);
                    var dataToken = apiResponse?["data"];      // null-safe access
                    if (dataToken == null)
                        return null;

                    return dataToken.ToObject<OrderDetail>();

                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GetOrderDetailByOrderId Error: " + ex.Message);
                return null;
            }
        }

    }
}
