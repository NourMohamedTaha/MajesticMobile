using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MajesticApi.Controllers
{
    public class NewRequestDto
    {
        public int LocationId { get; set; }
        public DateTime RequestDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<NewRequestDetailDto> Details { get; set; } = new();
    }

    public class NewRequestDetailDto
    {
        public int ProductId { get; set; }
        public int RequestedQuantity { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class RequestsController : ControllerBase
    {
        private readonly string _connectionString;

        public RequestsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        [HttpPost]
        public async Task<IActionResult> CreateRequest([FromBody] NewRequestDto request)
        {
            if (request == null || request.Details.Count == 0)
                return BadRequest(new { message = "بيانات الطلبية غير مكتملة." });

            try
            {
                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    await con.OpenAsync();
                    using (SqlTransaction trans = con.BeginTransaction())
                    {
                        try
                        {
                            int requestId = 0;

                            string insertRequestQuery = @"
                                INSERT INTO Requests (LocationId, RequestDate, Status, Notes, ManagerNotes) 
                                VALUES (@LocationId, @RequestDate, @Status, NULL, NULL);
                                SELECT CAST(SCOPE_IDENTITY() AS INT);";

                            using (SqlCommand cmd = new SqlCommand(insertRequestQuery, con, trans))
                            {
                                cmd.Parameters.AddWithValue("@LocationId", request.LocationId);
                                cmd.Parameters.AddWithValue("@RequestDate", request.RequestDate);
                                cmd.Parameters.AddWithValue("@Status", request.Status ?? string.Empty);

                                requestId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                            }

                            string insertDetailsQuery = @"
                                INSERT INTO RequestDetails (RequestId, ProductId, RequestedQuantity, ApprovedQuantity) 
                                VALUES (@RequestId, @ProductId, @RequestedQty, 0);";

                            using (SqlCommand cmd = new SqlCommand(insertDetailsQuery, con, trans))
                            {
                                foreach (var detail in request.Details)
                                {
                                    cmd.Parameters.Clear();
                                    cmd.Parameters.AddWithValue("@RequestId", requestId);
                                    cmd.Parameters.AddWithValue("@ProductId", detail.ProductId);
                                    cmd.Parameters.AddWithValue("@RequestedQty", detail.RequestedQuantity);

                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }

                            trans.Commit();
                            return Ok(new { ServerId = requestId });
                        }
                        catch (Exception ex)
                        {
                            trans.Rollback();
                            return StatusCode(500, new { message = "خطأ أثناء حفظ الطلبية", error = ex.Message });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في الاتصال بقاعدة البيانات", error = ex.Message });
            }
        }

        [HttpGet("Status")]
        public async Task<IActionResult> GetRequestsStatus([FromQuery] int locationId)
        {
            var statuses = new List<object>();
            try
            {
                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    string query = @"
                        SELECT 
                            Id, 
                            Status, 
                            LTRIM(RTRIM(ISNULL(Notes, '') + ' ' + ISNULL(ManagerNotes, ''))) AS WarehouseMessage
                        FROM Requests 
                        WHERE LocationId = @LocationId
                        ORDER BY Id DESC";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@LocationId", locationId);
                        await con.OpenAsync();

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                statuses.Add(new
                                {
                                    ServerId = Convert.ToInt32(reader["Id"]),
                                    Status = reader["Status"]?.ToString() ?? string.Empty,
                                    Message = reader["WarehouseMessage"]?.ToString() ?? string.Empty
                                });
                            }
                        }
                    }
                }
                return Ok(statuses);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ", error = ex.Message });
            }
        }

        [HttpPut("{id}/Confirm")]
        public async Task<IActionResult> ConfirmDelivery(int id)
        {
            try
            {
                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    const string query = "UPDATE Requests SET Status = N'تم الاستلام' WHERE Id = @Id;";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        await con.OpenAsync();

                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected == 0)
                            return NotFound(new { message = "الطلبية غير موجودة." });
                    }
                }
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ أثناء تأكيد الاستلام", error = ex.Message });
            }
        }
    }
}