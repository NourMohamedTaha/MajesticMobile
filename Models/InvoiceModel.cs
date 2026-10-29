using SQLite;
using System;

namespace MajesticMobile.Models
{
    public class InvoiceModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string InvoiceNumber { get; set; } = string.Empty;

        public DateTime InvoiceDate { get; set; } = DateTime.Now;

        public string CustomerName { get; set; } = "عميل نقدي";

        // بيبقى null لو "عميل نقدي" أو اسم يدوي مش من عميل حقيقي مسجل
        public int? CustomerId { get; set; }

        public int LocationId { get; set; }

        public decimal TotalAmount { get; set; }

        public decimal Discount { get; set; }

        public decimal NetAmount { get; set; }

        public decimal PaidAmount { get; set; }

        public decimal RemainingAmount { get; set; }

        public string PaymentMethod { get; set; } = "نقدي";

        public bool IsReturn { get; set; }

        public bool IsSynced { get; set; }

        // رقم الفاتورة الحقيقي في سيرفر الديسكتوب بعد نجاح المزامنة
        public int ServerSaleId { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}