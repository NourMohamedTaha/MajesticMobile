using SQLite;
using System.Text.Json.Serialization;

namespace MajesticMobile.Models
{
    public class ProductModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [JsonPropertyName("serverId")]
        public int ServerId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("purchasePrice")]
        public decimal PurchasePrice { get; set; }

        [JsonPropertyName("stockQuantity")]
        public double StockQuantity { get; set; }

        [JsonPropertyName("categoryId")]
        public int CategoryId { get; set; }

        [JsonPropertyName("alertLimit")]
        public double AlertLimit { get; set; }
    }
}