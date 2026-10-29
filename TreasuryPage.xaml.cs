using MajesticMobile.Data;
using MajesticMobile.Models;
using MajesticMobile.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.Net.Http.Json;
using System.Collections.Generic;

namespace MajesticMobile
{
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

    public class CashTransactionDto
    {
        public int LocationId { get; set; }
        public DateTime TransactionDate { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public partial class TreasuryPage : ContentPage
    {
        private readonly LocalDbService _dbService;
        private decimal _expectedCash = 0;
        private decimal _monthlyAvailableCash = 0;
        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // المتغير اللي هيحدد الحركة مصروف ولا سلفة
        private string _selectedTransactionType = "مصروفات محل";

        public TreasuryPage(LocalDbService dbService)
        {
            InitializeComponent();
            _dbService = dbService;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                await PushPendingCashTransactionsAsync();
            }
            await LoadTreasuryDataAsync();
        }

        private async void OnRefreshRequested(object sender, EventArgs e)
        {
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                await PushPendingCashTransactionsAsync();
            }
            await LoadTreasuryDataAsync();

            refreshViewDaily.IsRefreshing = false;
            refreshViewMonthly.IsRefreshing = false;
        }

        private async void OnTabClicked(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            btnTabDaily.BackgroundColor = Color.FromArgb("#162130");
            btnTabMonthly.BackgroundColor = Color.FromArgb("#162130");
            btn.BackgroundColor = Color.FromArgb("#3498DB");

            string tab = btn.CommandParameter?.ToString() ?? "";

            // عشان السحب يشتغل صح مع التاب المعروض
            refreshViewDaily.IsVisible = tab == "Daily";
            refreshViewMonthly.IsVisible = tab == "Monthly";

            await LoadTreasuryDataAsync();
        }

        private void OnTransTypeClicked(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            string type = btn.CommandParameter?.ToString() ?? "Expense";

            if (type == "Expense")
            {
                _selectedTransactionType = "مصروفات محل";
                btnTypeExpense.BackgroundColor = Color.FromArgb("#E74C3C");
                btnTypeExpense.TextColor = Colors.White;
                btnTypeAdvance.BackgroundColor = Color.FromArgb("#162130");
                btnTypeAdvance.TextColor = Color.FromArgb("#BDC3C7");
            }
            else
            {
                _selectedTransactionType = "سلفة";
                btnTypeAdvance.BackgroundColor = Color.FromArgb("#E67E22");
                btnTypeAdvance.TextColor = Colors.White;
                btnTypeExpense.BackgroundColor = Color.FromArgb("#162130");
                btnTypeExpense.TextColor = Color.FromArgb("#BDC3C7");
            }
        }

        private async Task LoadTreasuryDataAsync()
        {
            var pendingTrans = await _dbService.GetPendingCashTransactionsAsync();

            // فصلنا السلف عن المصروفات في الحسابات المحلية كمان
            decimal pendingExpenses = pendingTrans.Where(t => t.TransactionType == "مصروفات محل" || t.TransactionType == "مصروف" || t.TransactionType == "صادر").Sum(t => t.Amount);
            decimal pendingAdvances = pendingTrans.Where(t => t.TransactionType == "سلفة" || t.TransactionType == "سلف").Sum(t => t.Amount);

            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet && BranchContext.IsConfigured)
            {
                try
                {
                    string url = $"{ApiConfig.BaseUrl}/api/Treasury/Stats?locationId={BranchContext.LocationId}";
                    var stats = await _httpClient.GetFromJsonAsync<TreasuryStatsDto>(url);

                    if (stats != null)
                    {
                        // اليومية المفتوحة
                        lblDailyTotalSales.Text = stats.DailyTotalSales.ToString("N2");
                        lblDailyCash.Text = stats.DailyCashSales.ToString("N2");
                        lblDailyInstaKurdi.Text = stats.DailyInstaKurdi.ToString("N2");
                        lblDailyInstaHakosh.Text = stats.DailyInstaHakosh.ToString("N2");

                        lblDailyExpenses.Text = (stats.DailyExpenses + pendingExpenses).ToString("N2");
                        lblDailyAdvances.Text = (stats.DailyAdvances + pendingAdvances).ToString("N2");
                        lblDailyReturns.Text = stats.DailyReturns.ToString("N2");
                        lblDailyCredit.Text = stats.DailyCredit.ToString("N2");

                        // الدرج بيطرح منه المصروف والسلفة مع بعض
                        _expectedCash = stats.ExpectedCash - pendingExpenses - pendingAdvances;
                        lblExpectedCash.Text = _expectedCash.ToString("N2");

                        // الشهرية (اليوميات المقفلة)
                        lblMonthlyTotalSales.Text = stats.MonthlyTotalSales.ToString("N2");
                        lblMonthlyCash.Text = stats.MonthlyCashSales.ToString("N2");
                        lblMonthlyInstaKurdi.Text = stats.MonthlyInstaKurdi.ToString("N2");
                        lblMonthlyInstaHakosh.Text = stats.MonthlyInstaHakosh.ToString("N2");

                        lblMonthlyExpenses.Text = stats.MonthlyExpenses.ToString("N2");
                        lblMonthlyAdvances.Text = stats.MonthlyAdvances.ToString("N2");
                        lblMonthlyReturns.Text = stats.MonthlyReturns.ToString("N2");
                        lblMonthlyCredit.Text = stats.MonthlyCredit.ToString("N2");

                        _monthlyAvailableCash = stats.MonthlyAvailableCash;
                        lblMonthlyAvailable.Text = _monthlyAvailableCash.ToString("N2") + " ج.م";
                        return;
                    }
                }
                catch { } // سيبناه يقلب أوفلاين بهدوء لو النت قطع
            }

