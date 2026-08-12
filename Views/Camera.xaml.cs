using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace kuboPix.Views
{
    /// <summary>
    /// Interaction logic for Camera.xaml
    /// </summary>
    public partial class Camera : Page
    {
        private CamHelp.VideoStreamCallBack? _videoStreamCallBack;
        private WriteableBitmap? _previewBitmap;
        private int _currentWidth = 0;
        private int _currentHeight = 0;
        private bool _isCameraRunning = false;

        public Camera()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize SDK
            int ret = CamHelp.HIGH_InitSDK();
            if (ret != 0)
            {
                MessageBox.Show("Failed to initialize Camera SDK!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Register frame callback
            _videoStreamCallBack = new CamHelp.VideoStreamCallBack(VideoStreamCB);
            CamHelp.HIGH_SetVideoStreamCallBack(_videoStreamCallBack);

            // Set camera resolution
            try
            {
                int resCount = CamHelp.HIGH_GetPixelNum();
                if (resCount > 0)
                {
                    // Choose first resolution (typically highest/default)
                    int hPixel = CamHelp.HIGH_GetHPixelByIndex(0);
                    int vPixel = CamHelp.HIGH_GetVPixelByIndex(0);
                    CamHelp.HIGH_SetCameraPixel(hPixel, vPixel);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error setting camera pixel: " + ex.Message);
            }

            // Start Camera Stream
            int startRet = CamHelp.HIGH_StartCamera();
            if (startRet != 0)
            {
                MessageBox.Show("Failed to start camera preview!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Clean up if start fails
                CamHelp.HIGH_SetVideoStreamCallBack(null);
                CamHelp.HIGH_ReleaseSDK();
                return;
            }

            // Rotate camera feed 180° to correct upside-down image
            CamHelp.HIGH_SetRotateSize(2); // 0 = 0°, 1 = 90°, 2 = 180°, 3 = 270°

            // Reset processing overlay, captured preview and buttons
            if (ProcessingOverlay != null) ProcessingOverlay.Visibility = Visibility.Collapsed;
            if (CapturedPhotoPreview != null)
            {
                CapturedPhotoPreview.Visibility = Visibility.Collapsed;
                CapturedPhotoPreview.Source = null;
            }
            if (CaptureButton != null) CaptureButton.IsEnabled = true;
            if (BackButton != null) BackButton.IsEnabled = true;

            _isCameraRunning = true;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            // Clean up camera stream and release SDK resources
            if (_isCameraRunning)
            {
                try
                {
                    CamHelp.HIGH_SetVideoStreamCallBack(null);
                    CamHelp.HIGH_StopCamera();
                    CamHelp.HIGH_CloseWindow();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error stopping camera: " + ex.Message);
                }
                finally
                {
                    _isCameraRunning = false;
                }
            }

            try
            {
                CamHelp.HIGH_ReleaseSDK();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error releasing SDK: " + ex.Message);
            }

            _previewBitmap = null;
            _videoStreamCallBack = null;
        }

        private void VideoStreamCB(IntPtr rgbData, int iWidth, int iHeight)
        {
            if (rgbData == IntPtr.Zero || iWidth <= 0 || iHeight <= 0) return;

            int stride = iWidth * 3;
            int bufferSize = stride * iHeight;

            // Copy pixels safely to a managed buffer before returning from the callback thread
            byte[] managedPixels = new byte[bufferSize];
            System.Runtime.InteropServices.Marshal.Copy(rgbData, managedPixels, 0, bufferSize);

            // Switch to the main UI thread to render the frame using the safe managed buffer
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (CameraPreview == null) return;

                // Hide placeholder UI upon receiving the first frame
                if (CameraPlaceholder.Visibility == Visibility.Visible)
                {
                    CameraPlaceholder.Visibility = Visibility.Collapsed;
                }

                // Initialize or recreate WriteableBitmap if size changes
                if (_previewBitmap == null || _currentWidth != iWidth || _currentHeight != iHeight)
                {
                    _previewBitmap = new WriteableBitmap(iWidth, iHeight, 96, 96, PixelFormats.Bgr24, null);
                    CameraPreview.Source = _previewBitmap;
                    _currentWidth = iWidth;
                    _currentHeight = iHeight;
                }

                // Render frame pixels safely from the managed buffer
                _previewBitmap.WritePixels(new Int32Rect(0, 0, iWidth, iHeight), managedPixels, stride, 0);
            }));
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService != null && NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
            else
            {
                NavigationService?.Navigate(new Uri("Views/Capture.xaml", UriKind.RelativeOrAbsolute));
            }
        }

        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isCameraRunning)
            {
                MessageBox.Show("Camera is not active.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Show processing overlay
            if (ProcessingOverlay != null)
            {
                ProcessingOverlay.Visibility = Visibility.Visible;
            }
            if (CaptureButton != null) CaptureButton.IsEnabled = false;
            if (BackButton != null) BackButton.IsEnabled = false;

            try
            {
                // Create Captures folder in Application base directory
                string capturesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Captures");
                if (!Directory.Exists(capturesFolder))
                {
                    Directory.CreateDirectory(capturesFolder);
                }

                string fileName = $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                string filePath = Path.Combine(capturesFolder, fileName);

                // Convert file path to byte array for native C DLL
                byte[] pathBytes = Encoding.Default.GetBytes(filePath);

                // Save picture
                int result = CamHelp.HIGH_TakePicture(pathBytes);
                if (result == 0)
                {
                    // Show captured static image in CapturedPhotoPreview overlay
                    if (CapturedPhotoPreview != null && File.Exists(filePath))
                    {
                        try
                        {
                            BitmapImage bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(filePath);
                            bitmap.EndInit();
                            CapturedPhotoPreview.Source = bitmap;
                            CapturedPhotoPreview.Visibility = Visibility.Visible;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine("Error loading captured photo preview: " + ex.Message);
                        }
                    }

                    // Wait for 3 seconds of background processing
                    await Task.Delay(3000);

                    // Navigate to Edit page directly
                    NavigationService?.Navigate(new Edit(filePath));
                }
                else
                {
                    MessageBox.Show("Hardware failed to take picture. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    // Reset overlay
                    if (ProcessingOverlay != null) ProcessingOverlay.Visibility = Visibility.Collapsed;
                    if (CaptureButton != null) CaptureButton.IsEnabled = true;
                    if (BackButton != null) BackButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save photo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Reset overlay
                if (ProcessingOverlay != null) ProcessingOverlay.Visibility = Visibility.Collapsed;
                if (CaptureButton != null) CaptureButton.IsEnabled = true;
                if (BackButton != null) BackButton.IsEnabled = true;
            }
        }
    }
}
