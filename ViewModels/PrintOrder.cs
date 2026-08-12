using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kuboPix.ViewModels
{
    public class PrintOrder
    {
        public int OrderId { get; set; }
        public string PrinterName { get; set; } = "";
        public string ImageBase64 { get; set; } = "";
    }
}
