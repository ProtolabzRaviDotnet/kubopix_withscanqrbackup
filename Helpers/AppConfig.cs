using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kuboPix.Helpers
{
    public static class AppConfig
    {
        public static string ApiBaseUrl => ConfigurationManager.AppSettings["ApiBaseUrl"] ?? throw new InvalidOperationException("ApiBaseUrl missing in App.config");

        public static bool PortraitPreview => 
            bool.TryParse(ConfigurationManager.AppSettings["PortraitPreview"], out bool result) && result;

        public static double PortraitPreviewHeightFactor => 
            double.TryParse(ConfigurationManager.AppSettings["PortraitPreviewHeightFactor"], out double result) ? result : 0.9;
    }
}
