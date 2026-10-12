using System;

namespace kuboPix.ViewModels
{
    public class FreeOrder_VM
    {
        public Int64 ShopLoginId { get; set; }
        public Int64 PrinterId { get; set; }
        public Int64 LogoWatermarkId { get; set; }
        public Int64 FrameWatermarkId { get; set; }
        public string FinalImagePath { get; set; } = string.Empty;
        public int IsPaidOrder { get; set; }
    }
}
