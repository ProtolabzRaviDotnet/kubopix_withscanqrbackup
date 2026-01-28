using MiniApp.Services;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Input;

namespace MiniApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // TOUCH → MOUSE CLICK CONVERSION (KIOSK FIX)
            EventManager.RegisterClassHandler(typeof(UIElement),
                UIElement.TouchUpEvent,
                new EventHandler<TouchEventArgs>(OnTouchUpConvertToClick),
                true);
        }
        private void OnTouchUpConvertToClick(object? sender, TouchEventArgs e)
        {
            if (sender is UIElement element)
            {
                var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                    Source = e.Source
                };
                element.RaiseEvent(mouse);
            }
        }
        protected override void OnStartup(StartupEventArgs e)
        {

            System.Net.ServicePointManager.SecurityProtocol =
            System.Net.SecurityProtocolType.Tls12 |
            System.Net.SecurityProtocolType.Tls13;

            base.OnStartup(e);

        }

    }
 }
