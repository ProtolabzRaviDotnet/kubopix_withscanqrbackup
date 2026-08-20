using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using kuboPix.Helpers;
using kuboPix.Services;
using kuboPix.ViewModels;

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

        // Payment method selection (default: card)
        private string _selectedPaymentMethod = "card";

        // Brush constants for payment card states
        private static readonly System.Windows.Media.SolidColorBrush SelectedBorderBrush
            = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF5C6BFF"));
        private static readonly System.Windows.Media.SolidColorBrush DefaultBorderBrush
            = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFE5E7EB"));
        private static readonly System.Windows.Media.SolidColorBrush SelectedBackground
            = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EEF2FF"));
        private static readonly System.Windows.Media.SolidColorBrush DefaultBackground
            = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
        
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
            // Photo preview is not shown on this page (Choose Payment Method UI).
            // Keep _capturedPhotoPath stored for navigation/printing.

           
        }

        /// <summary>
        /// Retained for compatibility. Background is not shown in the payment selection UI.
        /// </summary>
        private void ApplyBackgroundChoice(string bgName)
        {
            // No photo border in the Choose Payment Method UI — no-op.
        }

        /// <summary>
        /// Retained for compatibility. Frame overlay is not shown in the payment selection UI.
        /// </summary>
        private void ApplyFrameChoice(string frameName)
        {
            // No frame overlay in the Choose Payment Method UI — no-op.
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

      

      

        // ===== Payment Method Selection Handlers =====

        private void CardPayment_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectPaymentMethod("card");
        }

        private void Twint_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectPaymentMethod("twint");
        }

        private void Coupon_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectPaymentMethod("coupon");
        }

        private void Terminal_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectPaymentMethod("terminal");
        }

        private void SelectPaymentMethod(string method)
        {
            _selectedPaymentMethod = method;
            HighlightPaymentCard(CardPaymentBorder, method == "card");
            HighlightPaymentCard(TwintBorder,       method == "twint");
            HighlightPaymentCard(CouponBorder,      method == "coupon");
            HighlightPaymentCard(TerminalBorder,    method == "terminal");
        }

        private void HighlightPaymentCard(System.Windows.Controls.Border border, bool isSelected)
        {
            if (border == null) return;
            border.BorderBrush = isSelected ? SelectedBorderBrush : DefaultBorderBrush;
            border.Background  = isSelected ? SelectedBackground   : DefaultBackground;
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

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            // ── BYPASS MODE (demo recording only) ─────────────────────────────────
            if (App.BypassPrinterForDemo)
            {
                NavigationService?.Navigate(new PrintProgressView());
                return;
            }

            // ── NORMAL PRODUCTION PATH ─────────────────────────────────────────────
            if (string.IsNullOrEmpty(_capturedPhotoPath) || !File.Exists(_capturedPhotoPath))
            {
                MessageBox.Show("No captured photo found. Please take a photo first.", 
                                "Photo Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var session = kuboPixSessionManager.Load();
            if (session == null)
            {
                MessageBox.Show("No active session found. Please log in again.", 
                                "Session Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string printerName = LoginView.GetCurrentSessionPrinterName() ?? session.PrinterName ?? "";
            if (string.IsNullOrEmpty(printerName))
            {
                MessageBox.Show("No active printer configuration found for this session. Please check your shop configuration.", 
                                "Printer Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. Upload photo to backend
            string uploadedImageUrl = await PrintPreview.UploadKioskPhotoAsync(session.Token, _capturedPhotoPath);
            if (string.IsNullOrEmpty(uploadedImageUrl))
            {
                MessageBox.Show("Failed to upload photo to server.", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 2. Create order or process order via OQS
            long orderId = await PrintPreview.CreateFreeOrderAsync(session, uploadedImageUrl);
            if (orderId <= 0)
            {
                orderId = DateTime.Now.Ticks % 1000000;
            }

            using var oqs = new OrderQueueService(
                session.Token,
                session.ShopLoginId,
                session.PrinterId,
                printerName,
                session.PrinterStatus,
                "",
                session.PrinterPrice,
                DateTime.UtcNow,
                session.PrinterPaymentEnabled,
                session.PrinterBrand
            );

            // Determine payment type id based on selected payment method ("card", "twint", "coupon", "terminal")
            int paymentTypeId = 1; // Default card (Stripe)
            if (_selectedPaymentMethod == "twint") paymentTypeId = 3;
            else if (_selectedPaymentMethod == "coupon") paymentTypeId = 4;
            else if (_selectedPaymentMethod == "terminal") paymentTypeId = 5;

            var order = new PendingOrder
            {
                OrderId = orderId,
                Image = uploadedImageUrl,
                ShopLoginId = session.ShopLoginId,
                PrinterId = session.PrinterId,
                IsPaidOrder = 1,
                OrderPaymentTypeId = paymentTypeId
            };

            for (int i = 0; i < _copies; i++)
            {
                await oqs.ProcessSingleOrderAsync(order, isRetry: false);
            }
        }
    }
}
