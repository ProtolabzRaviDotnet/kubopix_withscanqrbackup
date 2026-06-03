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

        public static bool CanGoBack(int steps = 1)
        {
            if (_mainFrame == null)
                return false;

            bool enoughHistory = false;
            _mainFrame.Dispatcher.Invoke(() =>
            {
                var entries = _mainFrame.BackStack;
                if (entries == null)
                {
                    enoughHistory = false;
                    return;
                }

                int count = 0;
                foreach (var _ in entries)
                {
                    count++;
                    if (count >= steps)
                        break;
                }

                enoughHistory = count >= steps;
            });

            return enoughHistory;
        }

        public static void GoBack(int steps)
        {
            if (_mainFrame == null)
                return;

            _mainFrame.Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < steps && _mainFrame.CanGoBack; i++)
                {
                    _mainFrame.GoBack();
                }
            });
        }

        // Get the current page
        public static Page? GetCurrentPage()
        {
            if (_mainFrame == null)
                return null;

            var currentFramePage = _mainFrame.Content as Page;
            if (currentFramePage != null)
                _currentPage = currentFramePage;

            return _currentPage;
        }
    }


}
