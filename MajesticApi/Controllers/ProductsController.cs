using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace MajesticApi.Controllers
{
    public class NewBranchProductRequest
    {
        public string Name { get; set; } = string.Empty;
        public double StockQuantity { get; set; }
        public decimal PurchasePrice { get; set; }
        public int LocationId { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly string _connectionString;
        private const string BranchCategoryName = "منتجات الفرع";

        public ProductsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        [HttpGet]
        public IActionResult GetProducts([FromQuery] int locationId = 1)
        {
            var products = new List<object>();

            try
            {
                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    // تم إضافة p.AlertLimit هنا عشان السيرفر يبعت حد التنبيه للموبايل
                    string query = @"
                        SELECT 
                            p.Id, 
                            p.Name, 
                            p.CategoryId,
                            ISNULL(p.AlertLimit, 0) AS AlertLimit,
                            CASE 
                                WHEN p.BranchId = @LocationId THEN ISNULL(bp.PurchasePrice, ISNULL(p.PurchasePrice, 0))
                                ELSE 0
                            END AS PurchasePrice,
                            ISNULL(s.Quantity, 0) AS CurrentQuantity 
                        FROM Products p
                        LEFT JOIN Stock s ON p.Id = s.ProductId AND s.LocationId = @LocationId
                        LEFT JOIN BranchProducts bp ON p.Id = bp.ProductID AND bp.BranchID = @LocationId
                        WHERE p.IsActive = 1
                          AND (p.BranchId IS NULL OR p.BranchId = @LocationId)";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@LocationId", locationId);

                        con.Open();
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                products.Add(new
                                {
                                    ServerId = reader["Id"],
                                    Name = reader["Name"].ToString(),
                                    CategoryId = Convert.ToInt32(reader["CategoryId"]),
                                    PurchasePrice = Convert.ToDecimal(reader["PurchasePrice"]),
                                    StockQuantity = Convert.ToDouble(reader["CurrentQuantity"]),
                                    // تم ربط الحقل هنا عشان يتبعت في الـ JSON
                                    AlertLimit = Convert.ToDouble(reader["AlertLimit"])
                                });
                            }
                        }
                    }
                }
                return Ok(products);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult AddBranchProduct([FromBody] NewBranchProductRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.LocationId <= 0)
                return BadRequest(new { message = "بيانات المنتج غير صالحة." });

            using (SqlConnection con = new SqlConnection(_connectionString))
            {
                con.Open();
                using (SqlTransaction trans = con.BeginTransaction())
                {
                    try
                    {
                        int categoryId;
                        using (SqlCommand cmdFindCat = new SqlCommand("SELECT Id FROM Categories WHERE Name = @Name", con, trans))
                        {
                            cmdFindCat.Parameters.AddWithValue("@Name", BranchCategoryName);
                            object result = cmdFindCat.ExecuteScalar();

                            if (result != null)
                            {
                                categoryId = Convert.ToInt32(result);
                            }
                            else
                            {
                                using (SqlCommand cmdInsertCat = new SqlCommand(
                                    "INSERT INTO Categories (Name) VALUES (@Name); SELECT CAST(SCOPE_IDENTITY() AS INT);", con, trans))
                                {
                                    cmdInsertCat.Parameters.AddWithValue("@Name", BranchCategoryName);
                                    categoryId = Convert.ToInt32(cmdInsertCat.ExecuteScalar());
                                }
                            }
                        }

                        int productId = 0;
                        object existingBranchIdObj = null;

                        using (SqlCommand cmdCheck = new SqlCommand("SELECT Id, BranchId FROM Products WHERE Name = @Name", con, trans))
                        {
                            cmdCheck.Parameters.AddWithValue("@Name", request.Name.Trim());
                            using (SqlDataReader reader = cmdCheck.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    productId = Convert.ToInt32(reader["Id"]);
                                    existingBranchIdObj = reader["BranchId"];
                                }
                            }
                        }

                        if (productId > 0)
                        {
                            if (existingBranchIdObj != DBNull.Value && Convert.ToInt32(existingBranchIdObj) != request.LocationId)
                            {
                                using (SqlCommand cmdMakeGlobal = new SqlCommand("UPDATE Products SET BranchId = NULL WHERE Id = @Id", con, trans))
                                {
                                    cmdMakeGlobal.Parameters.AddWithValue("@Id", productId);
                                    cmdMakeGlobal.ExecuteNonQuery();
                                }
                            }
                        }
                        else
                        {
                            using (SqlCommand cmdInsertProduct = new SqlCommand(
                                "INSERT INTO Products (Name, CategoryId, IsActive, AlertLimit, PurchasePrice, BranchId) VALUES (@Name, @CategoryId, 1, 5, @Purchase, @LocationId); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                                con, trans))
                            {
                                cmdInsertProduct.Parameters.AddWithValue("@Name", request.Name.Trim());
                                cmdInsertProduct.Parameters.AddWithValue("@CategoryId", categoryId);
                                cmdInsertProduct.Parameters.AddWithValue("@Purchase", request.PurchasePrice);
                                cmdInsertProduct.Parameters.AddWithValue("@LocationId", request.LocationId);
                                productId = Convert.ToInt32(cmdInsertProduct.ExecuteScalar());
                            }
                        }

                        int existsBp;
                        using (SqlCommand cmdCheckBp = new SqlCommand("SELECT COUNT(1) FROM BranchProducts WHERE BranchID = @LocationId AND ProductID = @ProdId", con, trans))
                        {
                            cmdCheckBp.Parameters.AddWithValue("@LocationId", request.LocationId);
                            cmdCheckBp.Parameters.AddWithValue("@ProdId", productId);
                            existsBp = Convert.ToInt32(cmdCheckBp.ExecuteScalar());
                        }

                        if (existsBp > 0)
                        {
                            using (SqlCommand cmdUpdBp = new SqlCommand("UPDATE BranchProducts SET PurchasePrice = @Purchase WHERE BranchID = @LocationId AND ProductID = @ProdId", con, trans))
                            {
                                cmdUpdBp.Parameters.AddWithValue("@Purchase", request.PurchasePrice);
                                cmdUpdBp.Parameters.AddWithValue("@LocationId", request.LocationId);
                                cmdUpdBp.Parameters.AddWithValue("@ProdId", productId);
                                cmdUpdBp.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            using (SqlCommand cmdInsBp = new SqlCommand("INSERT INTO BranchProducts (BranchID, ProductID, PurchasePrice) VALUES (@LocationId, @ProdId, @Purchase)", con, trans))
                            {
                                cmdInsBp.Parameters.AddWithValue("@LocationId", request.LocationId);
                                cmdInsBp.Parameters.AddWithValue("@ProdId", productId);
                                cmdInsBp.Parameters.AddWithValue("@Purchase", request.PurchasePrice);
                                cmdInsBp.ExecuteNonQuery();
                            }
                        }

                        using (SqlCommand cmdUpdStock = new SqlCommand("UPDATE Stock SET Quantity = @Qty WHERE ProductId = @ProdId AND LocationId = @LocationId", con, trans))
                        {
                            cmdUpdStock.Parameters.AddWithValue("@Qty", request.StockQuantity);
                            cmdUpdStock.Parameters.AddWithValue("@ProdId", productId);
                            cmdUpdStock.Parameters.AddWithValue("@LocationId", request.LocationId);

                            if (cmdUpdStock.ExecuteNonQuery() == 0)
                            {
                                using (SqlCommand cmdInsStock = new SqlCommand("INSERT INTO Stock (ProductId, LocationId, Quantity) VALUES (@ProdId, @LocationId, @Qty)", con, trans))
                                {
                                    cmdInsStock.Parameters.AddWithValue("@ProdId", productId);
                                    cmdInsStock.Parameters.AddWithValue("@LocationId", request.LocationId);
                                    cmdInsStock.Parameters.AddWithValue("@Qty", request.StockQuantity);
                                    cmdInsStock.ExecuteNonQuery();
                                }
                            }
                        }

                        trans.Commit();

                        return Ok(new { ServerId = productId });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        return BadRequest(new { message = ex.Message });
                    }
                }
            }
        }
    }
}