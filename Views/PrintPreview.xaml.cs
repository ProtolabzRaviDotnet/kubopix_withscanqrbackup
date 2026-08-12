using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using kuboPix.Services;

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

        private int _copies = 1;

        public PrintPreview()
        {
            InitializeComponent();
        }

        public PrintPreview(string imagePath, string selectedFrame, string selectedBackground, int copies = 1) : this()
        {
            _capturedPhotoPath = imagePath;
            _selectedFrame = selectedFrame;
            _selectedBackground = selectedBackground;
            _copies = copies;

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
                    System.Diagnostics.Debug.WriteLine("Error loading preview photo: " + ex.Message);
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
                System.Diagnostics.Debug.WriteLine("Error loading preview frame overlay: " + ex.Message);
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

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(new Checkout(_capturedPhotoPath, _selectedFrame, _selectedBackground));
        }
    }
}
