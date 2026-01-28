using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniApp.Helpers
{
    public static class AppConfig
    {
        public static string ApiBaseUrl => ConfigurationManager.AppSettings["ApiBaseUrl"] ?? throw new InvalidOperationException("ApiBaseUrl missing in App.config");
    }
}
