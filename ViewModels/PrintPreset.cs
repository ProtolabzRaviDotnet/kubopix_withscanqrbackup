using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kuboPix.ViewModels
{
    public class PrintPreset
    {
        public string Name { get; set; } = string.Empty;
        public byte byOrientation { get; set; } = 1;
        public byte byRibbonType { get; set; } = 0;
        public byte byPrintSide { get; set; } = 1;
        public byte byPrintPanelFront { get; set; } = 0;
        public byte byPrintPanelBack { get; set; } = 0;
        public byte byResolution { get; set; } = 0;
        public byte byEjectCardMode { get; set; } = 0;
        public byte byAutoDetectRibbon { get; set; } = 1;
        public byte byPaperSize { get; set; } = 0;
        public byte byInputBin { get; set; } = 0;
        public byte byOutputBin { get; set; } = 0;
        public byte byFeedCardMode { get; set; } = 0;
    }
}
