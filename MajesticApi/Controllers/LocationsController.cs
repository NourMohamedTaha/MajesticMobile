using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace MajesticApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LocationsController : ControllerBase
    {
        private readonly string _connectionString;

        public LocationsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        [HttpGet]
        public IActionResult GetActiveBranches()
        {
            var branches = new List<object>();
            try
            {
                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    // استبعاد المخزن الرئيسي (Id = 1) من قائمة الفروع اللي بتظهر للمناديب/الفروع
                    // على الموبايل، لأن حسابات الكاش والتقفيل بتاعته منفصلة تمامًا عن الفروع.
                    string query = "SELECT Id, Name FROM Locations WHERE ISNULL(IsActive, 1) = 1 AND Id <> 1 ORDER BY Id ASC";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        con.Open();
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                branches.Add(new
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    Name = reader["Name"].ToString()
                                });
                            }
                        }
                    }
                }
                return Ok(branches);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }
    }
}