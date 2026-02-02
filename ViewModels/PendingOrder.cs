using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniApp.ViewModels
{
    public class PendingOrder
    {
        public Int64 OrderId { get; set; }
        public string? Image { get; set; }
        public string? PaymentIntentId { get; set; }
        public Int64 ShopLoginId { get; set; }
        public Int64 PrinterId { get; set; }
        public int IsPaidOrder { get; set; } //(0 = Free 1 = Paid)
        public int OrderPaymentTypeId { get; set; } // (1 For Stripe // 2 For Free //3 For Twint)
        public int OrderStatus { get; set; } //3 For Refunded via Twint
    }
}