            // اللوجيك لو أوفلاين (يومي فقط، الشهري معتمد كلياً على السيرفر)
            var todayInvoices = await _dbService.GetTodayInvoicesAsync();
            decimal cashSales = todayInvoices.Where(i => i.PaymentMethod == "نقدي" || i.PaymentMethod == "كاش").Sum(i => (decimal)i.PaidAmount);
            decimal instaKurdi = todayInvoices.Where(i => i.PaymentMethod.Contains("الكردي")).Sum(i => (decimal)i.PaidAmount);
            decimal instaHakosh = todayInvoices.Where(i => i.PaymentMethod.Contains("الحكش")).Sum(i => (decimal)i.PaidAmount);
            decimal totalSales = todayInvoices.Sum(i => (decimal)i.NetAmount);
            decimal localReturns = todayInvoices.Where(i => i.IsReturn).Sum(i => (decimal)i.PaidAmount);

            var todayTrans = await _dbService.GetTodayCashTransactionsAsync();
            decimal totalExpensesLocal = todayTrans.Where(t => t.TransactionType == "مصروفات محل" || t.TransactionType == "صادر" || t.TransactionType == "أخرى").Sum(t => (decimal)t.Amount);
            decimal totalAdvancesLocal = todayTrans.Where(t => t.TransactionType == "سلفة").Sum(t => (decimal)t.Amount);

            _expectedCash = cashSales - totalExpensesLocal - totalAdvancesLocal - localReturns;

            lblDailyTotalSales.Text = totalSales.ToString("N2");
            lblDailyCash.Text = cashSales.ToString("N2");
            lblDailyInstaKurdi.Text = instaKurdi.ToString("N2");
            lblDailyInstaHakosh.Text = instaHakosh.ToString("N2");

            lblDailyExpenses.Text = totalExpensesLocal.ToString("N2");
            lblDailyAdvances.Text = totalAdvancesLocal.ToString("N2");
            lblDailyReturns.Text = localReturns.ToString("N2");
            lblDailyCredit.Text = "غير متاح";
            lblExpectedCash.Text = _expectedCash.ToString("N2");

            _monthlyAvailableCash = 0; // مقفول للأوفلاين
            lblMonthlyAvailable.Text = "يتطلب إنترنت";
        }

        private async void OnSaveExpenseClicked(object sender, EventArgs e)
        {
            if (!decimal.TryParse(entryExpenseAmount.Text, out decimal amount) || amount <= 0)
            {
                await DisplayAlert("تنبيه", "برجاء إدخال مبلغ صحيح.", "موافق");
                return;
            }

            if (!BranchContext.IsConfigured)
            {
                await DisplayAlert("تنبيه", "لم يتم تحديد الفرع على هذا الجهاز بعد.", "موافق");
                return;
            }

            string notes = entryExpenseNotes.Text ?? "";

            // العنوان بيوضح إن كانت سلفة ولا مصروف
            string description = _selectedTransactionType == "سلفة" ? "سلفة عامل" : "مصروفات";
            if (!string.IsNullOrWhiteSpace(notes)) description += $" - {notes}";

            var expense = new CashTransactionModel
            {
                TransactionDate = DateTime.Now,
                TransactionType = _selectedTransactionType, // مصروفات محل أو سلفة
                Amount = amount,
                Description = description,
                LocationId = BranchContext.LocationId,
                IsSynced = false
            };

            await _dbService.SaveCashTransactionAsync(expense);

            entryExpenseAmount.Text = "";
            entryExpenseNotes.Text = "";

            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                bool synced = await PushPendingCashTransactionsAsync();
                if (synced)
                    await DisplayAlert("نجاح", $"تم تسجيل ה{_selectedTransactionType} ورفعها للسيرفر بنجاح.", "موافق");
                else
                    await DisplayAlert("نجاح جزئي", $"تم حفظ ה{_selectedTransactionType} محلياً، وسيتم رفعها لاحقاً.", "موافق");
            }
            else
            {
                await DisplayAlert("تم الحفظ محلياً", "العملية محفوظة على الجهاز، وهيترفع للسيرفر أول ما يرجع النت.", "موافق");
            }

