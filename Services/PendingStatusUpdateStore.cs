using kuboPix.Helpers;
using kuboPix.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace kuboPix.Services
{
    /// <summary>
    /// Persistent queue of order-status updates that failed to reach the backend.
    /// A background flusher in <see cref="OrderQueueService"/> retries these until the
    /// backend acknowledges, then removes them. Survives app restart (T-5.3 / T-5.4).
    /// Thread-safe.
    /// </summary>
    public static class PendingStatusUpdateStore
    {
        private const string Key = "pending_status_updates";

        private static readonly object _lock = new object();
        private static List<PendingStatusUpdate>? _items;

        private static List<PendingStatusUpdate> Items
            => _items ??= (LocalStore.Load<List<PendingStatusUpdate>>(Key) ?? new List<PendingStatusUpdate>());

        public static bool HasAny
        {
            get { lock (_lock) { return Items.Count > 0; } }
        }

        /// <summary>Enqueue (or replace) a pending update for a given order+status.</summary>
        public static void Add(PendingStatusUpdate update)
        {
            lock (_lock)
            {
                Items.RemoveAll(x => Same(x, update));
                Items.Add(update);
                Persist();
            }
        }

        public static List<PendingStatusUpdate> Snapshot()
        {
            lock (_lock) { return Items.ToList(); }
        }

        public static void Remove(PendingStatusUpdate update)
        {
            lock (_lock)
            {
                Items.RemoveAll(x => Same(x, update));
                Persist();
            }
        }

        private static bool Same(PendingStatusUpdate a, PendingStatusUpdate b)
            => a.OrderId == b.OrderId && a.Status == b.Status && a.IsFree == b.IsFree;

        private static void Persist() => LocalStore.Save(Key, _items);
    } 
}
