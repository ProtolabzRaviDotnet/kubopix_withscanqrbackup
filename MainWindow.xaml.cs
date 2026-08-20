using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using kuboPix.Helpers;
using System.Windows.Shapes;


namespace kuboPix
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// The portrait shape the pages are authored against - a reference, not a target
        /// resolution. The real canvas is derived from the window at runtime, so one build
        /// fills any portrait panel with no per-kiosk configuration.
        /// </summary>
        private const double DesignHeight = 1920;

        /// <summary>
        /// Narrowest logical width the page layouts are known to fit in. Acts as the guard
        /// that makes content shrink rather than clip on a narrower-than-design panel.
        /// </summary>
        private const double MinDesignWidth = 1080;

        public MainWindow()
        {
            InitializeComponent();
            NavigationService.Initialize(MainFrame);

             if (AppConfig.PortraitPreview)
            {
                this.WindowState = WindowState.Normal;
                this.WindowStyle = WindowStyle.SingleBorderWindow;
                this.ResizeMode = ResizeMode.NoResize;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                double factor = AppConfig.PortraitPreviewHeightFactor;
                this.Height = screenHeight * factor;
                this.Width = this.Height * (MinDesignWidth / DesignHeight);
                // Center on screen
                this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
                this.Top = (SystemParameters.PrimaryScreenHeight - this.Height) / 2;
            }
            // ── BYPASS MODE: skip Login entirely for demo recording ────────────
            // In production this is always LoginView; set BypassPrinterForDemo=false
            // in App.xaml.cs before the production build.
            if (App.BypassPrinterForDemo)
                MainFrame.Navigate(new Views.Capture());
            else
                MainFrame.Navigate(new Views.LoginView());

        }

        /// <summary>
        /// Scales the whole UI by the window's height, unless the window is too narrow for the
        /// layouts - in which case width becomes the constraint and the content shrinks to fit
        /// instead of clipping. The frame is then given the full screen expressed in logical
        /// units, so the extra space shows up as margin around the already-centred content
        /// rather than as letterbox bars.
        /// </summary>
        private void ScaleHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double width = e.NewSize.Width;
            double height = e.NewSize.Height;

            if (width <= 0 || height <= 0)
                return;

            double scale = Math.Min(height / DesignHeight, width / MinDesignWidth);

            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
                return;

            UiScale.ScaleX = scale;
            UiScale.ScaleY = scale;

            MainFrame.Width = width / scale;
            MainFrame.Height = height / scale;
        }
    }
}