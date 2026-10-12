using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using kuboPix.Helpers;
using kuboPix.Services;
using kuboPix.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace kuboPix.Views
{
    /// <summary>
    /// Interaction logic for Edit.xaml
    /// </summary>
    public partial class Edit : Page
    {
        private string _capturedPhotoPath = "";
        private string _selectedFrame = "none";
        private string _selectedBackground = "none";
        private string _selectedLogo = "none";
        private long _selectedFrameId = 0;
        private long _selectedLogoId = 0;
        private string? _selectedFrameImageUrl = null;
        private string? _selectedLogoImageUrl = null;
        private string? _selectedBackgroundImageUrl = null;
        private string? _removedBgPhotoPath = null;
        private string? _bakedPhotoPath = null;

        // Set true when GET /api/get/backgrounds returns enabled:true (KuboPix shops only)
        private bool _backgroundsEnabled = false;

        private List<WatermarkFrame> _availableFrames = new();
        private List<WatermarkFrame> _availableLogos = new();
        private List<BackgroundItem> _availableBackgrounds = new();

        private static readonly Brush GrayBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE5E7EB"));
        private static readonly Brush ActiveBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5C6BFF"));

        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly Dictionary<string, ImageSource> _imageCache = new();

        public Edit()
        {
            InitializeComponent();
            UpdatePaymentProgressText();
        }

        public Edit(string imagePath) : this()
        {
            _capturedPhotoPath = imagePath;

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                SetCapturedPhotoSource(imagePath);
            }

            Loaded += async (s, e) =>
            {
                UpdatePaymentProgressText();
                await Task.WhenAll(
                    LoadWatermarksFromBackendAsync(),
                    LoadBackgroundsFromBackendAsync()
                );
            };
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            int schemeIdx = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeIdx > 0)
            {
                string schemeAndHost = url.Substring(0, schemeIdx + 3);
                string rest = url.Substring(schemeIdx + 3);
                while (rest.Contains("//"))
                {
                    rest = rest.Replace("//", "/");
                }
                return schemeAndHost + rest;
            }
            return url;
        }

        private static async Task<ImageSource?> LoadImageAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = NormalizeUrl(url);

            lock (_imageCache)
            {
                if (_imageCache.TryGetValue(url, out var cached))
                    return cached;
            }

            try
            {
                byte[] data;
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    data = await _httpClient.GetByteArrayAsync(url);
                }
                else
                {
                    string localPath = url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                        ? new Uri(url).LocalPath : url;
                    if (!File.Exists(localPath)) return null;
                    data = await File.ReadAllBytesAsync(localPath);
                }

                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(data))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }
                bmp.Freeze();

                lock (_imageCache)
                {
                    _imageCache[url] = bmp;
                }
                return bmp;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LoadImageAsync failed for [{url}]: {ex.Message}");
                return null;
            }
        }

        private async Task LoadWatermarksFromBackendAsync()
        {
            var session = kuboPixSessionManager.Load();
            string token = session?.Token ?? "";

            _availableFrames.Clear();
            _availableLogos.Clear();

            // Single clean 'None' option
            _availableFrames.Add(new WatermarkFrame { Id = 0, FrameName = "None", FrameImage = "", Status = 1, Type = "frame" });

            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    string apiUrl = $"{AppConfig.ApiBaseUrl}api/get/watermarks";

                    var resp = await client.GetAsync(apiUrl);

                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        JObject? result = JsonConvert.DeserializeObject<JObject>(json);

                        if (result != null && (int?)result["status"] == 1 && result["data"] is JArray flatList)
                        {
                            var items = flatList.ToObject<List<WatermarkFrame>>();
                            if (items != null)
                            {
                                foreach (var item in items.Where(x => x.Status == 1))
                                {
                                    // Skip duplicate 'No Frame' or empty image items from server
                                    if (item.Id == 80 ||
                                        item.FrameName.Equals("No Frame", StringComparison.OrdinalIgnoreCase) ||
                                        string.IsNullOrEmpty(item.FrameImage))
                                    {
                                        continue;
                                    }

                                    // Check if item is Frame (TypeId=2 or path in /Frames/) or Logo
                                    bool isFrame = item.TypeId == 2 ||
                                                   item.FrameImage.Contains("/Frames/", StringComparison.OrdinalIgnoreCase) ||
                                                   item.FrameImage.Contains("Frame", StringComparison.OrdinalIgnoreCase);

                                    if (isFrame)
                                    {
                                        item.Type = "frame";
                                        _availableFrames.Add(item);
                                    }
                                    else
                                    {
                                        item.Type = "logo";
                                        _availableLogos.Add(item);
                                    }
                                }
                            }
                           
                        }
                    }
                    else
                    {
                        AppLogger.Log($"Watermarks API failed — HTTP {(int)resp.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log("LoadWatermarksFromBackendAsync exception: " + ex.Message);
                }
            }

            RenderFrameButtons();
        }

        private void RenderFrameButtons()
        {
            if (FrameWrapPanel == null) return;
            FrameWrapPanel.Children.Clear();

            // ── Frames ──────────────────────────────────────────────────────────────
            foreach (var frame in _availableFrames)
            {
                bool isNone = frame.Id == 0 || string.Equals(frame.FrameName, "none", StringComparison.OrdinalIgnoreCase);
                bool isSelected = isNone
                    ? (string.IsNullOrEmpty(_selectedFrame) || string.Equals(_selectedFrame, "none", StringComparison.OrdinalIgnoreCase))
                    : string.Equals(frame.FrameName, _selectedFrame, StringComparison.OrdinalIgnoreCase);

                var card = CreateThumbnailCard(
                    isNone ? "None" : frame.FrameName,
                    GetFrameImageUri(frame),
                    isSelected,
                    () => SelectFrameItem(frame),
                    isFrame: true
                );
                FrameWrapPanel.Children.Add(card);
            }

            // ── Logos (if any returned from watermarks) ──────────────────────────────
            foreach (var logo in _availableLogos)
            {
                bool isSelected = string.Equals(logo.FrameName, _selectedLogo, StringComparison.OrdinalIgnoreCase);
                var card = CreateThumbnailCard(
                    logo.FrameName,
                    GetFrameImageUri(logo),
                    isSelected,
                    () => SelectLogoItem(logo),
                    isFrame: false
                );
                FrameWrapPanel.Children.Add(card);
            }
        }

        private Uri? GetFrameImageUri(WatermarkFrame frame)
        {
            if (string.IsNullOrEmpty(frame.FrameImage)) return null;

            if (frame.FrameImage.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                frame.FrameImage.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(NormalizeUrl(frame.FrameImage), UriKind.Absolute);
            }

            string subFolder = (frame.TypeId == 1 || frame.Type == "logo") ? "Logos" : "Frames";
            return new Uri(NormalizeUrl($"{AppConfig.ApiBaseUrl}Content/Uploads/WaterMarks/{subFolder}/{frame.FrameImage}"), UriKind.Absolute);
        }

        private void RenderBackgroundButtons()
        {
            if (BackgroundSectionHeader != null)
                BackgroundSectionHeader.Visibility = Visibility.Visible;
            if (BackgroundScrollViewer != null)
                BackgroundScrollViewer.Visibility = Visibility.Visible;
            if (BackgroundWrapPanel == null) return;

            BackgroundWrapPanel.Children.Clear();

            // "None" — always first (restores original photo)
            var noneItem = new BackgroundItem { Id = 0, BackgroundName = "None", BackgroundImage = "", Status = 1 };
            bool noneSelected = string.IsNullOrEmpty(_selectedBackground) || string.Equals(_selectedBackground, "none", StringComparison.OrdinalIgnoreCase);
            BackgroundWrapPanel.Children.Add(
                CreateThumbnailCard("None", null, noneSelected, () => SelectBackgroundItem(noneItem), isFrame: false));

            // API backgrounds
            if (_backgroundsEnabled)
            {
                foreach (var bg in _availableBackgrounds.Where(x => x.Id != 0))
                {
                    Uri? uri = GetBackgroundImageUri(bg);
                    bool isSelected = string.Equals(bg.BackgroundName, _selectedBackground, StringComparison.OrdinalIgnoreCase);
                    var card = CreateThumbnailCard(
                        bg.BackgroundName,
                        uri,
                        isSelected,
                        () => SelectBackgroundItem(bg),
                        isFrame: false
                    );
                    BackgroundWrapPanel.Children.Add(card);
                }
            }
        }

        private Uri? GetBackgroundImageUri(BackgroundItem bg)
        {
            if (string.IsNullOrEmpty(bg.BackgroundImage)) return null;

            if (bg.BackgroundImage.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                bg.BackgroundImage.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(NormalizeUrl(bg.BackgroundImage), UriKind.Absolute);
            }

            return new Uri(NormalizeUrl($"{AppConfig.ApiBaseUrl}Content/Uploads/Backgrounds/{bg.BackgroundImage}"), UriKind.Absolute);
        }

        private async void SelectLogoItem(WatermarkFrame logo)
        {
            bool isNone = logo.Id == 0 || string.Equals(logo.FrameName, "none", StringComparison.OrdinalIgnoreCase);
            _selectedLogo = isNone ? "none" : logo.FrameName;
            _selectedLogoId = isNone ? 0 : logo.Id;

            if (isNone)
            {
                _selectedLogoImageUrl = null;
                if (LogoOverlay != null)
                {
                    LogoOverlay.Source = null;
                    LogoOverlay.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                var uri = GetFrameImageUri(logo);
                if (uri != null)
                {
                    _selectedLogoImageUrl = uri.ToString();
                    var src = await LoadImageAsync(_selectedLogoImageUrl);
                    if (LogoOverlay != null && _selectedLogo == logo.FrameName)
                    {
                        LogoOverlay.Source = src;
                        LogoOverlay.Visibility = Visibility.Visible;
                    }
                }
            }

            RenderFrameButtons();
        }

        private async Task LoadBackgroundsFromBackendAsync()
        {
            var session = kuboPixSessionManager.Load();
            string token = session?.Token ?? "";

            _backgroundsEnabled = false;
            _availableBackgrounds.Clear();

            if (string.IsNullOrEmpty(token)) { RenderBackgroundButtons(); return; }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                string apiUrl = $"{AppConfig.ApiBaseUrl}api/get/backgrounds";
           
                var resp = await client.GetAsync(apiUrl);
              

                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    var apiResp = JsonConvert.DeserializeObject<BackgroundApiResponse>(json);

                    if (apiResp != null && apiResp.Status == 1)
                    {
                        _backgroundsEnabled = apiResp.Enabled;
                        if (apiResp.Enabled && apiResp.Data != null)
                        {
                            _availableBackgrounds = apiResp.Data;
                           
                        }
                    }
                }
                else
                {
                    AppLogger.Log($"Backgrounds API failed — HTTP {(int)resp.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("LoadBackgroundsFromBackendAsync error: " + ex.Message);
            }

            RenderBackgroundButtons();
        }

        private FrameworkElement CreateThumbnailCard(string title, Uri? imageUri, bool isSelected, Action onClick, bool isFrame = false)
        {
            var stack = new StackPanel
            {
                Margin = new Thickness(0, 0, 16, 6),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var border = new Border
            {
                BorderBrush = isSelected ? ActiveBorder : GrayBorder,
                BorderThickness = new Thickness(isSelected ? 3.0 : 1.5),
                CornerRadius = new CornerRadius(14),
                Width = 100,
                Height = 100,
                Background = isFrame
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3F4F6"))
                    : System.Windows.Media.Brushes.White,
                Cursor = Cursors.Hand,
                ClipToBounds = true
            };

            border.MouseLeftButtonUp += (s, e) =>
            {
                if (!_isMouseDragging) onClick();
            };

            if (imageUri == null)
            {
                // None icon (circle with diagonal slash)
                var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var ellipse = new System.Windows.Shapes.Ellipse
                {
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA0A0AA")),
                    StrokeThickness = 2.0,
                    Width = 42,
                    Height = 42
                };
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 15,37 L 37,15"),
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA0A0AA")),
                    StrokeThickness = 2.0
                };
                grid.Children.Add(ellipse);
                grid.Children.Add(path);
                border.Child = grid;
            }
            else
            {
                var img = new Image
                {
                    Stretch = isFrame ? Stretch.Uniform : Stretch.UniformToFill,
                    Margin = isFrame ? new Thickness(6) : new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                border.Child = img;

                string urlStr = imageUri.ToString();
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    var src = await LoadImageAsync(urlStr);
                    if (src != null) img.Source = src;
                });
            }

            var text = new TextBlock
            {
                Text = title,
                FontFamily = new System.Windows.Media.FontFamily("Poppins"),
                FontSize = 16,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = isSelected ? ActiveBorder : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1F2937")),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Width = 115,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 44,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 6, 0, 0)
            };

            stack.Children.Add(border);
            stack.Children.Add(text);
            return stack;
        }

        private async void SelectFrameItem(WatermarkFrame frame)
        {
            bool isNone = frame.Id == 0 || string.Equals(frame.FrameName, "none", StringComparison.OrdinalIgnoreCase);
            _selectedFrame = isNone ? "none" : frame.FrameName;
            _selectedFrameId = isNone ? 0 : frame.Id;

            if (isNone)
            {
                _selectedFrameImageUrl = null;
                if (FrameOverlay != null)
                {
                    FrameOverlay.Source = null;
                    FrameOverlay.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                var uri = GetFrameImageUri(frame);
                if (uri != null)
                {
                    _selectedFrameImageUrl = uri.ToString();
                    var src = await LoadImageAsync(_selectedFrameImageUrl);
                    if (FrameOverlay != null && _selectedFrame == frame.FrameName)
                    {
                        FrameOverlay.Source = src;
                        FrameOverlay.Visibility = Visibility.Visible;
                    }
                }
            }

            RenderFrameButtons();
        }

        private void SetCapturedPhotoSource(string photoPath)
        {
            try
            {
                if (!File.Exists(photoPath)) return;
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(photoPath);
                bitmap.EndInit();
                if (CapturedPhoto != null)
                {
                    CapturedPhoto.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Error loading photo source: " + ex.Message);
            }
        }

        private async void SelectBackgroundItem(BackgroundItem background)
        {
            bool isNone = background.Id == 0 || string.Equals(background.BackgroundName, "none", StringComparison.OrdinalIgnoreCase);
            _selectedBackground = isNone ? "none" : background.BackgroundName;

            if (isNone)
            {
                _selectedBackgroundImageUrl = null;
                if (BackgroundImage != null)
                {
                    BackgroundImage.Source = null;
                    BackgroundImage.Visibility = Visibility.Collapsed;
                }
                if (PhotoBorder != null)
                {
                    PhotoBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF9FAFB"));
                }
                if (CapturedPhoto != null)
                {
                    CapturedPhoto.Opacity = 1.0;
                    SetCapturedPhotoSource(_capturedPhotoPath);
                }
            }
            else
            {
                var uri = GetBackgroundImageUri(background);
                _selectedBackgroundImageUrl = uri?.ToString();

                // Show background image behind photo cutout
                if (BackgroundImage != null && !string.IsNullOrEmpty(_selectedBackgroundImageUrl))
                {
                    var bgSource = await LoadImageAsync(_selectedBackgroundImageUrl);
                    if (_selectedBackground == background.BackgroundName)
                    {
                        BackgroundImage.Source = bgSource;
                        BackgroundImage.Visibility = Visibility.Visible;
                    }
                }

                // Show loading indicator while background removal completes
                if (BgLoadingOverlay != null && _removedBgPhotoPath == null)
                {
                    BgLoadingOverlay.Visibility = Visibility.Visible;
                }
                Cursor = Cursors.Wait;

                try
                {
                    if (_removedBgPhotoPath == null)
                    {
                        _removedBgPhotoPath = await RemoveBackgroundOnceAsync(_capturedPhotoPath);
                    }

                    if (!string.IsNullOrEmpty(_removedBgPhotoPath) && File.Exists(_removedBgPhotoPath) && _removedBgPhotoPath != _capturedPhotoPath)
                    {
                        SetCapturedPhotoSource(_removedBgPhotoPath);
                    }
                    if (PhotoBorder != null)
                    {
                        PhotoBorder.Background = System.Windows.Media.Brushes.Transparent;
                    }
                }
                finally
                {
                    if (BgLoadingOverlay != null)
                    {
                        BgLoadingOverlay.Visibility = Visibility.Collapsed;
                    }
                    Cursor = Cursors.Arrow;
                }
            }

            RenderBackgroundButtons();
        }

        private async Task<string?> RemoveBackgroundOnceAsync(string photoPath)
        {
            try
            {
                if (!File.Exists(photoPath))
                {
                    AppLogger.Log($"Photo not found for background removal: {photoPath}");
                    return photoPath;
                }

                if (LocalBackgroundRemovalService.Instance.IsModelAvailable)
                {
                  
                    string? localResult = await LocalBackgroundRemovalService.Instance.RemoveBackgroundAsync(photoPath);
                    if (!string.IsNullOrEmpty(localResult) && File.Exists(localResult))
                    {
                        AppLogger.Log($"Local background removal successful: {localResult}");
                        return localResult;
                    }
                    AppLogger.Log("Local background removal returned null.");
                }
                else
                {
                    AppLogger.Log("Local ONNX model file not found in Models directory.");
                }

                return photoPath;
            }
            catch (Exception ex)
            {
                AppLogger.Log("Background removal exception: " + ex.Message);
                return photoPath;
            }
        }

        private string BakeFrameOntoPhoto(string photoPath, string? frameImageUrl, string? logoImageUrl = null, string? backgroundImageUrl = null)
        {
            try
            {
                if (!File.Exists(photoPath)) return photoPath;

                using var baseImage = System.Drawing.Image.FromFile(photoPath);

                // Target portrait aspect ratio matching kiosk viewport (550x872 = ~1:1.585)
                double targetAspect = 550.0 / 872.0;

                int outWidth, outHeight;
                Rectangle srcRect;

                double srcAspect = (double)baseImage.Width / baseImage.Height;

                if (srcAspect > targetAspect)
                {
                    // Source image is wider than target portrait (e.g. landscape camera photo 2592x1944):
                    // Match WPF Stretch="UniformToFill": fit full height, crop center horizontally
                    int cropWidth = (int)Math.Round(baseImage.Height * targetAspect);
                    int cropX = (baseImage.Width - cropWidth) / 2;
                    srcRect = new Rectangle(cropX, 0, cropWidth, baseImage.Height);
                    outWidth = cropWidth;
                    outHeight = baseImage.Height;
                }
                else
                {
                    // Source image is taller than target portrait:
                    // Match WPF Stretch="UniformToFill": fit full width, crop center vertically
                    int cropHeight = (int)Math.Round(baseImage.Width / targetAspect);
                    int cropY = (baseImage.Height - cropHeight) / 2;
                    srcRect = new Rectangle(0, cropY, baseImage.Width, cropHeight);
                    outWidth = baseImage.Width;
                    outHeight = cropHeight;
                }

                using var bitmap = new Bitmap(outWidth, outHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bitmap);

                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.Clear(System.Drawing.Color.White);

                // Helper: load image from URL or file path
                System.Drawing.Image? LoadOverlay(string? url)
                {
                    if (string.IsNullOrEmpty(url)) return null;
                    try
                    {
                        url = NormalizeUrl(url);
                        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            using var hc = new HttpClient();
                            var bytes = hc.GetByteArrayAsync(url).GetAwaiter().GetResult();
                            using var ms = new MemoryStream(bytes);
                            return new Bitmap(ms);
                        }
                        string localPath = url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                            ? new Uri(url).LocalPath : url;
                        return File.Exists(localPath) ? System.Drawing.Image.FromFile(localPath) : null;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"LoadOverlay failed for [{url}]: {ex.Message}");
                        return null;
                    }
                }

                // 1. Draw background image first (behind photo) if selected
                if (!string.IsNullOrEmpty(backgroundImageUrl))
                {
                    using var bgImg = LoadOverlay(backgroundImageUrl);
                    if (bgImg != null) g.DrawImage(bgImg, 0, 0, outWidth, outHeight);
                }

                // 2. Draw base photo cropped to portrait ratio (matching UniformToFill in Edit viewport)
                g.DrawImage(baseImage, new Rectangle(0, 0, outWidth, outHeight), srcRect, GraphicsUnit.Pixel);

                // 3. Draw frame overlay across the entire portrait canvas
                using (var frameImg = LoadOverlay(frameImageUrl))
                {
                    if (frameImg != null) g.DrawImage(frameImg, 0, 0, outWidth, outHeight);
                }

                // 4. Draw logo overlay (watermark on top)
                using (var logoImg = LoadOverlay(logoImageUrl))
                {
                    if (logoImg != null) g.DrawImage(logoImg, 0, 0, outWidth, outHeight);
                }

                string capturesFolder = PrintPreview.GetCapturesFolder();

                string bakedFile = Path.Combine(capturesFolder, $"Capture_Baked_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                bitmap.Save(bakedFile, System.Drawing.Imaging.ImageFormat.Jpeg);

                return bakedFile;
            }
            catch (Exception ex)
            {
                AppLogger.Log("BakeFrameOntoPhoto Exception: " + ex.Message);
                return photoPath;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService != null && NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
            else
            {
                NavigationService?.Navigate(new Uri("Views/Camera.xaml", UriKind.RelativeOrAbsolute));
            }
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            bool hasBg = !string.IsNullOrEmpty(_selectedBackground) &&
                         !string.Equals(_selectedBackground, "none", StringComparison.OrdinalIgnoreCase);

            string sourcePhoto = (hasBg && !string.IsNullOrEmpty(_removedBgPhotoPath) && File.Exists(_removedBgPhotoPath))
                ? _removedBgPhotoPath
                : _capturedPhotoPath;

            string? bgUrlToBake = hasBg ? _selectedBackgroundImageUrl : null;

            // Bake background + photo + frame + logo overlays onto the photo before navigating
            _bakedPhotoPath = BakeFrameOntoPhoto(sourcePhoto, _selectedFrameImageUrl, _selectedLogoImageUrl, bgUrlToBake);

            NavigationService?.Navigate(new PrintPreview(_bakedPhotoPath, _selectedFrame, _selectedBackground, _selectedFrameId, _selectedLogoId));
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private Point _mouseScrollStart;
        private double _mouseScrollStartOffset;
        private bool _isMouseDragging = false;
        private ScrollViewer? _activeScrollViewer = null;

        private void ScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ScrollViewer sv && e.LeftButton == MouseButtonState.Pressed)
            {
                _activeScrollViewer = sv;
                _mouseScrollStart = e.GetPosition(sv);
                _mouseScrollStartOffset = sv.HorizontalOffset;
                _isMouseDragging = false;
            }
        }

        private void ScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_activeScrollViewer != null && e.LeftButton == MouseButtonState.Pressed)
            {
                Point current = e.GetPosition(_activeScrollViewer);
                double deltaX = current.X - _mouseScrollStart.X;
                if (Math.Abs(deltaX) > 6)
                {
                    _isMouseDragging = true;
                    _activeScrollViewer.ScrollToHorizontalOffset(_mouseScrollStartOffset - deltaX);
                }
            }
        }

        private void ScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _activeScrollViewer = null;
            if (_isMouseDragging)
            {
                Dispatcher.BeginInvoke(new Action(() => _isMouseDragging = false), System.Windows.Threading.DispatcherPriority.Background);
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
                AppLogger.Log("Edit UpdatePaymentProgressText error: " + ex.Message);
            }
        }
    }
}
