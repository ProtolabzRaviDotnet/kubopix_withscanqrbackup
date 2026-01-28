using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniApp.ViewModels
{
    public class MiniAppSession
    {
        public string Token { get; set; } = string.Empty;
        public string? Password { get; set; }
        public string PrinterName { get; set; } = string.Empty;
        public Int64 ShopLoginId { get; set; }
        public Int64 PrinterId { get; set; }
        public DateTime SavedAt { get; set; }
        public decimal PrinterPrice { get; set; }
        public int PrinterStatus { get; set; }
        public int PrinterPaymentEnabled { get; set; }
    }
}
