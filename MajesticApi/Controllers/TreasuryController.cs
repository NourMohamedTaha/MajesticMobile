using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MajesticApi.Controllers
{
    public class CashTransactionDto
    {
        public int LocationId { get; set; }
        public DateTime TransactionDate { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class ShiftClosingDto
    {
        public int BranchID { get; set; }
        public DateTime ClosingDate { get; set; }
        public decimal TotalCash { get; set; }
    }

    // تم تحديث الـ DTO ليطابق الكروت الـ 8 المطلوبة
    public class TreasuryStatsDto
    {
        public decimal DailyCashSales { get; set; }
        public decimal DailyInstaKurdi { get; set; }
        public decimal DailyInstaHakosh { get; set; }
        public decimal DailyTotalSales { get; set; }
        public decimal DailyExpenses { get; set; }
        public decimal DailyAdvances { get; set; }
        public decimal DailyReturns { get; set; }
        public decimal DailyCredit { get; set; }
        public decimal ExpectedCash { get; set; }

        public decimal MonthlyCashSales { get; set; }
        public decimal MonthlyInstaKurdi { get; set; }
        public decimal MonthlyInstaHakosh { get; set; }
        public decimal MonthlyTotalSales { get; set; }
        public decimal MonthlyExpenses { get; set; }
        public decimal MonthlyAdvances { get; set; }
        public decimal MonthlyReturns { get; set; }
        public decimal MonthlyCredit { get; set; }
        public decimal MonthlyAvailableCash { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class TreasuryController : ControllerBase
    {
        private readonly string _connectionString;

        public TreasuryController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        [HttpPost("Transactions")]
        public async Task<IActionResult> SyncTransactions([FromBody] List<CashTransactionDto> transactions)
        {
            if (transactions == null || transactions.Count == 0) return BadRequest("لا توجد بيانات.");
            int savedCount = 0;
            try
            {
                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    await con.OpenAsync();
                    string query = @"
                        INSERT INTO CashTransactions (TransactionDate, TransactionType, Amount, Description, LocationId, IsClosed, ReferenceNumber) 
                        VALUES (@Date, @Type, @Amount, @Desc, @LocId, 0, N'تطبيق الموبايل')";

                    foreach (var trans in transactions)
                    {
                        using (SqlCommand cmd = new SqlCommand(query, con))
                        {
                            cmd.Parameters.AddWithValue("@Date", trans.TransactionDate);
                            // هنا بنسجل نوع الحركة زي ما الموبايل باعته (مصروفات محل أو سلفة)
                            cmd.Parameters.AddWithValue("@Type", trans.TransactionType);
                            cmd.Parameters.AddWithValue("@Amount", trans.Amount);
                            cmd.Parameters.AddWithValue("@Desc", trans.Description ?? "");
                            cmd.Parameters.AddWithValue("@LocId", trans.LocationId);
                            await cmd.ExecuteNonQueryAsync();
                            savedCount++;
                        }
                    }
                }
                return Ok(new { message = $"تم حفظ {savedCount} حركة نقدية." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message });
            }
        }

        [HttpGet("Stats")]
        public async Task<IActionResult> GetTreasuryStats([FromQuery] int locationId)
        {
            try
            {
                var stats = new TreasuryStatsDto();
                using (SqlConnection con = new SqlConnection(_connectionString))
                {
                    await con.OpenAsync();

                    string query = @"
                        -- ==================== 1: إحصائيات اليومية المفتوحة ====================
                        SELECT 
                            ISNULL(SUM(CASE WHEN PaymentMethod IS NULL OR PaymentMethod IN (N'نقدي', N'كاش') THEN PaidAmount ELSE 0 END), 0) AS CashSales,
                            ISNULL(SUM(CASE WHEN PaymentMethod LIKE N'%الكردي%' THEN PaidAmount ELSE 0 END), 0) AS InstaKurdi,
                            ISNULL(SUM(CASE WHEN PaymentMethod LIKE N'%الحكش%' THEN PaidAmount ELSE 0 END), 0) AS InstaHakosh,
                            ISNULL(SUM(NetAmount), 0) AS TotalSales
                        FROM Sales WHERE ISNULL(LocationId, 1) = @Loc AND ISNULL(IsShiftClosed, 0) = 0;

                        SELECT 
                            ISNULL(SUM(CASE WHEN TransactionType IN (N'مصروف', N'مصروفات محل', N'مرتبات', N'بنزين سيارة', N'أخرى') THEN Amount ELSE 0 END), 0) AS Expenses,
                            ISNULL(SUM(CASE WHEN TransactionType IN (N'سلفة', N'سلف') THEN Amount ELSE 0 END), 0) AS Advances
                        FROM CashTransactions WHERE ISNULL(LocationId, 1) = @Loc AND ISNULL(IsClosed, 0) = 0;

                        SELECT ISNULL(SUM(TotalAmount), 0) AS Returns FROM Returns WHERE LocationId = @Loc AND ISNULL(IsShiftClosed, 0) = 0;

                        SELECT ISNULL(SUM(CASE WHEN TransactionType IN (N'وارد', N'مبيعات') THEN Amount ELSE -Amount END), 0) AS ExpectedCash
                        FROM CashTransactions WHERE ISNULL(LocationId, 1) = @Loc AND ISNULL(IsClosed, 0) = 0
                        AND ISNULL(Description,'') NOT LIKE N'%انستا%' AND ISNULL(Description,'') NOT LIKE N'%فودافون كاش%';

                        -- ==================== 2: إحصائيات الشهرية (اليوميات المقفلة فقط) ====================
                        SELECT 
                            ISNULL(SUM(CASE WHEN PaymentMethod IS NULL OR PaymentMethod IN (N'نقدي', N'كاش') THEN PaidAmount ELSE 0 END), 0) AS CashSales,
                            ISNULL(SUM(CASE WHEN PaymentMethod LIKE N'%الكردي%' THEN PaidAmount ELSE 0 END), 0) AS InstaKurdi,
                            ISNULL(SUM(CASE WHEN PaymentMethod LIKE N'%الحكش%' THEN PaidAmount ELSE 0 END), 0) AS InstaHakosh,
                            ISNULL(SUM(NetAmount), 0) AS TotalSales
                        FROM Sales WHERE ISNULL(LocationId, 1) = @Loc AND IsShiftClosed = 1 AND ISNULL(IsMonthlyClosed, 0) = 0;

                        SELECT 
                            ISNULL(SUM(CASE WHEN TransactionType IN (N'مصروف', N'مصروفات محل', N'مرتبات', N'بنزين سيارة', N'أخرى') THEN Amount ELSE 0 END), 0) AS Expenses,
                            ISNULL(SUM(CASE WHEN TransactionType IN (N'سلفة', N'سلف') THEN Amount ELSE 0 END), 0) AS Advances
                        FROM CashTransactions WHERE ISNULL(LocationId, 1) = @Loc AND IsClosed = 1 AND ISNULL(IsMonthlyClosed, 0) = 0;

                        SELECT ISNULL(SUM(TotalAmount), 0) AS Returns FROM Returns WHERE LocationId = @Loc AND IsShiftClosed = 1 AND ISNULL(IsMonthlyClosed, 0) = 0;

                        -- الرصيد المرحل من اليوميات لدرج المركز الرئيسي
                        SELECT ISNULL(SUM(Debit) - SUM(Credit), 0) AS MonthlyAvailable FROM BranchMonthlyTreasury WHERE BranchID = @Loc;
                    ";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@Loc", locationId);
                        using (SqlDataReader r = await cmd.ExecuteReaderAsync())
                        {
                            // اليومية
                            if (await r.ReadAsync()) { stats.DailyCashSales = Convert.ToDecimal(r["CashSales"]); stats.DailyInstaKurdi = Convert.ToDecimal(r["InstaKurdi"]); stats.DailyInstaHakosh = Convert.ToDecimal(r["InstaHakosh"]); stats.DailyTotalSales = Convert.ToDecimal(r["TotalSales"]); }
                            if (await r.NextResultAsync() && await r.ReadAsync()) { stats.DailyExpenses = Convert.ToDecimal(r["Expenses"]); stats.DailyAdvances = Convert.ToDecimal(r["Advances"]); }
                            if (await r.NextResultAsync() && await r.ReadAsync()) stats.DailyReturns = Convert.ToDecimal(r["Returns"]);
                            if (await r.NextResultAsync() && await r.ReadAsync()) stats.ExpectedCash = Convert.ToDecimal(r["ExpectedCash"]);

                            // الشهرية
                            if (await r.NextResultAsync() && await r.ReadAsync()) { stats.MonthlyCashSales = Convert.ToDecimal(r["CashSales"]); stats.MonthlyInstaKurdi = Convert.ToDecimal(r["InstaKurdi"]); stats.MonthlyInstaHakosh = Convert.ToDecimal(r["InstaHakosh"]); stats.MonthlyTotalSales = Convert.ToDecimal(r["TotalSales"]); }
                            if (await r.NextResultAsync() && await r.ReadAsync()) { stats.MonthlyExpenses = Convert.ToDecimal(r["Expenses"]); stats.MonthlyAdvances = Convert.ToDecimal(r["Advances"]); }
                            if (await r.NextResultAsync() && await r.ReadAsync()) stats.MonthlyReturns = Convert.ToDecimal(r["Returns"]);
                            if (await r.NextResultAsync() && await r.ReadAsync()) stats.MonthlyAvailableCash = Convert.ToDecimal(r["MonthlyAvailable"]);
                        }
                    }

                    // حساب الأجل للشركة
                    string debtsQ = @"
                        SELECT ISNULL(SUM(CurrentBalance), 0) FROM (
                            SELECT (ISNULL(c.Balance, 0) 
                                + ISNULL((SELECT SUM(RemainingAmount) FROM Sales WHERE CustomerId = c.Id AND RemainingAmount > 0), 0)
                                - ISNULL((SELECT SUM(TotalRefundAmount) FROM SalesReturns WHERE CustomerId = c.Id), 0)
                                - ISNULL((SELECT SUM(Amount) FROM CustomerPayments WHERE CustomerId = c.Id), 0)) AS CurrentBalance
                            FROM Customers c WHERE c.IsActive = 1 AND c.LocationId = @Loc
                        ) AS TempDebts;";

                    using (SqlCommand cmdD = new SqlCommand(debtsQ, con))
                    {
                        cmdD.Parameters.AddWithValue("@Loc", locationId);
                        object res = await cmdD.ExecuteScalarAsync();
                        decimal debts = res != null && res != DBNull.Value ? Convert.ToDecimal(res) : 0;
                        stats.DailyCredit = debts;
                        stats.MonthlyCredit = debts;
                    }
                }
                return Ok(stats);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ", error = ex.Message });
            }
        }

        [HttpPost("CloseShift")]
        public async Task<IActionResult> CloseShift([FromBody] ShiftClosingDto closing)
        {
            using (SqlConnection con = new SqlConnection(_connectionString))
            {
                await con.OpenAsync();
                using (SqlTransaction trans = con.BeginTransaction())
                {
                    try
                    {
                        decimal expectedCash = 0;
                        using (SqlCommand cmdCalc = new SqlCommand(@"SELECT ISNULL(SUM(CASE WHEN TransactionType = N'وارد' OR TransactionType = N'مبيعات' THEN Amount ELSE -Amount END), 0) FROM CashTransactions WHERE ISNULL(LocationId,1) = @B AND ISNULL(IsClosed,0) = 0 AND ISNULL(Description,'') NOT LIKE N'%انستا%' AND ISNULL(Description,'') NOT LIKE N'%فودافون كاش%'", con, trans))
                        { cmdCalc.Parameters.AddWithValue("@B", closing.BranchID); object result = await cmdCalc.ExecuteScalarAsync(); expectedCash = result != null ? Convert.ToDecimal(result) : 0; }

                        if (expectedCash > 0)
                        {
                            using (SqlCommand cmdMonthly = new SqlCommand("INSERT INTO BranchMonthlyTreasury (TransactionDate, Debit, Credit, Note, BranchID) VALUES (GETDATE(), @Actual, 0, N'ترحيل صافي درج', @BranchID)", con, trans))
                            { cmdMonthly.Parameters.AddWithValue("@Actual", expectedCash); cmdMonthly.Parameters.AddWithValue("@BranchID", closing.BranchID); await cmdMonthly.ExecuteNonQueryAsync(); }
                        }

                        using (SqlCommand cmdClose = new SqlCommand("UPDATE CashTransactions SET IsClosed = 1 WHERE ISNULL(LocationId,1) = @B AND ISNULL(IsClosed,0) = 0", con, trans))
                        { cmdClose.Parameters.AddWithValue("@B", closing.BranchID); await cmdClose.ExecuteNonQueryAsync(); }

                        using (SqlCommand cmdCloseSales = new SqlCommand("UPDATE Sales SET IsShiftClosed = 1 WHERE ISNULL(LocationId,1) = @B AND ISNULL(IsShiftClosed,0) = 0", con, trans))
                        { cmdCloseSales.Parameters.AddWithValue("@B", closing.BranchID); await cmdCloseSales.ExecuteNonQueryAsync(); }

                        using (SqlCommand cmdCloseReturns = new SqlCommand("UPDATE Returns SET IsShiftClosed = 1 WHERE LocationId = @B AND ISNULL(IsShiftClosed,0) = 0", con, trans))
                        { cmdCloseReturns.Parameters.AddWithValue("@B", closing.BranchID); await cmdCloseReturns.ExecuteNonQueryAsync(); }

                        trans.Commit(); return Ok(new { message = "تم تقفيل الوردية بنجاح.", totalCash = expectedCash });
                    }
                    catch (Exception ex) { trans.Rollback(); return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message }); }
                }
            }
        }

        [HttpPost("CloseMonth")]
        public async Task<IActionResult> CloseMonth([FromBody] ShiftClosingDto closing)
        {
            using (SqlConnection con = new SqlConnection(_connectionString))
            {
                await con.OpenAsync();
                using (SqlTransaction trans = con.BeginTransaction())
                {
                    try
                    {
                        decimal netBalance = 0;
                        using (SqlCommand cmdCalc = new SqlCommand("SELECT ISNULL(SUM(Debit) - SUM(Credit), 0) FROM BranchMonthlyTreasury WHERE BranchID = @B", con, trans))
                        { cmdCalc.Parameters.AddWithValue("@B", closing.BranchID); object result = await cmdCalc.ExecuteScalarAsync(); netBalance = result != null ? Convert.ToDecimal(result) : 0; }

                        if (netBalance > 0)
                        {
                            using (SqlCommand cmd = new SqlCommand("INSERT INTO BranchMonthlyTreasury (TransactionDate, Debit, Credit, Note, BranchID) VALUES (GETDATE(), @Debit, 0, N'ترحيل شهرية', @BranchID)", con, trans))
                            { cmd.Parameters.AddWithValue("@Debit", netBalance); cmd.Parameters.AddWithValue("@BranchID", closing.BranchID); await cmd.ExecuteNonQueryAsync(); }
                        }

                        using (SqlCommand cmdDel = new SqlCommand("DELETE FROM BranchMonthlyTreasury WHERE BranchID = @B", con, trans))
                        { cmdDel.Parameters.AddWithValue("@B", closing.BranchID); await cmdDel.ExecuteNonQueryAsync(); }

                        using (SqlCommand cmdCSM = new SqlCommand("UPDATE Sales SET IsMonthlyClosed = 1 WHERE ISNULL(LocationId,1) = @B AND ISNULL(IsMonthlyClosed,0) = 0", con, trans))
                        { cmdCSM.Parameters.AddWithValue("@B", closing.BranchID); await cmdCSM.ExecuteNonQueryAsync(); }

                        using (SqlCommand cmdCR = new SqlCommand("UPDATE Returns SET IsMonthlyClosed = 1 WHERE LocationId = @B AND ISNULL(IsMonthlyClosed,0) = 0", con, trans))
                        { cmdCR.Parameters.AddWithValue("@B", closing.BranchID); await cmdCR.ExecuteNonQueryAsync(); }

                        using (SqlCommand cmdCDM = new SqlCommand("UPDATE CashTransactions SET IsMonthlyClosed = 1 WHERE ISNULL(LocationId,1) = @B AND ISNULL(IsMonthlyClosed,0) = 0", con, trans))
                        { cmdCDM.Parameters.AddWithValue("@B", closing.BranchID); await cmdCDM.ExecuteNonQueryAsync(); }

                        trans.Commit(); return Ok(new { message = "تم التوريد للمركز الرئيسي بنجاح وتصفير رصيد الفرع.", totalCash = netBalance });
                    }
                    catch (Exception ex) { trans.Rollback(); return StatusCode(500, new { message = "خطأ في السيرفر", error = ex.Message }); }
                }
            }
        }
    }
}