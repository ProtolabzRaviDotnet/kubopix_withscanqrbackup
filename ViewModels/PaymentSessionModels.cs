using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace kuboPix.ViewModels
{
    public class PaymentSessionRequest
    {
        public long LogoWatermarkId { get; set; }
        public long FrameWatermarkId { get; set; }
    }

    public class PaymentSessionResponseData
    {
        public long SessionId { get; set; }
        public string KioskSecret { get; set; } = string.Empty;
        public string QrUrl { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
        public int ExpiresInSeconds { get; set; }
    }

    public class PaymentSessionApiResponse<T>
    {
        public int status { get; set; }
        public string message { get; set; } = string.Empty;
        public T? data { get; set; }
    }

    public class CreateOrderInstruction
    {
        public string Method { get; set; } = "POST";
        public string Url { get; set; } = string.Empty;
        public JObject? Body { get; set; }
        public string? ImageField { get; set; }
        public string? OrderIdField { get; set; }
    }

    public class PaymentUpdatePayload
    {
        public long SessionId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? PaymentType { get; set; }
        public string? PaymentTypeName { get; set; }
        public int OrderPaymentTypeId { get; set; } = 1;
        public string? PaymentIntentId { get; set; }
        public string? CouponCode { get; set; }
        public decimal Amount { get; set; }
        public string? Currency { get; set; }
        public DateTime? PaidOn { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public long? OrderId { get; set; }
        public CreateOrderInstruction? CreateOrder { get; set; }
    }

    public class PaymentSessionCancelRequest
    {
        public long SessionId { get; set; }
        public string KioskSecret { get; set; } = string.Empty;
    }
}
