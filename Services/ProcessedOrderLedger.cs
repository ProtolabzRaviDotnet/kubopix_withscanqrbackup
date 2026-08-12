using kuboPix.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace kuboPix.Services
{
    /// <summary>
    /// Persistent ledger of Order IDs that have been **successfully printed on the hardware**.
    /// Survives app restart / power loss so an order is never printed twice — even if the
    /// status update to the backend failed and the order is still reported as "pending"
    /// (the root cause of the Order 840 double print).
    ///
    /// Thread-safe (T-5.2 atomic). Bounded in size (T-5.5 housekeeping).
    /// </summary>
    public static class ProcessedOrderLedger
    {
        private const string Key = "printed_orders";
        private const int MaxEntries = 500; // enough to cover any realistic restart/outage window

        private static readonly object _lock = new object();
        private static HashSet<long>? _printed;

        private static HashSet<long> Printed
        {
            get
            {
                if (_printed == null)
                {
                    var list = LocalStore.Load<List<long>>(Key) ?? new List<long>();
                    _printed = new HashSet<long>(list);
                }
                return _printed;
            }
        }

        /// <summary>True if this order was already printed (per the persistent ledger).</summary>
        public static bool IsPrinted(long orderId)
        {
            lock (_lock)
            {
                return Printed.Contains(orderId);
            }
        }

        /// <summary>Record a successfully printed order. Persisted immediately. Idempotent.</summary>
        public static void MarkPrinted(long orderId)
        {
            lock (_lock)
            {
                if (Printed.Add(orderId))
                {
                    Trim();
                    Persist();
                    AppLogger.Log($"Order {orderId} recorded in printed ledger.");
                }
            }
        }

        /// <summary>Snapshot of all printed Order IDs (used to seed the in-memory session set on startup).</summary>
        public static IReadOnlyCollection<long> All()
        {
            lock (_lock)
            {
                return Printed.ToList();
            }
        }

        // Keep the ledger bounded — retain the most recent IDs (Order IDs increase over time).
        private static void Trim()
        {
            if (_printed!.Count > MaxEntries)
            {
                _printed = _printed.OrderByDescending(id => id).Take(MaxEntries).ToHashSet();
            }
        }

        private static void Persist()
        {
            LocalStore.Save(Key, _printed!.ToList());
        }
    }
}
