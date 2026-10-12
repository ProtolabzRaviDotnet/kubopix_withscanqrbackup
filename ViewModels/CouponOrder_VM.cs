using System;

namespace kuboPix.ViewModels
{
    public class CouponOrder_VM
    {
        public Int64 ShopLoginId { get; set; }
        public Int64 PrinterId { get; set; }
        public Int64 LogoWatermarkId { get; set; }
        public Int64 FrameWatermarkId { get; set; }
        public string FinalImagePath { get; set; } = string.Empty;
        public int IsPaidOrder { get; set; } = 1;
        public string CouponCode { get; set; } = string.Empty;
    }

    public class CouponOrderResponse
    {
        public int status { get; set; }
        public string message { get; set; } = string.Empty;
        public long orderId { get; set; }
    }
}
