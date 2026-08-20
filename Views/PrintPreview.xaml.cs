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
      

        public PrintPreview()
        {
            InitializeComponent();
        }

        public PrintPreview(string imagePath, string selectedFrame, string selectedBackground, int copies = 1) : this()
        {
            _capturedPhotoPath = imagePath;
            _selectedFrame = selectedFrame;
            _selectedBackground = selectedBackground;
            

            Loaded += Page_Loaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
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

            if (session.PrinterPaymentEnabled == 0)
            {
                // FREE PATH — upload photo -> create free order -> run OQS print pipeline
                await HandleFreeKioskPrintAsync(session);
            }
            else
            {
                // PAID PATH — navigate to Checkout page
                NavigationService?.Navigate(new Checkout(_capturedPhotoPath, _selectedFrame, _selectedBackground));
            }
        }

        private async Task HandleFreeKioskPrintAsync(kuboPixSession session)
        {
            if (string.IsNullOrEmpty(_capturedPhotoPath) || !File.Exists(_capturedPhotoPath))
            {
                MessageBox.Show("Photo file missing.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 1. Upload JPEG to backend
            string uploadedImageUrl = await UploadKioskPhotoAsync(session.Token, _capturedPhotoPath);
            if (string.IsNullOrEmpty(uploadedImageUrl))
            {
                MessageBox.Show("Failed to upload photo to server.", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 2. Create free order via api/order/freeorder
            long orderId = await CreateFreeOrderAsync(session, uploadedImageUrl);
            if (orderId <= 0)
            {
                // Fallback: generate temporary timestamp order ID if backend order endpoint is not active yet
                orderId = DateTime.Now.Ticks % 1000000;
            }

            // 3. Route through OrderQueueService
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
                Image = uploadedImageUrl,
                ShopLoginId = session.ShopLoginId,
                PrinterId = session.PrinterId,
                IsPaidOrder = 0,
                OrderPaymentTypeId = 0
            };

            await oqs.ProcessSingleOrderAsync(order, isRetry: false);
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

        public static async Task<long> CreateFreeOrderAsync(kuboPixSession session, string imageUrl)
        {
            try
            {
                using var client = new HttpClient();
                if (!string.IsNullOrEmpty(session.Token))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
                }

                var payload = new
                {
                    ShopLoginId = session.ShopLoginId,
                    PrinterId = session.PrinterId,
                    PrintImagePath = imageUrl
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
                    if (result["data"] != null && result["data"]!["orderId"] != null)
                    {
                        orderId = (long)result["data"]!["orderId"]!;
                    }
                    else if (result["orderId"] != null)
                    {
                        orderId = (long)result["orderId"]!;
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
    }
}
