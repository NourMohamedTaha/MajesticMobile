namespace MajesticMobile.Models
{
    public class CustomerModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;

        // الحقل الجديد اللي كان ناقص
        public string Region { get; set; } = string.Empty;

        public decimal Balance { get; set; }
        public bool IsActive { get; set; }
        public int LocationId { get; set; }
    }
}