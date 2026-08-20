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
        private string? _selectedFrameImageUrl = null;
        private string? _selectedBackgroundImageUrl = null;
        private string? _removedBgPhotoPath = null;
        private string? _bakedPhotoPath = null;

        private List<WatermarkFrame> _availableFrames = new();
        private List<WatermarkFrame> _availableBackgrounds = new();

        private static readonly Brush GrayBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE5E7EB"));
        private static readonly Brush ActiveBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5C6BFF"));

        public Edit()
        {
            InitializeComponent();
        }

        public Edit(string imagePath) : this()
        {
            _capturedPhotoPath = imagePath;

            // Load captured image file safely
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                try
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.EndInit();
                    CapturedPhoto.Source = bitmap;
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Error loading captured image: " + ex.Message);
                }
            }

            Loaded += async (s, e) =>
            {
                await LoadWatermarksFromBackendAsync();
            };
        }

        private async Task LoadWatermarksFromBackendAsync()
        {
            var session = kuboPixSessionManager.Load();
            string token = session?.Token ?? "";

            _availableFrames.Clear();
            _availableBackgrounds.Clear();

            // Default 'None' item for frames
            _availableFrames.Add(new WatermarkFrame { Id = 0, FrameName = "none", FrameImage = "", Status = 1, Type = "frame" });

            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    string apiUrl = $"{AppConfig.ApiBaseUrl}api/get/watermarks";
                    AppLogger.Log($"Calling watermarks API: {apiUrl} | Token present: {!string.IsNullOrEmpty(token)}");

                    var resp = await client.GetAsync(apiUrl);
                    AppLogger.Log($"Watermarks API HTTP status: {(int)resp.StatusCode} {resp.StatusCode}");

                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        AppLogger.Log("Watermarks API raw response: " + json);

                        JObject? result = JsonConvert.DeserializeObject<JObject>(json);

                        if (result != null && (int?)result["status"] == 1 && result["data"] != null)
                        {
                            var data = result["data"];

                            // API returns a flat list of logos and frames (no backgrounds)
                            // Parse as JArray directly
                            JArray? flatList = data as JArray;
                            if (flatList != null)
                            {
                                var items = flatList.ToObject<List<WatermarkFrame>>();
                                if (items != null)
                                {
                                    foreach (var item in items.Where(x => x.Status == 1))
                                    {
                                        // TypeId 1 = Frame → show in frame panel
                                        // TypeId 2 = Watermark/Logo → show in logo panel
                                        if (item.TypeId == WatermarkFrame.TypeWatermark)
                                        {
                                            item.Type = "background";
                                            _availableBackgrounds.Add(item);
                                        }
                                        else // TypeId == 1 (Frame) or unknown
                                        {
                                            item.Type = "frame";
                                            _availableFrames.Add(item);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Fallback: old nested format {frames:[], backgrounds:[]}
                                if (data?["frames"] != null)
                                {
                                    var framesList = data["frames"]!.ToObject<List<WatermarkFrame>>();
                                    if (framesList != null)
                                        foreach (var f in framesList.Where(x => x.Status == 1))
                                            _availableFrames.Add(f);
                                }

                                if (data?["backgrounds"] != null)
                                {
                                    var bgList = data["backgrounds"]!.ToObject<List<WatermarkFrame>>();
                                    if (bgList != null)
                                        foreach (var bg in bgList.Where(x => x.Status == 1))
                                        {
                                            bg.Type = "background";
                                            _availableBackgrounds.Add(bg);
                                        }
                                }
                            }

                            AppLogger.Log($"Watermarks loaded — Frames: {_availableFrames.Count - 1}, Backgrounds: {_availableBackgrounds.Count}");
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

            // Fallback frames if API returned nothing
            if (_availableFrames.Count == 1)
            {
                AppLogger.Log("No remote frames found — using local fallback frames.");
                _availableFrames.Add(new WatermarkFrame { Id = -1, FrameName = "Ellipse", FrameImage = "Ellipse 5@2x.png", Status = 1, Type = "frame" });
                _availableFrames.Add(new WatermarkFrame { Id = -2, FrameName = "Bottle", FrameImage = "750mL Bottles (3)@2x.png", Status = 1, Type = "frame" });
            }

            // Fallback backgrounds if API returned nothing
            if (_availableBackgrounds.Count == 0)
            {
                AppLogger.Log("No remote backgrounds found — using local fallback backgrounds.");
                _availableBackgrounds.Add(new WatermarkFrame { Id = -1, FrameName = "Winter", FrameImage = "Ellipse 5@2x.png", Status = 1, Type = "background" });
                _availableBackgrounds.Add(new WatermarkFrame { Id = -2, FrameName = "X-mas", FrameImage = "750mL Bottles (3)@2x.png", Status = 1, Type = "background" });
                _availableBackgrounds.Add(new WatermarkFrame { Id = -3, FrameName = "Summer", FrameImage = "Ellipse 5@2x.png", Status = 1, Type = "background" });
            }

            RenderFrameButtons();
            RenderBackgroundButtons();
        }

        private void RenderFrameButtons()
        {
            if (FrameWrapPanel == null) return;
            FrameWrapPanel.Children.Clear();

            foreach (var frame in _availableFrames)
            {
                var card = CreateThumbnailCard(
                    frame.FrameName,
                    GetFrameImageUri(frame),
                    frame.FrameName == _selectedFrame,
                    () => SelectFrameItem(frame)
                );
                FrameWrapPanel.Children.Add(card);
            }
        }

        private Uri? GetFrameImageUri(WatermarkFrame frame)
        {
            if (string.IsNullOrEmpty(frame.FrameImage)) return null;

            if (frame.Id < 0)
            {
                // Local asset fallback
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", frame.FrameImage);
                if (!File.Exists(path))
                {
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", frame.FrameImage);
                }
                return File.Exists(path) ? new Uri(path, UriKind.Absolute) : null;
            }

            // Remote URL
            return new Uri($"{AppConfig.ApiBaseUrl}Content/Uploads/WaterMarks/Frames/{frame.FrameImage}", UriKind.Absolute);
        }

        private void RenderBackgroundButtons()
        {
            if (BackgroundWrapPanel == null || BackgroundSectionHeader == null) return;

            BackgroundWrapPanel.Children.Clear();

            if (_availableBackgrounds.Count == 0)
            {
                BackgroundSectionHeader.Visibility = Visibility.Collapsed;
                BackgroundWrapPanel.Visibility = Visibility.Collapsed;
                return;
            }
 
            BackgroundSectionHeader.Visibility = Visibility.Visible;
            BackgroundWrapPanel.Visibility = Visibility.Visible;

            // 'None' background option
            if (!_availableBackgrounds.Any(x => x.FrameName == "none"))
            {
                _availableBackgrounds.Insert(0, new WatermarkFrame { Id = 0, FrameName = "none", FrameImage = "", Status = 1, Type = "background" });
            }

            foreach (var bg in _availableBackgrounds)
            {
                Uri? uri = string.IsNullOrEmpty(bg.FrameImage)
                    ? null
                    : new Uri($"{AppConfig.ApiBaseUrl}Content/Uploads/WaterMarks/Logos/{bg.FrameImage}", UriKind.Absolute);

                var card = CreateThumbnailCard(
                    bg.FrameName,
                    uri,
                    bg.FrameName == _selectedBackground,
                    () => SelectBackgroundItem(bg)
                );
                BackgroundWrapPanel.Children.Add(card);
            }
        }

        private FrameworkElement CreateThumbnailCard(string title, Uri? imageUri, bool isSelected, Action onClick)
        {
            var stack = new StackPanel
            {
                Margin = new Thickness(0, 0, 18, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var border = new Border
            {
                BorderBrush = isSelected ? ActiveBorder : GrayBorder,
                BorderThickness = new Thickness(isSelected ? 2.0 : 1.5),
                CornerRadius = new CornerRadius(10),
                Width = 94,
                Height = 94,
                Background = System.Windows.Media.Brushes.White,
                Cursor = Cursors.Hand
            };

            border.MouseLeftButtonDown += (s, e) => onClick();

            if (imageUri == null)
            {
                // None icon (circle with diagonal slash)
                var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var ellipse = new System.Windows.Shapes.Ellipse
                {
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA0A0AA")),
                    StrokeThickness = 1.5,
                    Width = 38,
                    Height = 38
                };
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 14,34 L 34,14"),
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA0A0AA")),
                    StrokeThickness = 1.5
                };
                grid.Children.Add(ellipse);
                grid.Children.Add(path);
                border.Child = grid;
            }
            else
            {
                var isRemoteUrl = imageUri.Scheme == Uri.UriSchemeHttp || imageUri.Scheme == Uri.UriSchemeHttps;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = imageUri;
                if (!isRemoteUrl)
                {
                    // Local file: load immediately (safe to block)
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                }
                else
                {
                    // Remote URL: load asynchronously — OnLoad throws for HTTP URIs in WPF
                    bmp.CreateOptions = BitmapCreateOptions.None;
                }
                bmp.EndInit();

                // Log failures for remote images without crashing the card loop
                if (isRemoteUrl)
                {
                    bmp.DownloadFailed += (s, e) =>
                        AppLogger.Log($"Frame thumbnail download failed [{imageUri}]: {e.ErrorException?.Message}");
                }

                var img = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(10)
                };
                border.Child = img;
            }

            var text = new TextBlock
            {
                Text = title,
                FontFamily = new System.Windows.Media.FontFamily("Poppins"),
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA0A0AA")),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };

            stack.Children.Add(border);
            stack.Children.Add(text);
            return stack;
        }

        private void SelectFrameItem(WatermarkFrame frame)
        {
            _selectedFrame = frame.FrameName;

            if (frame.FrameName == "none")
            {
                _selectedFrameImageUrl = null;
                FrameOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                var uri = GetFrameImageUri(frame);
                if (uri != null)
                {
                    _selectedFrameImageUrl = uri.ToString();
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = uri;
                    bitmap.EndInit();

                    FrameOverlay.Source = bitmap;
                    FrameOverlay.Visibility = Visibility.Visible;
                }
            }

            RenderFrameButtons();
        }

        private async void SelectBackgroundItem(WatermarkFrame background)
        {
            _selectedBackground = background.FrameName;

            if (background.FrameName == "none")
            {
                _selectedBackgroundImageUrl = null;
                PhotoBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF9FAFB"));
                if (CapturedPhoto != null) CapturedPhoto.Opacity = 1.0;
            }
            else
            {
                _selectedBackgroundImageUrl = $"{AppConfig.ApiBaseUrl}Content/Uploads/WaterMarks/Logos/{background.FrameImage}";

                // Call ClearBackdrop API once if not already removed
                if (_removedBgPhotoPath == null)
                {
                    _removedBgPhotoPath = await RemoveBackgroundOnceAsync(_capturedPhotoPath);
                }

                if (!string.IsNullOrEmpty(_removedBgPhotoPath) && File.Exists(_removedBgPhotoPath))
                {
                    try
                    {
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(_removedBgPhotoPath);
                        bitmap.EndInit();
                        CapturedPhoto.Source = bitmap;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("Error updating photo with transparent background: " + ex.Message);
                    }
                }
            }

            RenderBackgroundButtons();
        }

        private async Task<string?> RemoveBackgroundOnceAsync(string photoPath)
        {
            try
            {
                string apiKey = ConfigurationManager.AppSettings["ClearBackdropApiKey"] ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    AppLogger.Log("ClearBackdropApiKey not set; skipping background removal.");
                    return photoPath;
                }

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                using var form = new MultipartFormDataContent();
                byte[] bytes = await File.ReadAllBytesAsync(photoPath);
                form.Add(new ByteArrayContent(bytes), "image_file", Path.GetFileName(photoPath));

                var response = await client.PostAsync("https://api.clearbackdrop.com/remove", form);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log($"ClearBackdrop API non-success status: {response.StatusCode}");
                    return photoPath;
                }

                byte[] pngBytes = await response.Content.ReadAsByteArrayAsync();
                string tempPath = Path.Combine(Path.GetTempPath(), $"removed_bg_{Guid.NewGuid()}.png");
                await File.WriteAllBytesAsync(tempPath, pngBytes);
                return tempPath;
            }
            catch (Exception ex)
            {
                AppLogger.Log("ClearBackdrop exception: " + ex.Message);
                return photoPath;
            }
        }

        private string BakeFrameOntoPhoto(string photoPath, string? frameImageUrl)
        {
            try
            {
                if (!File.Exists(photoPath)) return photoPath;

                using var baseImage = System.Drawing.Image.FromFile(photoPath);
                int width = baseImage.Width;
                int height = baseImage.Height;

                using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bitmap);

                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;

                // Draw photo
                g.DrawImage(baseImage, 0, 0, width, height);

                // Overlay frame if selected
                if (!string.IsNullOrEmpty(frameImageUrl))
                {
                    System.Drawing.Image? frameImg = null;
                    if (frameImageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        frameImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        using var client = new HttpClient();
                        var stream = client.GetStreamAsync(frameImageUrl).GetAwaiter().GetResult();
                        frameImg = System.Drawing.Image.FromStream(stream);
                    }
                    else if (frameImageUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        string localFile = new Uri(frameImageUrl).LocalPath;
                        if (File.Exists(localFile))
                        {
                            frameImg = System.Drawing.Image.FromFile(localFile);
                        }
                    }
                    else if (File.Exists(frameImageUrl))
                    {
                        frameImg = System.Drawing.Image.FromFile(frameImageUrl);
                    }

                    if (frameImg != null)
                    {
                        using (frameImg)
                        {
                            g.DrawImage(frameImg, 0, 0, width, height);
                        }
                    }
                }

                string capturesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Captures");
                if (!Directory.Exists(capturesFolder)) Directory.CreateDirectory(capturesFolder);

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
            string sourcePhoto = _removedBgPhotoPath ?? _capturedPhotoPath;
            _bakedPhotoPath = BakeFrameOntoPhoto(sourcePhoto, _selectedFrameImageUrl);

            NavigationService?.Navigate(new PrintPreview(_bakedPhotoPath, _selectedFrame, _selectedBackground));
        }
    }
}
