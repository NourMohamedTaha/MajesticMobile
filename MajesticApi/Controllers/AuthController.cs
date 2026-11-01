using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace MajesticApi.Controllers
{
    public class LoginRequestDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly string _connectionString;

        public AuthController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { message = "برجاء إدخال اسم المستخدم وكلمة المرور." });

            try
            {
                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    await con.OpenAsync();

                    // بنبحث عن المستخدم وبنجيب معاه اسم الفرع بتاعه من جدول Locations
                    // ملحوظة: لو جدول المستخدمين عندك اسمه مختلف (مثلا Employees) غيره هنا
                    string query = @"
                        SELECT u.Id, u.LocationId, l.Name AS BranchName 
                        FROM Users u
                        INNER JOIN Locations l ON u.LocationId = l.Id
                        WHERE u.Username = @User AND u.Password = @Pass AND ISNULL(u.IsActive, 1) = 1";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@User", request.Username);
                        cmd.Parameters.AddWithValue("@Pass", request.Password);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return Ok(new
                                {
                                    success = true,
                                    userId = Convert.ToInt32(reader["Id"]),
                                    locationId = Convert.ToInt32(reader["LocationId"]),
                                    branchName = reader["BranchName"].ToString()
                                });
                            }
                            else
                            {
                                return Unauthorized(new { success = false, message = "اسم المستخدم أو كلمة المرور غير صحيحة، أو الحساب موقوف." });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }
    }
}