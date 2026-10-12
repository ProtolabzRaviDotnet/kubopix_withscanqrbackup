using kuboPix.Helpers;
using kuboPix.ViewModels;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace kuboPix.Services
{
    public static class KuboPixContentService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly SemaphoreSlim _syncLock = new SemaphoreSlim(1, 1);

        public static readonly string ContentCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KuboPix", "Content");

        public static BitmapImage? CurrentStartBackground { get; private set; }
        private static string _currentBackgroundUrl = string.Empty;

        public static bool IsPrivacyPolicyEnabled { get; private set; }
        public static BitmapImage? CurrentPrivacyPolicyImage { get; private set; }
        private static string _currentPrivacyPolicyUrl = string.Empty;

        public static readonly string IdleVideoPath = Path.Combine(ContentCacheDir, "idle_video.mp4");
        private static readonly string IdleVideoUrlFile = Path.Combine(ContentCacheDir, "idle_video_url.txt");

        public static event Action<BitmapImage?>? OnStartBackgroundUpdated;
        public static event Action<string>? OnIdleVideoUpdated;

        static KuboPixContentService()
        {
            try
            {
                Directory.CreateDirectory(ContentCacheDir);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[ContentService] Error creating cache directory: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the in-memory cached start background image directly fetched from the server (zero disk storage).
        /// </summary>
        public static BitmapImage? GetCachedStartBackground()
        {
            return CurrentStartBackground;
        }

        /// <summary>
        /// Returns the current in-memory cached Privacy Policy state (IsEnabled, Image).
        /// </summary>
        public static (bool IsEnabled, BitmapImage? Image) GetCachedPrivacyPolicy()
        {
            return (IsPrivacyPolicyEnabled, CurrentPrivacyPolicyImage);
        }

        /// <summary>
        /// Ensures the Privacy Policy content is fetched using the session token.
        /// Can be called directly from PasswordDialog without needing any direct URLs in the dialog.
        /// </summary>
        public static async Task<(bool IsEnabled, BitmapImage? Image)> GetPrivacyPolicyAsync(string? jwtToken = null)
        {
            string? token = jwtToken ?? kuboPixSessionManager.Load()?.Token;
            if (!string.IsNullOrWhiteSpace(token))
            {
                await SyncContentAsync(token);
            }
            return (IsPrivacyPolicyEnabled, CurrentPrivacyPolicyImage);
        }

        /// <summary>
        /// Returns the absolute path to the cached idle video if it exists on disk, otherwise null.
        /// </summary>
        public static string? GetCachedIdleVideoPath()
        {
            try
            {
                if (File.Exists(IdleVideoPath))
                {
                    var fileInfo = new FileInfo(IdleVideoPath);
                    if (fileInfo.Length > 0)
                        return IdleVideoPath;
                }

                // Fallback to existing video in parent cache dir (%AppData%\KuboPix\idle_video.mp4)
                string legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KuboPix", "idle_video.mp4");
                if (File.Exists(legacyPath))
                {
                    var fileInfo = new FileInfo(legacyPath);
                    if (fileInfo.Length > 0)
                        return legacyPath;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[ContentService] Error checking cached video path: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Synchronizes dynamic content (Start Screen BG and Idle Video) from the server.
        /// Compares the remote URLs against local tracking files to avoid redundant downloads.
        /// </summary>
        public static async Task SyncContentAsync(string? jwtToken = null)
        {
            string? token = !string.IsNullOrWhiteSpace(jwtToken)
                ? jwtToken
                : kuboPixSessionManager.Load()?.Token;

            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            if (!await _syncLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(ContentCacheDir);

                string apiUrl = $"{AppConfig.ApiBaseUrl.TrimEnd('/')}/api/miniapp/content?platformTypeId=2";

                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log($"[ContentService] Content API failed with status: {response.StatusCode}");
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<kuboPixContentResponse>(json);
                if (result == null || result.status != 1 || result.data == null)
                {
                    AppLogger.Log("[ContentService] Content API returned invalid or unsuccessful payload.");
                    return;
                }

                // 1. Process Start Screen Background (ContentTypeId == 1)
                var bgItem = result.data.FirstOrDefault(x =>
                    x.ContentTypeId == 1 ||
                    string.Equals(x.ContentTypeName, "StartBackground", StringComparison.OrdinalIgnoreCase));

                if (bgItem != null)
                {
                    string? bgUrl = !string.IsNullOrWhiteSpace(bgItem.CurrentUrl)
                        ? bgItem.CurrentUrl
                        : (bgItem.UseCustom ? bgItem.CustomUrl : bgItem.DefaultUrl);

                    if (!string.IsNullOrWhiteSpace(bgUrl))
                    {
                        await SyncStartBackgroundAsync(bgUrl);
                    }
                    else
                    {
                        CurrentStartBackground = null;
                        _currentBackgroundUrl = string.Empty;
                        OnStartBackgroundUpdated?.Invoke(null);
                    }
                }

                // 2. Process Idle Video (ContentTypeId == 2)
                var videoItem = result.data.FirstOrDefault(x =>
                    x.ContentTypeId == 2 ||
                    string.Equals(x.ContentTypeName, "IdleVideo", StringComparison.OrdinalIgnoreCase));

                if (videoItem != null)
                {
                    string? videoUrl = !string.IsNullOrWhiteSpace(videoItem.CurrentUrl)
                        ? videoItem.CurrentUrl
                        : (videoItem.UseCustom ? videoItem.CustomUrl : videoItem.DefaultUrl);

                    if (!string.IsNullOrWhiteSpace(videoUrl))
                    {
                        await SyncIdleVideoAsync(videoUrl);
                    }
                }

                // 3. Process Privacy Policy (ContentTypeId == 3)
                var privacyItem = result.data.FirstOrDefault(x =>
                    x.ContentTypeId == 3 ||
                    string.Equals(x.ContentTypeName, "PrivacyPolicy", StringComparison.OrdinalIgnoreCase));

                if (privacyItem != null)
                {
                    await SyncPrivacyPolicyAsync(privacyItem);
                }
                else
                {
                    IsPrivacyPolicyEnabled = false;
                    CurrentPrivacyPolicyImage = null;
                    _currentPrivacyPolicyUrl = string.Empty;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[ContentService] SyncContentAsync error: {ex.Message}");
            }
            finally
            {
                _syncLock.Release();
            }
        }

        private static async Task SyncPrivacyPolicyAsync(kuboPixContentItem item)
        {
            try
            {
                IsPrivacyPolicyEnabled = item.IsEnabled;

                if (!item.IsEnabled || string.IsNullOrWhiteSpace(item.CurrentUrl))
                {
                    CurrentPrivacyPolicyImage = null;
                    _currentPrivacyPolicyUrl = string.Empty;
                    return;
                }

                string fullUrl = ResolveFullUrl(item.CurrentUrl);

                if (CurrentPrivacyPolicyImage != null && string.Equals(_currentPrivacyPolicyUrl, fullUrl, StringComparison.OrdinalIgnoreCase))
                {
                    // Already up-to-date in memory
                    return;
                }

                byte[] bytes = await _httpClient.GetByteArrayAsync(fullUrl);
                if (bytes == null || bytes.Length == 0)
                {
                    CurrentPrivacyPolicyImage = null;
                    return;
                }

                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                CurrentPrivacyPolicyImage = bmp;
                _currentPrivacyPolicyUrl = fullUrl;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[ContentService] SyncPrivacyPolicyAsync error: {ex.Message}");
            }
        }

        private static async Task SyncStartBackgroundAsync(string relativeOrAbsoluteUrl)
        {
            try
            {
                string fullUrl = ResolveFullUrl(relativeOrAbsoluteUrl);

                if (CurrentStartBackground != null && string.Equals(_currentBackgroundUrl, fullUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                byte[] bytes = await _httpClient.GetByteArrayAsync(fullUrl);
                if (bytes == null || bytes.Length == 0)
                {
                    AppLogger.Log("[ContentService] Fetched start background was empty.");
                    return;
                }

                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                CurrentStartBackground = bmp;
                _currentBackgroundUrl = fullUrl;

                OnStartBackgroundUpdated?.Invoke(bmp);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[ContentService] SyncStartBackgroundAsync error: {ex.Message}");
            }
        }

        private static async Task SyncIdleVideoAsync(string relativeOrAbsoluteUrl)
        {
            try
            {
                string fullUrl = ResolveFullUrl(relativeOrAbsoluteUrl);
                string cachedUrl = File.Exists(IdleVideoUrlFile)
                    ? File.ReadAllText(IdleVideoUrlFile).Trim()
                    : string.Empty;

                if (File.Exists(IdleVideoPath) && string.Equals(cachedUrl, fullUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                using var videoResponse = await _httpClient.GetAsync(fullUrl, HttpCompletionOption.ResponseHeadersRead);
                videoResponse.EnsureSuccessStatusCode();

                string tempFile = IdleVideoPath + ".tmp";
                using (var stream = await videoResponse.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await stream.CopyToAsync(fileStream);
                }

                File.Move(tempFile, IdleVideoPath, true);
                await File.WriteAllTextAsync(IdleVideoUrlFile, fullUrl);

                OnIdleVideoUpdated?.Invoke(IdleVideoPath);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[ContentService] SyncIdleVideoAsync error: {ex.Message}");
            }
        }

        public static string ResolveFullUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            string baseUrl = AppConfig.ApiBaseUrl.TrimEnd('/');
            return $"{baseUrl}/{url.TrimStart('/')}";
        }
    }
}
