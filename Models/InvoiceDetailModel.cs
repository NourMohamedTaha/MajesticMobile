using SQLite;

namespace MajesticMobile.Models
{
    public class InvoiceDetailModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int InvoiceId { get; set; }  // بيربط الصنف بالفاتورة بتاعته

        public int ProductId { get; set; }  // الـ Id المحلي للمنتج (مش ServerId)

        public string ProductName { get; set; } = string.Empty;

        public decimal Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal Total { get; set; }
    }
}