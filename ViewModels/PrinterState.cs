using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kuboPix.ViewModels
{
    public class PrinterState
    {
        public Int64 PrinterId { get; set; }
        public string PrinterName { get; set; } = string.Empty;
        public int PrinterStatus { get; set; }
        public int PrinterPaymentEnabled { get; set; }
        public decimal PrinterPrice { get; set; }
    }

}
