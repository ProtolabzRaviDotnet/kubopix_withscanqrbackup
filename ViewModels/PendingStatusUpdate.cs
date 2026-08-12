using System;

namespace kuboPix.ViewModels
{
    /// <summary>
    /// A backend order-status update that could not be delivered (backend down / network lost).
    /// Persisted so it can be retried until the backend acknowledges it (T-5.3 / T-5.4),
    /// ensuring an already-printed order stops being reported as "pending".
    /// </summary>
    public class PendingStatusUpdate
    {
        public long OrderId { get; set; }
        public int Status { get; set; }                 // 1 = success, 2 = failed
        public string? PaymentIntentId { get; set; }
        public string? Message { get; set; }
        public long? OrderPaymentTypeId { get; set; }
        public bool IsFree { get; set; }                // true → use the /free endpoint
        public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
