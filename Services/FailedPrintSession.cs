using MiniApp.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniApp.Services
{
    public static class FailedPrintSession
    {
        private static readonly List<PendingOrder> _failedOrders = new();

        public static void Add(PendingOrder order)
        {
            //if (_failedOrders.Any(o => o.OrderId == order.OrderId))
            //    return;

            // REMOVE ALL OLD FAILED JOBS
            _failedOrders.Clear();

            _failedOrders.Add(order);
        }

        public static IReadOnlyList<PendingOrder> GetAll()
            => _failedOrders;

        public static void Remove(long orderId)
        {
            _failedOrders.RemoveAll(o => o.OrderId == orderId);
        }

        public static int Count => _failedOrders.Count;

        public static bool HasFailedJobs => _failedOrders.Count > 0;
    }

}