            await LoadTreasuryDataAsync();
        }

        private async Task<bool> PushPendingCashTransactionsAsync()
        {
            if (!SyncLock.TryEnter()) return false;

            try
            {
                var pending = await _dbService.GetPendingCashTransactionsAsync();
                if (pending.Count == 0) return true;

                var payload = pending.Select(t => new CashTransactionDto
                {
                    LocationId = BranchContext.LocationId,
                    TransactionDate = t.TransactionDate,
                    TransactionType = t.TransactionType,
                    Amount = t.Amount,
                    Description = t.Description ?? ""
                }).ToList();

                string url = $"{ApiConfig.BaseUrl}/api/Treasury/Transactions";
                var response = await _httpClient.PostAsJsonAsync(url, payload);

                if (response.IsSuccessStatusCode)
                {
                    foreach (var t in pending) await _dbService.MarkCashTransactionAsSyncedAsync(t.Id);
                    return true;
                }
                return false;
            }
            catch { return false; }
            finally { SyncLock.Exit(); }
        }

        private async void OnCloseShiftClicked(object sender, EventArgs e)
        {
            if (_expectedCash <= 0)
            {
                await DisplayAlert("تنبيه", "رصيد الدرج صفر أو سالب، لا يوجد مبلغ لترحيله.", "موافق");
                return;
            }

            bool confirm = await DisplayAlert("تأكيد", $"هل أنت متأكد من تقفيل الوردية؟\nسيتم ترحيل مبلغ ({_expectedCash:N2} ج.م) للخزنة الشهرية.", "نعم، قفل الوردية", "إلغاء");
            if (!confirm) return;

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet || !BranchContext.IsConfigured)
            {
                await DisplayAlert("خطأ اتصال", "لازم تكون متصل بالإنترنت عشان تقفل الوردية وترحلها للسيرفر.", "موافق");
                return;
            }

            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;

            try
            {
                bool pushed = await PushPendingCashTransactionsAsync();
                if (!pushed)
                {
                    await DisplayAlert("تنبيه", "فيه حركات لسه محفوظة محلياً ومعرفتش أرفعها. السيرفر هيعتمد الأرقام اللي عنده بس حالياً. هل توافق؟", "موافق", "إلغاء");
                }

                var closingDto = new { BranchID = BranchContext.LocationId, ClosingDate = DateTime.Now, TotalCash = _expectedCash };
                var response = await _httpClient.PostAsJsonAsync($"{ApiConfig.BaseUrl}/api/Treasury/CloseShift", closingDto);

                if (response.IsSuccessStatusCode)
                {
                    await DisplayAlert("نجاح", "تم تقفيل الوردية وترحيل المبلغ بنجاح!", "موافق");
                    await LoadTreasuryDataAsync();
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    await DisplayAlert("خطأ", $"السيرفر رفض التقفيل:\n{error}", "موافق");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("مشكلة في الاتصال", $"تعذر الوصول للسيرفر:\n{ex.Message}", "موافق");
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void OnTransferToMainClicked(object sender, EventArgs e)
        {
            if (_monthlyAvailableCash <= 0)
            {
                await DisplayAlert("تنبيه", "لا يوجد رصيد متاح لتوريده للمركز الرئيسي.", "موافق");
                return;
            }

            decimal totalAvailable = _monthlyAvailableCash;
            bool confirm = await DisplayAlert("تأكيد", $"هل أنت متأكد من توريد مبلغ ({totalAvailable:N2} ج.م) للمركز الرئيسي وتصفير رصيد الفرع؟", "نعم، قم بالتوريد", "إلغاء");
            if (!confirm) return;

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet || !BranchContext.IsConfigured)
            {
                await DisplayAlert("خطأ اتصال", "يجب الاتصال بالإنترنت لتنفيذ التوريد المركزي.", "موافق");
                return;
            }

            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;

            try
            {
                var closingDto = new { BranchID = BranchContext.LocationId, ClosingDate = DateTime.Now, TotalCash = totalAvailable };
                var response = await _httpClient.PostAsJsonAsync($"{ApiConfig.BaseUrl}/api/Treasury/CloseMonth", closingDto);

                if (response.IsSuccessStatusCode)
                {
                    await DisplayAlert("نجاح", "تم توريد الكاش للمركز الرئيسي بنجاح!", "موافق");
                    await LoadTreasuryDataAsync();
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    await DisplayAlert("خطأ", $"فشل التوريد:\n{error}", "موافق");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("مشكلة في الاتصال", $"تعذر الوصول للسيرفر:\n{ex.Message}", "موافق");
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }
    }
}