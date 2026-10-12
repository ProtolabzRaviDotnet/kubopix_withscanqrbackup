using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using kuboPix.Helpers;
using kuboPix.Services;
using kuboPix.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace kuboPix.Views
{
    /// <summary>
    /// Interaction logic for PrintPreview.xaml
    /// </summary>
    public partial class PrintPreview : Page
    {
        private string _capturedPhotoPath = "";
        private string _selectedFrame = "none";
        private string _selectedBackground = "none";
        private long _frameWatermarkId = 0;
        private long _logoWatermarkId = 0;
      

        public PrintPreview()
        {
            InitializeComponent();
        }

        public PrintPreview(string imagePath, string selectedFrame, string selectedBackground) 
            : this(imagePath, selectedFrame, selectedBackground, 0, 0)
        {
        }

        public PrintPreview(string imagePath, string selectedFrame, string selectedBackground, long frameWatermarkId, long logoWatermarkId) : this()
        {
            _capturedPhotoPath = imagePath;
            _selectedFrame = selectedFrame;
            _selectedBackground = selectedBackground;
            _frameWatermarkId = frameWatermarkId;
            _logoWatermarkId = logoWatermarkId;

            Loaded += Page_Loaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePaymentProgressText();

            // 1. Load captured image safely from disk
            if (!string.IsNullOrEmpty(_capturedPhotoPath) && File.Exists(_capturedPhotoPath))
            {
                try
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(_capturedPhotoPath);
                    bitmap.EndInit();
                    CapturedPhoto.Source = bitmap;
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Error loading preview photo: " + ex.Message);
                }
            }

            // 2. Apply background selection color & opacity tint
            ApplyBackgroundChoice(_selectedBackground);

            // 3. Apply frame overlay selection
            ApplyFrameChoice(_selectedFrame);
        }

        private void ApplyBackgroundChoice(string bgName)
        {
            if (PhotoBorder == null) return;

            if (bgName == "none")
            {
                PhotoBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF9FAFB"));
                if (CapturedPhoto != null) CapturedPhoto.Opacity = 1.0;
            }
            else if (bgName == "winter")
            {
                PhotoBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE0F2FE"));
                if (CapturedPhoto != null) CapturedPhoto.Opacity = 0.85;
            }
            else if (bgName == "xmas")
            {
                PhotoBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2"));
                if (CapturedPhoto != null) CapturedPhoto.Opacity = 0.85;
            }
            else if (bgName == "summer")
            {
                PhotoBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
                if (CapturedPhoto != null) CapturedPhoto.Opacity = 0.85;
            }
        }

        private void ApplyFrameChoice(string frameName)
        {
            if (FrameOverlay == null) return;

            try
            {
                if (frameName == "none")
                {
                    FrameOverlay.Visibility = Visibility.Collapsed;
                }
                else if (frameName == "ellipse")
                {
                    FrameOverlay.Source = LoadAssetImage("Ellipse 5@2x.png");
                    FrameOverlay.Visibility = Visibility.Visible;
                }
                else if (frameName == "bottle")
                {
                    FrameOverlay.Source = LoadAssetImage("750mL Bottles (3)@2x.png");
                    FrameOverlay.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Error loading preview frame overlay: " + ex.Message);
            }
        }

        private BitmapImage LoadAssetImage(string fileName)
        {
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
            if (!File.Exists(fullPath))
            {
                string parentFallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", fileName);
                if (File.Exists(parentFallback))
                {
                    fullPath = parentFallback;
                }
            }

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.EndInit();
            return bitmap;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService != null && NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
            else
            {
                NavigationService?.Navigate(new Edit(_capturedPhotoPath));
            }
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            // ── BYPASS MODE (demo recording only) ─────────────────────────────────
            if (App.BypassPrinterForDemo)
            {
                NavigationService?.Navigate(new Checkout());
                return;
            }
            var session = kuboPixSessionManager.Load();
            if (session == null)
            {
                MessageBox.Show("No active session found. Please log in again.", "Session Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Refresh live printer state from server so dashboard payment settings take effect immediately
            try
            {
                var livePrinter = await LoginView.GetLivePrinterState(session.Token, session.ShopLoginId, session.PrinterId);
                if (livePrinter != null)
                {
                    session.PrinterPaymentEnabled = livePrinter.PrinterPaymentEnabled;
                    session.PrinterPrice = livePrinter.PrinterPrice;
                    session.PrinterStatus = livePrinter.PrinterStatus;
                    kuboPixSessionManager.Save(session);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("PrintPreview GetLivePrinterState check error: " + ex.Message);
            }

            if (session.PrinterPaymentEnabled == 0)
            {
                // FREE PATH — upload photo -> create free order -> run OQS print pipeline
                await HandleFreeKioskPrintAsync(session);
            }
            else
            {
                // PAID PATH — navigate to Checkout page
                NavigationService?.Navigate(new Checkout(_capturedPhotoPath, _selectedFrame, _selectedBackground, _frameWatermarkId, _logoWatermarkId));
            }
        }

        private async Task HandleFreeKioskPrintAsync(kuboPixSession session)
        {
            if (string.IsNullOrEmpty(_capturedPhotoPath) || !File.Exists(_capturedPhotoPath))
            {
                MessageBox.Show("Photo file missing.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 1. (OPTIONAL - COMMENTED OUT) Upload JPEG to backend. Kept for future reference if server-side photo storage is needed.
            /*
            string uploadedImageUrl = await UploadKioskPhotoAsync(session.Token, _capturedPhotoPath);
            if (string.IsNullOrEmpty(uploadedImageUrl))
            {
                MessageBox.Show("Failed to upload photo to server.", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            */

            // 2. Create free order via api/order/freeorder (sending order metadata)
            long orderId = await CreateFreeOrderAsync(session, "", _logoWatermarkId, _frameWatermarkId, 0);
            if (orderId <= 0)
            {
                MessageBox.Show("Failed to create order on server. Please try again.", "Order Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
           

            // 3. Route through OrderQueueService using local file path directly
            using var oqs = new OrderQueueService(
                session.Token,
                session.ShopLoginId,
                session.PrinterId,
                session.PrinterName ?? "",
                session.PrinterStatus,
                "", // qrUrl empty
                session.PrinterPrice,
                DateTime.UtcNow,
                session.PrinterPaymentEnabled,
                session.PrinterBrand
            );

            var order = new PendingOrder
            {
                OrderId = orderId,
                Image = _capturedPhotoPath, // Prints directly from local photo file
                ShopLoginId = session.ShopLoginId,
                PrinterId = session.PrinterId,
                IsPaidOrder = 0,
                OrderPaymentTypeId = 0
            };

            bool isSuccess = await oqs.ProcessSingleOrderAsync(order, isRetry: false);

            if (isSuccess)
            {
                // Delete immediately on successful print
                DeleteCapturedPhoto(_capturedPhotoPath);
            }
            else
            {
                // In failed case, delete after 3 minutes to allow operator retry via password dialog
                DeleteCapturedPhotoDelayed(_capturedPhotoPath, TimeSpan.FromMinutes(3), order.OrderId);
            }
        }

        public static async Task<string> UploadKioskPhotoAsync(string token, string localPath)
        {
            try
            {
                using var client = new HttpClient();
                if (!string.IsNullOrEmpty(token))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                byte[] fileBytes = await File.ReadAllBytesAsync(localPath);
                string fileName = Path.GetFileName(localPath);

                using var content = new MultipartFormDataContent();
                using var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

                content.Add(fileContent, "file", fileName);

                var response = await client.PostAsync($"{AppConfig.ApiBaseUrl}api/upload/kiosk-photo", content);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log($"UploadKioskPhotoAsync failed HTTP status: {response.StatusCode}");
                    return string.Empty;
                }

                string json = await response.Content.ReadAsStringAsync();
                JObject? result = JsonConvert.DeserializeObject<JObject>(json);

                if (result != null && (int?)result["status"] == 1)
                {
                    string? url = (string?)result["printImagePath"];
                    return url ?? string.Empty;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                AppLogger.Log("UploadKioskPhotoAsync Exception: " + ex.Message);
                return string.Empty;
            }
        }

        public static async Task<long> CreateFreeOrderAsync(
            kuboPixSession session, 
            string imageUrl, 
            long logoWatermarkId = 0, 
            long frameWatermarkId = 0, 
            int isPaidOrder = 0)
        {
            try
            {
                using var client = new HttpClient();
                if (!string.IsNullOrEmpty(session.Token))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
                }

                var payload = new FreeOrder_VM
                {
                    ShopLoginId = session.ShopLoginId,
                    PrinterId = session.PrinterId,
                    LogoWatermarkId = logoWatermarkId,
                    FrameWatermarkId = frameWatermarkId,
                    FinalImagePath = imageUrl ?? string.Empty,
                    IsPaidOrder = isPaidOrder
                };

                string jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"{AppConfig.ApiBaseUrl}api/order/freeorder", content);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log($"CreateFreeOrderAsync HTTP non-success: {response.StatusCode}");
                    return 0;
                }

                string responseJson = await response.Content.ReadAsStringAsync();
                JObject? result = JsonConvert.DeserializeObject<JObject>(responseJson);

                if (result != null)
                {
                    long orderId = 0;
                    if (result["orderId"] != null)
                    {
                        orderId = (long)result["orderId"]!;
                    }
                    else if (result["data"] != null && result["data"]!["orderId"] != null)
                    {
                        orderId = (long)result["data"]!["orderId"]!;
                    }
                    return orderId;
                }

                return 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log("CreateFreeOrderAsync Exception: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Safely deletes the captured photo from disk after printing to free disk space.
        /// Nulls the UI image source first to release any file lock.
        /// </summary>
        public static void DeleteCapturedPhoto(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    AppLogger.Log($"[Cleanup] Deleted captured photo: {filePath}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[Cleanup] Failed to delete captured photo: {ex.Message}");
                System.Diagnostics.Debug.WriteLine("DeleteCapturedPhoto error: " + ex.Message);
            }
        }

        /// <summary>
        /// Deletes the captured photo after a specified delay (3 minutes on print failure)
        /// to allow operator time to retry from the Password Dialog.
        /// Also removes the order from FailedPrintSession once the window expires.
        /// </summary>
        public static void DeleteCapturedPhotoDelayed(string? filePath, TimeSpan delay, long? orderId = null)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    AppLogger.Log($"[Cleanup] Scheduled deletion for {filePath} in {delay.TotalMinutes} minute(s).");
                    await Task.Delay(delay);

                    DeleteCapturedPhoto(filePath);

                    if (orderId.HasValue)
                    {
                        FailedPrintSession.Remove(orderId.Value);
                        AppLogger.Log($"[Cleanup] Removed expired failed Order {orderId.Value} from session after {delay.TotalMinutes} min.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[Cleanup] DeleteCapturedPhotoDelayed error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Gets the Captures folder path in Local Temp directory, creating it if it doesn't exist.
        /// Storing in local temp ensures proper write permissions when running from an installed build.
        /// </summary>
        public static string GetCapturesFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Captures");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return folder;
        }

        /// <summary>
        /// Deletes ALL files in the Captures folder older than the given age.
        /// Call this at Camera startup to wipe leftover files from previous sessions.
        /// </summary>
        public static void CleanupCapturesFolder(TimeSpan olderThan)
        {
            try
            {
                string folder = GetCapturesFolder();
                if (!Directory.Exists(folder)) return;

                foreach (string file in Directory.GetFiles(folder, "*.jpg"))
                {
                    try
                    {
                        if (File.GetCreationTime(file) < DateTime.Now - olderThan)
                        {
                            File.Delete(file);
                            AppLogger.Log($"[Cleanup] Removed old capture: {Path.GetFileName(file)}");
                        }
                    }
                    catch { /* skip locked/inaccessible files */ }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[Cleanup] CleanupCapturesFolder error: {ex.Message}");
            }
        }

        private async void UpdatePaymentProgressText()
        {
            try
            {
                var session = kuboPixSessionManager.Load();
                bool isPaymentEnabled = (session != null && session.PrinterPaymentEnabled == 1) ||
                                        LoginView.GetCurrentSessionPrinterPaymentEnabled() == 1;

                if (Step3Text != null)
                {
                    Step3Text.Text = isPaymentEnabled ? "Pay & Print" : "Print";
                }

                if (session != null && !string.IsNullOrEmpty(session.Token))
                {
                    var livePrinter = await LoginView.GetLivePrinterState(session.Token, session.ShopLoginId, session.PrinterId);
                    if (livePrinter != null)
                    {
                        session.PrinterPaymentEnabled = livePrinter.PrinterPaymentEnabled;
                        session.PrinterPrice = livePrinter.PrinterPrice;
                        session.PrinterStatus = livePrinter.PrinterStatus;
                        kuboPixSessionManager.Save(session);
                        LoginView.SetCurrentSessionPrinterPaymentEnabled(livePrinter.PrinterPaymentEnabled);

                        bool livePaymentEnabled = livePrinter.PrinterPaymentEnabled == 1;
                        if (Step3Text != null)
                        {
                            Step3Text.Text = livePaymentEnabled ? "Pay & Print" : "Print";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("PrintPreview UpdatePaymentProgressText error: " + ex.Message);
            }
        }
    }
}
