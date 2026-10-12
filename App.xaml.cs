using kuboPix.Services;
using kuboPix.Views;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using kuboPix.Helpers;

namespace kuboPix
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // ── DEMO / RECORDING MODE ──────────────────────────────────────────────────
        // Set to TRUE only when recording a UI demo/walkthrough video.
        // Printer calls are completely skipped; flow goes straight to PrintProgressView.
        // ⚠️  Set back to FALSE before building the production release.
        public static bool BypassPrinterForDemo { get; } = false;                                                                                                                                                                                                                    

        public App()
        {
            // TOUCH → MOUSE CLICK CONVERSION (KIOSK FIX)
            EventManager.RegisterClassHandler(typeof(UIElement),
                UIElement.TouchUpEvent,
                new EventHandler<TouchEventArgs>(OnTouchUpConvertToClick),
                true);
        }
               
        private void OnTouchUpConvertToClick(object? sender, TouchEventArgs e)
        {
            if (sender is UIElement element)
            {
                var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                    Source = e.Source
                };
                element.RaiseEvent(mouse);
            }
        }
        protected override void OnStartup(StartupEventArgs e)
        {

            System.Net.ServicePointManager.SecurityProtocol =
            System.Net.SecurityProtocolType.Tls12 |
            System.Net.SecurityProtocolType.Tls13;

            base.OnStartup(e);

        }

        // Ensure printer is set offline on app exit or session end
        protected override void OnExit(ExitEventArgs e)
        {
            SetPrinterOfflineOnExit();
            base.OnExit(e);
        }

        // Handle Windows session ending (e.g., user logoff, shutdown) to ensure printer is set offline
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            SetPrinterOfflineOnExit();
            base.OnSessionEnding(e);
        }

        // Centralized method to set printer offline with safety checks and logging
        private void SetPrinterOfflineOnExit()
        {
            try
            {
                // 1. Check if already handled
                if (LoginView.IsOfflineReported)
                {
                    AppLogger.Log("[APP-EXIT] Printer already reported offline. Skipping redundant call.");
                    return;
                }
                // 2. Get values from static session getters
                long printerId = LoginView.GetCurrentSessionPrinterId();
                long shopLoginId = LoginView.GetCurrentSessionShopLoginId();
                if (printerId > 0 && shopLoginId > 0)
                {
                    AppLogger.Log($"[APP-EXIT] Attempting force-offline for PrinterId: {printerId}, ShopLoginId: {shopLoginId}");

                    LoginView.IsOfflineReported = true;
                    // 3. Execution with 5-second timeout (Blocking call)
                    bool completed = Task.Run(async () =>
                    {
                        await OrderQueueService.SetPrinterOffline(printerId, shopLoginId);
                    }).Wait(TimeSpan.FromSeconds(5));
                    if (completed)
                        AppLogger.Log("[APP-EXIT] Force-offline API call finished successfully.");
                    else
                        AppLogger.Log("[APP-EXIT] Force-offline API call TIMED OUT after 5 seconds.");
                }
                else
                {
                    AppLogger.Log("[APP-EXIT] No active session found. Skipping offline call.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[APP-EXIT] ERROR during exit-offline: {ex.Message}");
            }
        }
    }
}
