using System;
using System.Windows;
using System.Windows.Media;

namespace kuboPix.Helpers
{
    /// <summary>
    /// Scales a modal dialog so it sits at the same visual scale as the rest of the UI,
    /// and accurately positions it in the center of its owner or page.
    ///
    /// Dialogs are separate top-level <see cref="Window"/>s, so they live outside the
    /// scaling host in MainWindow and would otherwise render at their fixed design size -
    /// noticeably small on a large kiosk panel, and inconsistent with the page behind them.
    /// This applies the same scale factor MainWindow derives from the screen, so a dialog
    /// keeps exactly the proportions it was drawn with relative to the page.
    ///
    /// Call once per dialog instance, from SourceInitialized - by then the caller has
    /// assigned Owner and the owner has a real size.
    /// </summary>
    public static class DialogScaler
    {
        // Must match the constants in MainWindow, so dialogs and pages scale together.
        private const double DesignHeight = 1366;
        private const double MinDesignWidth = 768;

        public static void ScaleToOwner(Window dialog)
        {
            if (dialog?.Owner == null)
                return;

            double ownerWidth = dialog.Owner.ActualWidth;
            double ownerHeight = dialog.Owner.ActualHeight;

            if (ownerWidth <= 0 || ownerHeight <= 0)
                return;

            double scale = Math.Min(ownerHeight / DesignHeight, ownerWidth / MinDesignWidth);

            if (scale > 0 && !double.IsNaN(scale) && !double.IsInfinity(scale))
            {
                // dialog.Width/Height are still the design values set in XAML - this runs once.
                if (!double.IsNaN(dialog.Width))
                    dialog.Width *= scale;

                if (!double.IsNaN(dialog.Height))
                    dialog.Height *= scale;
            }

            CenterOnOwner(dialog);
        }

        public static void CenterOnOwner(Window dialog)
        {
            CenterOnElement(dialog, dialog?.Owner);
        }

        public static void CenterOnElement(Window dialog, FrameworkElement? targetElement)
        {
            if (dialog == null)
                return;

            FrameworkElement? element = targetElement ?? dialog.Owner ?? Application.Current?.MainWindow;
            if (element == null)
                return;

            try
            {
                double elemWidth = element.ActualWidth;
                double elemHeight = element.ActualHeight;

                if (elemWidth <= 0 && element is Window w1) elemWidth = w1.Width;
                if (elemHeight <= 0 && element is Window w2) elemHeight = w2.Height;

                if (elemWidth <= 0 || elemHeight <= 0)
                    return;

                double centerScreenX;
                double centerScreenY;

                if (element.IsVisible)
                {
                    Point pTopLeft = element.PointToScreen(new Point(0, 0));
                    Point pBottomRight = element.PointToScreen(new Point(elemWidth, elemHeight));
                    centerScreenX = (pTopLeft.X + pBottomRight.X) / 2.0;
                    centerScreenY = (pTopLeft.Y + pBottomRight.Y) / 2.0;
                }
                else if (element is Window win)
                {
                    centerScreenX = win.Left + (elemWidth / 2.0);
                    centerScreenY = win.Top + (elemHeight / 2.0);
                }
                else
                {
                    return;
                }

                // Convert screen point to WPF device-independent units (DIPs)
                Visual visual = (dialog.Owner as Visual) ?? (element as Visual) ?? dialog;
                var source = PresentationSource.FromVisual(visual);
                Point wpfCenter;
                if (source?.CompositionTarget != null)
                {
                    wpfCenter = source.CompositionTarget.TransformFromDevice.Transform(new Point(centerScreenX, centerScreenY));
                }
                else
                {
                    wpfCenter = new Point(centerScreenX, centerScreenY);
                }

                double dialogWidth = !double.IsNaN(dialog.Width) && dialog.Width > 0 ? dialog.Width : dialog.ActualWidth;
                double dialogHeight = !double.IsNaN(dialog.Height) && dialog.Height > 0 ? dialog.Height : dialog.ActualHeight;

                if (dialogWidth <= 0) dialogWidth = 401;
                if (dialogHeight <= 0) dialogHeight = 345;

                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = wpfCenter.X - (dialogWidth / 2.0);
                dialog.Top = wpfCenter.Y - (dialogHeight / 2.0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DialogScaler.CenterOnElement error: {ex.Message}");
            }
        }
    }
}
