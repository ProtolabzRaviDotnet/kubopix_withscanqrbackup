using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace MiniApp.Views
{
    /// <summary>
    /// Interaction logic for PrintSuccessView.xaml
    /// </summary>
    public partial class PrintSuccessView : Page
    {
        // Constructor
        public PrintSuccessView()
        {
            InitializeComponent();
        }

        // Page Loaded Event Handler
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeVideo();
        }

        // Initialize Video Method
        private void InitializeVideo()
        {
            try
            {
                string videoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "printsuccess.mp4");

                if (File.Exists(videoPath))
                {
                    SuccessVideo.Source = new Uri(videoPath, UriKind.Absolute);
                    SuccessVideo.Play();
                }
                else
                {
                    SuccessVideo.Source = new Uri("/Assets/printsuccess.mp4", UriKind.Relative);
                    SuccessVideo.Play();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Video initialization failed: {ex.Message}");
            }
        }

        // Video Media Ended Event Handler
        private void SuccessVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Loop video (if needed)
            SuccessVideo.Position = TimeSpan.Zero;
            SuccessVideo.Play();
        }

        // Video Media Failed Event Handler
        private void SuccessVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            AppLogger.Log($"Video playback failed: {e.ErrorException?.Message}");
        }
    }
}
