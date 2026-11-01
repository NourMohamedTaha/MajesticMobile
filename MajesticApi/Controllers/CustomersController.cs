using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace MajesticApi.Controllers
{
    public class CustomerRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty; // تم إضافة المنطقة
        public decimal Balance { get; set; }
        public int LocationId { get; set; }
    }

    public class PayDebtRequest
    {
        public int CustomerId { get; set; }
        public decimal Amount { get; set; }
        public int LocationId { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class CustomersController : ControllerBase
    {
        private readonly string _connectionString;

        public CustomersController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        [HttpGet]
        public IActionResult GetCustomers([FromQuery] int locationId)
        {
            var customers = new List<object>();

            try
            {
                // تم إضافة جلب عمود المنطقة Region
                const string query = @"
                    SELECT 
                        c.Id, 
                        c.Name, 
                        c.Phone, 
                        c.Region, 
                        (ISNULL(c.Balance, 0) 
                         + ISNULL((SELECT SUM(RemainingAmount) FROM Sales WHERE CustomerId = c.Id AND RemainingAmount > 0), 0)
                         - ISNULL((SELECT SUM(TotalRefundAmount) FROM SalesReturns WHERE CustomerId = c.Id), 0)
                         - ISNULL((SELECT SUM(Amount) FROM CustomerPayments WHERE CustomerId = c.Id), 0)) AS CurrentBalance 
                    FROM Customers c 
                    WHERE c.IsActive = 1 AND c.LocationId = @LocationId
                    ORDER BY c.Id ASC;";

                using (SqlConnection con = new SqlConnection(_connectionString))
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@LocationId", locationId);
                    con.Open();

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string name = reader["Name"].ToString() ?? string.Empty;
                            if (name.Contains("???") || name.Contains("؟") || name == "/")
                            {
                                name = "عميل نقدي";
                            }

                            customers.Add(new
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Name = name,
                                Phone = reader["Phone"]?.ToString() ?? string.Empty,
                                Region = reader["Region"]?.ToString() ?? string.Empty, // تمرير المنطقة للموبايل
                                Balance = Convert.ToDecimal(reader["CurrentBalance"]),
                                IsActive = true,
                                LocationId = locationId
                            });
                        }
                    }
                }
                return Ok(customers);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }

        [HttpGet("ledger/{customerId}")]
        public IActionResult GetLedger(int customerId)
        {
            try
            {
                decimal initialBalance = 0;
                var entries = new List<object>();

                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    con.Open();

                    using (SqlCommand cmdInit = new SqlCommand("SELECT ISNULL(Balance, 0) FROM Customers WHERE Id = @Id", con))
                    {
                        cmdInit.Parameters.AddWithValue("@Id", customerId);
                        var result = cmdInit.ExecuteScalar();
                        initialBalance = result != null && result != DBNull.Value ? Convert.ToDecimal(result) : 0;
                    }

                    const string ledgerQuery = @"
                        SELECT 
                            Id as RefId, 
                            InvoiceDate as TransDate, 
                            N'فاتورة مبيعات (' + ISNULL(CAST(Id AS NVARCHAR), '') + N')' as Description, 
                            RemainingAmount as Debit, 
                            0 as Credit, 
                            'Invoice' as TransType 
                        FROM Sales 
                        WHERE CustomerId = @Id AND RemainingAmount > 0
                        UNION ALL
                        SELECT 
                            Id as RefId, 
                            PaymentDate as TransDate, 
                            N'سداد دفعة نقدية' as Description, 
                            0 as Debit, 
                            Amount as Credit, 
                            'Payment' as TransType 
                        FROM CustomerPayments 
                        WHERE CustomerId = @Id
                        UNION ALL
                        SELECT 
                            Id as RefId, 
                            ReturnDate as TransDate, 
                            N'مرتجع مبيعات' as Description, 
                            0 as Debit, 
                            TotalRefundAmount as Credit, 
                            'Return' as TransType 
                        FROM SalesReturns 
                        WHERE CustomerId = @Id AND TotalRefundAmount > 0
                        ORDER BY TransDate ASC";

                    using (SqlCommand cmdLedger = new SqlCommand(ledgerQuery, con))
                    {
                        cmdLedger.Parameters.AddWithValue("@Id", customerId);
                        using (SqlDataReader reader = cmdLedger.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                entries.Add(new
                                {
                                    RefId = Convert.ToInt32(reader["RefId"]),
                                    TransDate = Convert.ToDateTime(reader["TransDate"]),
                                    Description = reader["Description"].ToString(),
                                    Debit = Convert.ToDecimal(reader["Debit"]),
                                    Credit = Convert.ToDecimal(reader["Credit"]),
                                    TransType = reader["TransType"].ToString()
                                });
                            }
                        }
                    }
                }

                return Ok(new { InitialBalance = initialBalance, Entries = entries });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult AddCustomer([FromBody] CustomerRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.LocationId <= 0)
                return BadRequest(new { message = "بيانات العميل غير صالحة." });

            try
            {
                // تم إضافة حقل Region في جملة الإضافة
                const string query = "INSERT INTO Customers (Name, Phone, Region, Balance, IsActive, LocationId) VALUES (@Name, @Phone, @Region, @Balance, 1, @LocationId); SELECT CAST(SCOPE_IDENTITY() AS INT);";

                using (SqlConnection con = new SqlConnection(_connectionString))
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@Name", request.Name.Trim());
                    cmd.Parameters.AddWithValue("@Phone", string.IsNullOrWhiteSpace(request.Phone) ? (object)DBNull.Value : request.Phone.Trim());
                    cmd.Parameters.AddWithValue("@Region", string.IsNullOrWhiteSpace(request.Region) ? (object)DBNull.Value : request.Region.Trim());
                    cmd.Parameters.AddWithValue("@Balance", request.Balance);
                    cmd.Parameters.AddWithValue("@LocationId", request.LocationId);

                    con.Open();
                    int newId = Convert.ToInt32(cmd.ExecuteScalar());

                    return Ok(new { Id = newId });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public IActionResult UpdateCustomer(int id, [FromBody] CustomerRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "بيانات العميل غير صالحة." });

            try
            {
                // تم إضافة حقل Region في جملة التعديل
                const string query = "UPDATE Customers SET Name = @Name, Phone = @Phone, Region = @Region, Balance = @Balance WHERE Id = @Id;";

                using (SqlConnection con = new SqlConnection(_connectionString))
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.Parameters.AddWithValue("@Name", request.Name.Trim());
                    cmd.Parameters.AddWithValue("@Phone", string.IsNullOrWhiteSpace(request.Phone) ? (object)DBNull.Value : request.Phone.Trim());
                    cmd.Parameters.AddWithValue("@Region", string.IsNullOrWhiteSpace(request.Region) ? (object)DBNull.Value : request.Region.Trim());
                    cmd.Parameters.AddWithValue("@Balance", request.Balance);

                    con.Open();
                    cmd.ExecuteNonQuery();
                }
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public IActionResult DeleteCustomer(int id)
        {
            try
            {
                const string query = "UPDATE Customers SET IsActive = 0 WHERE Id = @Id;";

                using (SqlConnection con = new SqlConnection(_connectionString))
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    con.Open();
                    cmd.ExecuteNonQuery();
                }
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }

        [HttpPost("pay")]
        public IActionResult PayDebt([FromBody] PayDebtRequest request)
        {
            if (request == null || request.CustomerId <= 0 || request.Amount <= 0 || request.LocationId <= 0)
                return BadRequest(new { message = "بيانات السداد غير صالحة." });

            using (SqlConnection con = new SqlConnection(_connectionString))
            {
                con.Open();
                using (SqlTransaction trans = con.BeginTransaction())
                {
                    try
                    {
                        using (SqlCommand cmdInsert = new SqlCommand(
                            "INSERT INTO CustomerPayments (CustomerId, Amount, PaymentDate, LocationId) VALUES (@Id, @Amount, GETDATE(), @LocationId);",
                            con, trans))
                        {
                            cmdInsert.Parameters.AddWithValue("@Id", request.CustomerId);
                            cmdInsert.Parameters.AddWithValue("@Amount", request.Amount);
                            cmdInsert.Parameters.AddWithValue("@LocationId", request.LocationId);
                            cmdInsert.ExecuteNonQuery();
                        }

                        using (SqlCommand cmdCash = new SqlCommand(
                            "INSERT INTO CashTransactions (LocationId, TransactionDate, TransactionType, Amount, Description, ReferenceNumber) VALUES (@LocationId, GETDATE(), N'وارد', @Amount, N'سداد مديونية من عميل كود ' + CAST(@Id AS NVARCHAR), N'CustomerDebt');",
                            con, trans))
                        {
                            cmdCash.Parameters.AddWithValue("@LocationId", request.LocationId);
                            cmdCash.Parameters.AddWithValue("@Id", request.CustomerId);
                            cmdCash.Parameters.AddWithValue("@Amount", request.Amount);
                            cmdCash.ExecuteNonQuery();
                        }

                        trans.Commit();
                        return Ok();
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
                    }
                }
            }
        }
    }
}