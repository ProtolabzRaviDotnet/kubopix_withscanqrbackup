using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace MiniApp.Helpers
{
    public static class NavigationService
    {
        private static Frame? _mainFrame;
        private static Page? _currentPage;  // Store the current page

        public static void Initialize(Frame frame)
        {
            _mainFrame = frame;
        }

        public static void Navigate(Page page)
        {
            if (_mainFrame == null)
                return;

            // Set the current page when navigating
            _currentPage = page;

            // Run navigation on UI thread
            _mainFrame.Dispatcher.Invoke(() =>
            {
                _mainFrame.Navigate(page);
            });
        }

        public static void GoBack()
        {
            if (_mainFrame?.CanGoBack == true)
            {
                _mainFrame.Dispatcher.Invoke(() =>
                {
                    _mainFrame.GoBack();
                });
            }
        }

        // Get the current page
        public static Page? GetCurrentPage()
        {
            return _currentPage;
        }
    }


}
