using System;
using System.Windows;

namespace kuboPix.Helpers
{
    /// <summary>
    /// Scales a modal dialog so it sits at the same visual scale as the rest of the UI.
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

            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
                return;

            // dialog.Width/Height are still the design values set in XAML - this runs once.
            if (!double.IsNaN(dialog.Width))
                dialog.Width *= scale;

            if (!double.IsNaN(dialog.Height))
                dialog.Height *= scale;
        }
    }
}
