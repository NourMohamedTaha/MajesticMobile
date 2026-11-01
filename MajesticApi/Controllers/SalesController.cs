using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace MajesticApi.Controllers
{
    public class SaleSyncDetailRequest
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }

    public class SaleSyncRequest
    {
        public DateTime InvoiceDate { get; set; }
        public string CustomerName { get; set; } = "عميل نقدي";
        public int? CustomerId { get; set; }
        public int LocationId { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal Discount { get; set; }
        public decimal NetAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal RemainingAmount { get; set; }
        public string PaymentMethod { get; set; } = "نقدي";
        public string Notes { get; set; } = string.Empty;
        public List<SaleSyncDetailRequest> Details { get; set; } = new();
    }

    [Route("api/[controller]")]
    [ApiController]
    public class SalesController : ControllerBase
    {
        private readonly string _connectionString;

        public SalesController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        // دالة جلب وسائل الدفع (تم تعديلها لتطابق الديسكتوب تماماً)
        [HttpGet("PaymentMethods")]
        public IActionResult GetPaymentMethods()
        {
            var methods = new List<string> { "نقدي", "إنستاباي الكردي", "إنستاباي الحكش", "آجل" };
            return Ok(methods);
        }

        [HttpPost]
        public IActionResult CreateSale([FromBody] SaleSyncRequest request)
        {
            if (request == null || request.Details == null || request.Details.Count == 0)
                return BadRequest(new { message = "الفاتورة فارغة ولا تحتوي على أصناف." });

            if (request.LocationId <= 0)
                return BadRequest(new { message = "رقم الفرع غير صالح." });

            // هنا صلحنا المشكلة: السيرفر هياخد الكلمة اللي جاية من الموبايل زي ما هي بدون تعديل
            // عشان تتطابق مع شاشة الديسكتوب بالضبط
            string finalPaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "نقدي" : request.PaymentMethod.Trim();

            using (SqlConnection con = new SqlConnection(_connectionString))
            {
                con.Open();

                using (SqlTransaction trans = con.BeginTransaction())
                {
                    try
                    {
                        int saleId;

                        const string insertSaleQuery = @"
                            INSERT INTO Sales
                            (
                                InvoiceNumber, CustomerId, LocationId, InvoiceDate,
                                TotalAmount, Discount, NetAmount, PaidAmount,
                                RemainingAmount, PaymentMethod, Notes
                            )
                            VALUES
                            (
                                N'TEMP', @CustomerId, @LocationId, @InvoiceDate,
                                @TotalAmount, @Discount, @NetAmount, @PaidAmount,
                                @RemainingAmount, @PaymentMethod, @Notes
                            );
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";

                        using (SqlCommand cmdSale = new SqlCommand(insertSaleQuery, con, trans))
                        {
                            cmdSale.Parameters.AddWithValue("@CustomerId", request.CustomerId.HasValue ? (object)request.CustomerId.Value : DBNull.Value);
                            cmdSale.Parameters.AddWithValue("@LocationId", request.LocationId);
                            cmdSale.Parameters.AddWithValue("@InvoiceDate", request.InvoiceDate);
                            cmdSale.Parameters.AddWithValue("@TotalAmount", request.TotalAmount);
                            cmdSale.Parameters.AddWithValue("@Discount", request.Discount);
                            cmdSale.Parameters.AddWithValue("@NetAmount", request.NetAmount);
                            cmdSale.Parameters.AddWithValue("@PaidAmount", request.PaidAmount);
                            cmdSale.Parameters.AddWithValue("@RemainingAmount", request.RemainingAmount);
                            cmdSale.Parameters.AddWithValue("@PaymentMethod", finalPaymentMethod);
                            cmdSale.Parameters.AddWithValue("@Notes", request.Notes ?? string.Empty);

                            saleId = Convert.ToInt32(cmdSale.ExecuteScalar());
                        }

                        string finalInvoiceNumber = $"MOB-{request.LocationId}-{DateTime.Now:yyyyMMdd}-{saleId:D4}";

                        using (SqlCommand cmdNum = new SqlCommand("UPDATE Sales SET InvoiceNumber = @Num WHERE Id = @Id", con, trans))
                        {
                            cmdNum.Parameters.AddWithValue("@Num", finalInvoiceNumber);
                            cmdNum.Parameters.AddWithValue("@Id", saleId);
                            cmdNum.ExecuteNonQuery();
                        }

                        foreach (var detail in request.Details)
                        {
                            using (SqlCommand cmdStock = new SqlCommand(
                                "UPDATE Stock SET Quantity = Quantity - @Qty WHERE ProductId = @ProdId AND LocationId = @LocationId AND Quantity >= @Qty", con, trans))
                            {
                                cmdStock.Parameters.AddWithValue("@Qty", detail.Quantity);
                                cmdStock.Parameters.AddWithValue("@ProdId", detail.ProductId);
                                cmdStock.Parameters.AddWithValue("@LocationId", request.LocationId);

                                int affected = cmdStock.ExecuteNonQuery();

                                if (affected == 0)
                                {
                                    throw new Exception($"الكمية غير متوفرة للصنف '{detail.ProductName}' في هذا الفرع.");
                                }
                            }

                            using (SqlCommand cmdDetail = new SqlCommand(
                                "INSERT INTO SaleDetails (SaleId, ProductId, Quantity, UnitPrice, TotalPrice) VALUES (@SaleId, @ProductId, @Quantity, @UnitPrice, @TotalPrice)", con, trans))
                            {
                                cmdDetail.Parameters.AddWithValue("@SaleId", saleId);
                                cmdDetail.Parameters.AddWithValue("@ProductId", detail.ProductId);
                                cmdDetail.Parameters.AddWithValue("@Quantity", detail.Quantity);
                                cmdDetail.Parameters.AddWithValue("@UnitPrice", detail.UnitPrice);
                                cmdDetail.Parameters.AddWithValue("@TotalPrice", detail.TotalPrice);
                                cmdDetail.ExecuteNonQuery();
                            }
                        }

                        if (request.PaidAmount > 0 && finalPaymentMethod != "آجل")
                        {
                            string transType = finalPaymentMethod == "نقدي" ? "مبيعات" : "وارد";
                            string descSuffix = finalPaymentMethod == "نقدي" ? "" : $" - {finalPaymentMethod}";

                            // تم إضافة PaymentSource هنا لتسمع في الخزنة المركزية أو اليومية
                            using (SqlCommand cmdCash = new SqlCommand(
                                "INSERT INTO CashTransactions (LocationId, TransactionDate, TransactionType, Amount, Description, ReferenceNumber, IsClosed, PaymentSource) VALUES (@LocationId, GETDATE(), @TransType, @Amount, @Description, @RefNo, 0, @PaymentSource)", con, trans))
                            {
                                cmdCash.Parameters.AddWithValue("@LocationId", request.LocationId);
                                cmdCash.Parameters.AddWithValue("@TransType", transType);
                                cmdCash.Parameters.AddWithValue("@Amount", request.PaidAmount);
                                cmdCash.Parameters.AddWithValue("@Description", $"إيراد مبيعات موبايل{descSuffix} - فاتورة رقم {finalInvoiceNumber}");
                                cmdCash.Parameters.AddWithValue("@RefNo", finalInvoiceNumber);
                                cmdCash.Parameters.AddWithValue("@PaymentSource", finalPaymentMethod);
                                cmdCash.ExecuteNonQuery();
                            }
                        }

                        trans.Commit();

                        return Ok(new
                        {
                            SaleId = saleId,
                            ServerId = saleId,
                            InvoiceNumber = finalInvoiceNumber
                        });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        return BadRequest(new { message = ex.Message });
                    }
                }
            }
        }

        [HttpGet("DailyInvoices")]
        public IActionResult GetDailyInvoices([FromQuery] int locationId)
        {
            var invoices = new List<object>();
            try
            {
                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    string query = @"
                        SELECT s.InvoiceNumber, s.InvoiceDate, ISNULL(c.Name, N'عميل نقدي') AS CustomerName,
                               s.TotalAmount, s.Discount, s.NetAmount, s.PaidAmount, s.RemainingAmount, s.PaymentMethod
                        FROM Sales s
                        LEFT JOIN Customers c ON s.CustomerId = c.Id
                        WHERE s.LocationId = @LocationId 
                          AND ISNULL(s.IsShiftClosed, 0) = 0
                        ORDER BY s.InvoiceDate DESC";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@LocationId", locationId);
                        con.Open();
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                invoices.Add(new
                                {
                                    InvoiceNumber = reader["InvoiceNumber"].ToString(),
                                    InvoiceDate = Convert.ToDateTime(reader["InvoiceDate"]),
                                    CustomerName = reader["CustomerName"].ToString(),
                                    TotalAmount = Convert.ToDecimal(reader["TotalAmount"]),
                                    Discount = Convert.ToDecimal(reader["Discount"]),
                                    NetAmount = Convert.ToDecimal(reader["NetAmount"]),
                                    PaidAmount = Convert.ToDecimal(reader["PaidAmount"]),
                                    RemainingAmount = Convert.ToDecimal(reader["RemainingAmount"]),
                                    PaymentMethod = reader["PaymentMethod"].ToString()
                                });
                            }
                        }
                    }
                }
                return Ok(invoices);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }
    }
}