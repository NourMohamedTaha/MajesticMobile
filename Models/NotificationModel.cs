namespace MajesticMobile.Models
{
    public class NotificationModel
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string TimeAgo { get; set; } = string.Empty;
        public string ColorCode { get; set; } = "#FFFFFF"; // مثال: "#2ECC71"
    }
}