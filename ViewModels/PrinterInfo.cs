using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kuboPix.ViewModels
{
    public class PrinterInfo
    {
        public string? Name { get; set; }
        public string? Model { get; set; }
        public string? Firmware { get; set; }
        public string? Serial { get; set; }
        public string? RibbonType { get; set; }
        public string? RibbonCount { get; set; }
        public string? PrintCount { get; set; }
        public string? Status { get; set; }        // friendly text or "Error Code 0x..."
        public string? StatusRaw { get; set; }     // raw hex code if any
        public string? Warning { get; set; }       // friendly warning or "Warning Code..."
        public string? WarningRaw { get; set; }    // raw hex code if any
        public string? CardPosition { get; set; }
        public string? MacAddress { get; set; }
        public string? RejectBox { get; set; }
    }

}
