using SQLite;
using Microsoft.Maui.Graphics;

namespace MajesticMobile.Models
{
    public class CategoryModel
    {
        [PrimaryKey]
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [Ignore]
        public Color BgColor { get; set; } = Colors.Transparent;
    }
}