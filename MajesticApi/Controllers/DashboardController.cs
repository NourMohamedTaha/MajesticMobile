using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;

namespace MajesticApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase
    {
        private readonly string _connectionString;

        public DashboardController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        [HttpGet]
        public IActionResult GetDashboard([FromQuery] int locationId = 1)
        {
            try
            {
                decimal mainSales = 0;
                decimal netCash = 0;
                decimal totalReturns = 0;

                // تم حساب الكاش الفعلي في الدرج (استبعاد فودافون كاش والانستا)
                const string query = @"
                    DECLARE @MainSales DECIMAL(18,2) = 0;
                    DECLARE @NetCash DECIMAL(18,2) = 0;
                    DECLARE @TotalReturns DECIMAL(18,2) = 0;

                    SELECT @MainSales = ISNULL(SUM(NetAmount), 0) 
                    FROM Sales 
                    WHERE LocationId = @LocationId AND IsShiftClosed = 0;

                    SELECT @NetCash = ISNULL(SUM(CASE WHEN TransactionType IN (N'وارد', N'إيراد', N'مبيعات') THEN Amount ELSE -Amount END), 0)
                    FROM CashTransactions 
                    WHERE LocationId = @LocationId 
                      AND IsClosed = 0
                      AND ISNULL(Description, '') NOT LIKE N'%انستا%'
                      AND ISNULL(Description, '') NOT LIKE N'%فودافون كاش%'
                      AND ISNULL(PaymentMethod, N'نقدي') IN (N'نقدي', N'كاش');

                    SELECT @TotalReturns = ISNULL(SUM(TotalRefundAmount), 0) 
                    FROM SalesReturns 
                    WHERE LocationId = @LocationId AND IsShiftClosed = 0;

                    SELECT @MainSales AS Sales, @NetCash AS NetCash, @TotalReturns AS Returns;";

                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@LocationId", locationId);
                        con.Open();

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                mainSales = Convert.ToDecimal(reader["Sales"]);
                                netCash = Convert.ToDecimal(reader["NetCash"]);
                                totalReturns = Convert.ToDecimal(reader["Returns"]);
                            }
                        }
                    }
                }

                return Ok(new
                {
                    TodaySales = mainSales,
                    CurrentCash = netCash,
                    Returns = totalReturns
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }
    }
}