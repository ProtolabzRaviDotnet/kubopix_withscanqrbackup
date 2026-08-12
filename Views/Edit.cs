using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace kuboPix.Views
{
    /// <summary>
    /// Interaction logic for SelectFrame.xaml
    /// </summary>
    public partial class Edit : Page
    {
        private string _capturedPhotoPath = "";
        private string _selectedFrame = "none";
        private string _selectedBackground = "none";

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
                    // Load image using BitmapCacheOption.OnLoad to avoid locking the physical file
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.EndInit();
                    CapturedPhoto.Source = bitmap;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error loading captured image: " + ex.Message);
                }
            }

            // Default frames & background states
            Loaded += (s, e) =>
            {
                SetSelectedFrame("none");
                SetSelectedBackground("none");
            };
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
            NavigationService?.Navigate(new PrintPreview(_capturedPhotoPath, _selectedFrame, _selectedBackground));
        }

        // Frame Selection Click Handlers
        private void FrameNone_Click(object sender, MouseButtonEventArgs e)
        {
            SetSelectedFrame("none");
        }

        private void FrameEllipse_Click(object sender, MouseButtonEventArgs e)
        {
            SetSelectedFrame("ellipse");
        }

        private void FrameBottle_Click(object sender, MouseButtonEventArgs e)
        {
            SetSelectedFrame("bottle");
        }

        private BitmapImage LoadAssetImage(string fileName)
        {
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
            if (!File.Exists(fullPath))
            {
                // Fallback to project root assets folder if base directory is different
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

        private void SetSelectedFrame(string frameName)
        {
            _selectedFrame = frameName;
            if (FrameNoneBorder == null || FrameEllipseBorder == null || FrameBottleBorder == null || FrameOverlay == null)
                return;

            Brush grayBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE5E7EB"));
            Brush activeBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5C6BFF"));

            // Reset borders
            FrameNoneBorder.BorderBrush = grayBorder;
            FrameNoneBorder.BorderThickness = new Thickness(1.5);
            FrameEllipseBorder.BorderBrush = grayBorder;
            FrameEllipseBorder.BorderThickness = new Thickness(1.5);
            FrameBottleBorder.BorderBrush = grayBorder;
            FrameBottleBorder.BorderThickness = new Thickness(1.5);

            try
            {
                // Highlight selected and set frame overlay
                if (frameName == "none")
                {
                    FrameNoneBorder.BorderBrush = activeBorder;
                    FrameNoneBorder.BorderThickness = new Thickness(2.5);
                    FrameOverlay.Visibility = Visibility.Collapsed;
                }
                else if (frameName == "ellipse")
                {
                    FrameEllipseBorder.BorderBrush = activeBorder;
                    FrameEllipseBorder.BorderThickness = new Thickness(2.5);
                    FrameOverlay.Source = LoadAssetImage("Ellipse 5@2x.png");
                    FrameOverlay.Visibility = Visibility.Visible;
                }
                else if (frameName == "bottle")
                {
                    FrameBottleBorder.BorderBrush = activeBorder;
                    FrameBottleBorder.BorderThickness = new Thickness(2.5);
                    FrameOverlay.Source = LoadAssetImage("750mL Bottles (3)@2x.png");
                    FrameOverlay.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error loading frame overlay: " + ex.Message);
            }
        }

        // Background Selection Click Handlers
        private void BgNone_Click(object sender, MouseButtonEventArgs e)
        {
            SetSelectedBackground("none");
        }

        private void BgWinter_Click(object sender, MouseButtonEventArgs e)
        {
            SetSelectedBackground("winter");
        }

        private void BgXmas_Click(object sender, MouseButtonEventArgs e)
        {
            SetSelectedBackground("xmas");
        }

        private void BgSummer_Click(object sender, MouseButtonEventArgs e)
        {
            SetSelectedBackground("summer");
        }

        private void SetSelectedBackground(string bgName)
        {
            _selectedBackground = bgName;
            if (BgNoneBorder == null || BgWinterBorder == null || BgXmasBorder == null || BgSummerBorder == null || PhotoBorder == null)
                return;

            Brush grayBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE5E7EB"));
            Brush activeBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5C6BFF"));

            // Reset borders
            BgNoneBorder.BorderBrush = grayBorder;
            BgNoneBorder.BorderThickness = new Thickness(1.5);
            BgWinterBorder.BorderBrush = grayBorder;
            BgWinterBorder.BorderThickness = new Thickness(1.5);
            BgXmasBorder.BorderBrush = grayBorder;
            BgXmasBorder.BorderThickness = new Thickness(1.5);
            BgSummerBorder.BorderBrush = grayBorder;
            BgSummerBorder.BorderThickness = new Thickness(1.5);

            // Highlight selected background option & apply color to PhotoBorder
            if (bgName == "none")
            {
                BgNoneBorder.BorderBrush = activeBorder;
                BgNoneBorder.BorderThickness = new Thickness(2.5);
                PhotoBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF9FAFB"));
                if (CapturedPhoto != null) CapturedPhoto.Opacity = 1.0;
            }
            else if (bgName == "winter")
            {
                BgWinterBorder.BorderBrush = activeBorder;
                BgWinterBorder.BorderThickness = new Thickness(2.5);
                PhotoBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE0F2FE"));
                if (CapturedPhoto != null) CapturedPhoto.Opacity = 0.85; // Allow background color to tint the preview
            }
            else if (bgName == "xmas")
            {
                BgXmasBorder.BorderBrush = activeBorder;
                BgXmasBorder.BorderThickness = new Thickness(2.5);
                PhotoBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2"));
                if (CapturedPhoto != null) CapturedPhoto.Opacity = 0.85; // Allow background color to tint the preview
            }
            else if (bgName == "summer")
            {
                BgSummerBorder.BorderBrush = activeBorder;
                BgSummerBorder.BorderThickness = new Thickness(2.5);
                PhotoBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
                if (CapturedPhoto != null) CapturedPhoto.Opacity = 0.85; // Allow background color to tint the preview
            }
        }
    }
}
