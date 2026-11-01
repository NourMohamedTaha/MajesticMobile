using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MajesticApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationsController : ControllerBase
    {
        private readonly string _connectionString;

        public NotificationsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery] int locationId = 1)
        {
            var notifications = new List<object>();

            try
            {
                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    string query = @"
                        SELECT 
                            p.Name, 
                            ISNULL(s.Quantity, 0) AS Qty, 
                            ISNULL(p.AlertLimit, 5) AS AlertLimit
                        FROM Products p
                        LEFT JOIN Stock s ON p.Id = s.ProductId AND s.LocationId = @LocationId
                        WHERE p.IsActive = 1
                          AND (p.BranchId IS NULL OR p.BranchId = @LocationId)
                          AND ISNULL(s.Quantity, 0) <= ISNULL(p.AlertLimit, 5)
                        ORDER BY ISNULL(s.Quantity, 0) ASC";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@LocationId", locationId);

                        await con.OpenAsync(); // استخدام Async لتجنب التنبيهات وتسريع السيرفر

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                // تأمين المتغيرات عشان تنبيهات الـ Null تختفي
                                string name = reader["Name"]?.ToString() ?? "صنف غير معروف";
                                int qty = Convert.ToInt32(reader["Qty"]);

                                notifications.Add(new
                                {
                                    Title = "رصيد منخفض",
                                    Message = $"صنف '{name}' أوشك على النفاذ (المتبقي: {qty}).",
                                    TimeAgo = "الآن",
                                    ColorCode = "#E74C3C"
                                });
                            }
                        }
                    }
                }
                return Ok(notifications);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }
    }
}