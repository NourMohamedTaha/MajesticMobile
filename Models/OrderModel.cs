using SQLite;
using System;
using System.Collections.Generic;

namespace MajesticMobile.Models
{
    public class OrderModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int ServerId { get; set; }

        public DateTime OrderDate { get; set; }

        public string Status { get; set; } = "Pending";

        public string WarehouseMessage { get; set; } = string.Empty;

        public bool IsSynced { get; set; } = false;
        public bool PendingDeliverySync { get; set; } = false;

        [Ignore]
        public List<OrderDetailModel> Details { get; set; } = new();
    }

    public class OrderDetailModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int OrderId { get; set; }

        public int ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public double RequestedQuantity { get; set; }

        public double ApprovedQuantity { get; set; }
    }
}