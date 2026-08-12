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
    /// Interaction logic for Checkout.xaml
    /// </summary>
    public partial class Checkout : Page
    {
        private string _capturedPhotoPath = "";
        private string _selectedFrame = "none";
        private string _selectedBackground = "none";
        private int _copies = 1;
        //private double PricePerCopy => kuboPixSessionManager.CurrentSession != null ? (double)kuboPixSessionManager.CurrentSession.PrinterPrice : 50.0;

        public Checkout()
        {
            InitializeComponent();
        }

        public Checkout(string imagePath, string selectedFrame, string selectedBackground) : this()
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
                    System.Diagnostics.Debug.WriteLine("Error loading checkout photo: " + ex.Message);
                }
            }

            // 2. Apply background selection color & opacity tint
            ApplyBackgroundChoice(_selectedBackground);

            // 3. Apply frame overlay selection
            ApplyFrameChoice(_selectedFrame);

            // 4. Initialize display
            UpdateCopiesAndPrice();
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
                System.Diagnostics.Debug.WriteLine("Error loading checkout frame overlay: " + ex.Message);
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

        private void BtnPlus_Click(object sender, RoutedEventArgs e)
        {
            _copies++;
            UpdateCopiesAndPrice();
        }

        private void BtnMinus_Click(object sender, RoutedEventArgs e)
        {
            if (_copies > 1)
            {
                _copies--;
                UpdateCopiesAndPrice();
            }
        }

        private void UpdateCopiesAndPrice()
        {
            if (TxtCopies != null)
            {
                TxtCopies.Text = _copies.ToString();
            }

            if (TxtPrice != null)
            {
                //TxtPrice.Text = $"Price: {(_copies * PricePerCopy):0.##}$";
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
                NavigationService?.Navigate(new PrintPreview(_capturedPhotoPath, _selectedFrame, _selectedBackground));
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_capturedPhotoPath) && File.Exists(_capturedPhotoPath))
            {
                string printerName = LoginView.GetCurrentSessionPrinterName() ?? "";
                
                if (string.IsNullOrEmpty(printerName))
                {
                    MessageBox.Show("No active printer configuration found for this session. Please check your shop configuration.", 
                                    "Printer Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    int successCount = 0;
                    string lastError = "";

                    for (int i = 1; i <= _copies; i++)
                    {
                        string result = ExecutePrinterPrint(printerName, _capturedPhotoPath);
                        if (result == "OK")
                        {
                            successCount++;
                        }
                        else
                        {
                            lastError = result;
                        }
                    }

                    if (successCount == _copies)
                    {
                        MessageBox.Show($"Successfully printed {_copies} copy/copies of the photo!", 
                                        "Printing Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else if (successCount > 0)
                    {
                        MessageBox.Show($"Successfully printed {successCount} of {_copies} copy/copies. Last error: {lastError}", 
                                        "Partial Print Output", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show($"Failed to print card: {lastError}", 
                                        "Printing Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return; // Keep user on the screen if printing fails completely
                    }
                }
            }

          
        }

        /// <summary>
        /// Routes the image path to the appropriate printer library based on the printer name.
        /// </summary>
        private string ExecutePrinterPrint(string printerName, string imagePath)
        {
            try
            {
                if (printerName.Contains("Seaory", StringComparison.OrdinalIgnoreCase))
                {
                    return kuboPix.Wrapper.SeaoryPrinterPrint.PrintImage(printerName, imagePath);
                }
                else if (printerName.Contains("Epson", StringComparison.OrdinalIgnoreCase) || 
                         printerName.Contains("CW-C40", StringComparison.OrdinalIgnoreCase))
                {
                    // Epson printer uses printer name and optional spooler port (empty for default)
                    return kuboPix.Wrapper.EpsonPrinterPrint.PrintImage(printerName, "", imagePath);
                }
                else
                {
                    return $"Unsupported printer model type: '{printerName}'";
                }
            }
            catch (Exception ex)
            {
                return $"Printer DLL execution error: {ex.Message}";
            }
        }
    }
}
