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
    /// Interaction logic for UnderMaintenancePage.xaml
    /// </summary>
    public partial class UnderMaintenancePage : Page
    {
        private DispatcherTimer? _printerRetryTimer;
        private bool _navigationTriggered = false;   // HARD GUARD

        // Becomes true only after printer is seen NOT READY at least once
        // This is the key fix to avoid auto navigation in manual maintenance
        private bool _hasSeenPrinterNotReady = false;

        public UnderMaintenancePage(bool fromPrinterError = false)
        {
            InitializeComponent();

            _hasSeenPrinterNotReady = fromPrinterError;

            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            StartPrinterRecoveryWatcher();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            StopPrinterWatcher();
        }

        // -------------------------------
        // MANUAL NAVIGATION (LOGIN)
        // -------------------------------
        private void BackToLogin_TouchDown(object sender, TouchEventArgs e)
        {
            e.Handled = true;
            BackToLogin_Click(sender, new RoutedEventArgs(Button.ClickEvent));
        }

        private void BackToLogin_Click(object sender, RoutedEventArgs e)
        {
            TriggerNavigation();

            NavigationService?.Navigate(new LoginView());
        }

        // -------------------------------
        // LOGO CLICK → EXIT FLOW
        // -------------------------------
        private void Logo_Click_UM(object sender, MouseButtonEventArgs e)
        {
            ExitButton_Click_UM(sender, e);
        }

        private async void ExitButton_Click_UM(object sender, RoutedEventArgs e)
        {
            TriggerNavigation();

            Int64 printerId = LoginView.GetCurrentSessionPrinterId();
            Int64 shoploginId = LoginView.GetCurrentSessionShopLoginId();

            var dialog = new ConfirmExitDialogForUnderMaintenancePage
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() != true)
                return;

            kuboPixSessionManager.Clear();

            if (printerId > 0 && shoploginId > 0)
            {
                await OrderQueueService.SetPrinterOffline(printerId, shoploginId);
            }

            NavigationService?.Navigate(new LoginView());
        }

        // -------------------------------
        // PRINTER RECOVERY WATCHER
        // -------------------------------
        private void StartPrinterRecoveryWatcher()
        {
            if (_printerRetryTimer != null)
                return;

            _printerRetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };

            _printerRetryTimer.Tick += async (s, e) =>
            {
                await CheckPrinterRecoveryAsync();
            };

            _printerRetryTimer.Start();
        }

        private void StopPrinterWatcher()
        {
            _printerRetryTimer?.Stop();
            _printerRetryTimer = null;
        }
        private async Task CheckPrinterRecoveryAsync()
        {
            // Hard guard: avoid double navigation
            if (_navigationTriggered)
                return;

            try
            {
                var session = kuboPixSessionManager.Load();
                if (session == null)
                    return;

                if (string.IsNullOrWhiteSpace(session.PrinterName))
                    return;

                string status = "";
                string brand = session.PrinterBrand ?? LoginView.SelectedPrinterBrand ?? "Seaory";

                try
                {
                    if (brand == "Epson")
                    {
                        ENS2Control ensControl = ENS2Control.GetInstance();
                        try
                        {
                            ensControl.Initialize(session.PrinterName, "");
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
                        finally
                        {
                            ensControl.Release();
                        }
                    }
                    else
                    {
                        // Read REAL hardware status
                        status = SeaoryPrinterPrint.CheckPrinterStatus(session.PrinterName);
                    }
                }
                catch (Exception ex)
                {
                    // SDK / driver level failure
                    AppLogger.Log($"[UM] Failed to read printer status for {brand}: {ex}");
                    return;
                }

                // CORE LOGIC
                // If this page was opened from PrinterError
                // and printer is READY → recover immediately
                if (_hasSeenPrinterNotReady &&
                    status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                {
                    TriggerNavigation();
                    await HandlePrinterRecovered(session);
                    return;
                }

                // Track printer NOT READY state
                if (!status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                {
                    _hasSeenPrinterNotReady = true;
                }
            }
            catch (Exception ex)
            {
                // Safety net — nothing should crash UI thread
                AppLogger.Log($"[UM] Printer recovery check failed: {ex}");
            }
        }
        private async Task HandlePrinterRecovered(kuboPixSession session)
        {
            NavigationService?.Navigate(new Capture());
            await Task.CompletedTask;
        }
                
        // -------------------------------
        //  SINGLE POINT OF TRUTH
        // -------------------------------
        private void TriggerNavigation()
        {
            if (_navigationTriggered)
                return;

            _navigationTriggered = true;
            StopPrinterWatcher();
        }
    }
}
