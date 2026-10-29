using MajesticMobile.Data;
using MajesticMobile.Models;
using MajesticMobile.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Storage; // للوصول لاسم المستخدم

namespace MajesticMobile
{
    public partial class MainPage : ContentPage
    {
        private readonly LocalDbService _dbService;
        private static bool _isGreetingShown = false;

        public MainPage(LocalDbService dbService)
        {
            InitializeComponent();
            _dbService = dbService;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // الاشتراك في حدث تغير حالة الإنترنت (عشان المزامنة التلقائية تشتغل فوراً)
            Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

            SetupHeader();

            if (!_isGreetingShown)
            {
                _isGreetingShown = true;
                ShowGreetingAsync();
            }

            await LoadOfflineInvoiceCount();
            await LoadNotificationsAsync();

            // محاولة المزامنة الصامتة في الخلفية
            await AutoSyncSilently();
            await AutoSyncInventorySilently();

            // تحميل الإحصائيات (أونلاين أو أوفلاين حسب الحالة)
            await LoadDashboardStatsFromServer();

            UpdateConnectionStatusUI();
        }

        private async void ShowGreetingAsync()
        {
            frmGreeting.Opacity = 0;
            frmGreeting.IsVisible = true;

            await frmGreeting.FadeTo(1, 500);
            await Task.Delay(10000);
            await frmGreeting.FadeTo(0, 500);

            frmGreeting.IsVisible = false;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        }

        // =======================================================
        // دالة المراقبة: بتشتغل تلقائي أول ما النت يقطع أو يرجع
        // =======================================================
        private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                // النت رجع: ارفع الفواتير، نزل المخزن، وحدث الشاشة تلقائياً
                await AutoSyncSilently();
                await AutoSyncInventorySilently();

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await LoadOfflineInvoiceCount();
                    await LoadDashboardStatsFromServer();
                    await LoadNotificationsAsync();
                    UpdateConnectionStatusUI();
                });
            }
            else
            {
                // النت فصل: حدث حالة الاتصال بس، وكل حاجة هتكمل أوفلاين
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UpdateConnectionStatusUI();
                    _ = LoadDashboardStatsFromServer(); // إعادة الحسابات محلياً
                });
            }
        }

        private async Task AutoSyncSilently()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
            if (!BranchContext.IsConfigured) return;
            if (!SyncLock.TryEnter()) return;

            try
            {
                await SyncOfflineInvoicesAsync();
                await SyncOfflineOrdersAsync();
                await SyncOfflineCashTransactionsAsync();
            }
            catch { }
            finally
            {
                SyncLock.Exit();
            }
        }

        private async Task AutoSyncInventorySilently()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
            if (!BranchContext.IsConfigured) return;

            try
            {
                var oldProducts = await _dbService.GetProductsAsync();
                var oldByServerId = oldProducts
                    .Where(p => p.ServerId > 0)
                    .GroupBy(p => p.ServerId)
                    .ToDictionary(g => g.Key, g => g.First());

                string productsApiUrl = $"{ApiConfig.BaseUrl}/api/Products?locationId={BranchContext.LocationId}";
                var response = await ApiClient.Instance.GetAsync(productsApiUrl);

                if (!response.IsSuccessStatusCode) return;

                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var productsFromServer = await response.Content.ReadFromJsonAsync<List<ProductModel>>(options);

                if (productsFromServer == null || productsFromServer.Count == 0) return;

                TrackInventoryChanges(oldByServerId, productsFromServer);

                await _dbService.UpsertServerProductsAsync(productsFromServer);
            }
            catch { }
        }

        private void TrackInventoryChanges(Dictionary<int, ProductModel> oldProducts, List<ProductModel> newProducts)
        {
            var changedNames = new List<string>();
            int changedCount = 0;

            foreach (var newProduct in newProducts)
            {
                if (newProduct.ServerId <= 0) continue;

                if (oldProducts.TryGetValue(newProduct.ServerId, out var oldProduct))
                {
                    bool quantityChanged = Math.Abs(oldProduct.StockQuantity - newProduct.StockQuantity) > 0.001;
                    if (quantityChanged)
                    {
                        changedCount++;
                        if (changedNames.Count < 3)
                            changedNames.Add(newProduct.Name);
                    }
                }
                else
                {
                    changedCount++;
                    if (changedNames.Count < 3)
                        changedNames.Add(newProduct.Name + " (جديد)");
                }
            }

            if (changedCount > 0)
            {
                Preferences.Default.Set("LastInventorySyncTime", DateTime.Now.ToString("dd/MM hh:mm tt"));
                Preferences.Default.Set("LastInventoryChangedCount", changedCount);
                Preferences.Default.Set("LastInventoryChangedNames", string.Join("، ", changedNames));
            }
        }

        private void SetupHeader()
        {
            try
            {
                var culture = new CultureInfo("ar-EG");
                lblDate.Text = "تاريخ اليوم: " + DateTime.Now.ToString("dd MMMM yyyy", culture);
            }
            catch
            {
                lblDate.Text = "تاريخ اليوم: " + DateTime.Now.ToString("dd/MM/yyyy");
            }

            // عرض اسم المستخدم اللي سجل دخول + اسم الفرع
            string userName = Preferences.Default.Get("OfflineUser", "مستخدم");
            lblBranchName.Text = $"أهلاً بك يا {userName}، ({BranchContext.BranchName})";
        }

        private async Task LoadOfflineInvoiceCount()
        {
            try
            {
                int offlineInvoicesCount = await _dbService.GetOfflineInvoicesCountAsync();
                lblOfflineInvoices.Text = offlineInvoicesCount + " فاتورة";
            }
            catch { }
        }

        private async Task LoadDashboardStatsFromServer()
        {
            if (!BranchContext.IsConfigured) return;

            // 1. محاولة جلب الإحصائيات من السيرفر لو فيه إنترنت
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    string url = $"{ApiConfig.BaseUrl}/api/Dashboard?locationId={BranchContext.LocationId}";
                    var data = await ApiClient.Instance.GetFromJsonAsync<DashboardResponse>(url, options);

                    if (data != null)
                    {
                        lblSales.Text = data.TodaySales.ToString("N2") + " ج.م";
                        lblCash.Text = data.CurrentCash.ToString("N2") + " ج.م";
                        lblReturns.Text = data.Returns.ToString("N2") + " ج.م";
                        return; // لو نجحنا نخرج من الدالة
                    }
                }
                catch { }
            }

            // 2. السقوط الآمن للأوفلاين (أو لو السيرفر ماردش): حسابات محلية 100%
            try
            {
                var todayInvoices = await _dbService.GetTodayInvoicesAsync();
                decimal todaySales = todayInvoices.Sum(i => (decimal)i.NetAmount);

                decimal cashSales = todayInvoices
                    .Where(i => i.PaymentMethod == "نقدي" || i.PaymentMethod == "كاش")
                    .Sum(i => (decimal)i.PaidAmount);

                decimal localReturns = todayInvoices.Where(i => i.IsReturn).Sum(i => (decimal)i.PaidAmount);

                var todayTrans = await _dbService.GetTodayCashTransactionsAsync();
                decimal totalExpensesLocal = todayTrans
                    .Where(t => (t.TransactionType == "مصروفات محل" || t.TransactionType == "صادر")
                             && !t.Description.Contains("انستا")
                             && !t.Description.Contains("فودافون كاش"))
                    .Sum(t => (decimal)t.Amount);

                decimal currentCash = cashSales - totalExpensesLocal - localReturns;

                lblSales.Text = todaySales.ToString("N2") + " ج.م";
                lblCash.Text = currentCash.ToString("N2") + " ج.م";
                lblReturns.Text = localReturns.ToString("N2") + " ج.م";
            }
            catch { }
        }

        private void UpdateConnectionStatusUI()
        {
            bool isOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

            if (isOnline)
            {
                lblConnectionStatus.Text = "متصل 🟢";
                lblConnectionStatus.TextColor = Colors.White;
                frmConnectionStatus.BackgroundColor = Color.FromArgb("#2ECC71");
            }
            else
            {
                lblConnectionStatus.Text = "أوفلاين 🔴";
                lblConnectionStatus.TextColor = Colors.White;
                frmConnectionStatus.BackgroundColor = Color.FromArgb("#E74C3C");
            }
        }

        private async Task LoadNotificationsAsync()
        {
            var notifications = new ObservableCollection<NotificationModel>();
            bool loadedFromServer = false;

            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet && BranchContext.IsConfigured)
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    string url = $"{ApiConfig.BaseUrl}/api/Notifications?locationId={BranchContext.LocationId}";
                    var serverNotifications = await ApiClient.Instance.GetFromJsonAsync<List<NotificationModel>>(url, options);

                    if (serverNotifications != null)
                    {
                        foreach (var n in serverNotifications)
                        {
                            if (n.Title == "رصيد منخفض") n.Title = "نواقص الفرع ⚠️";
                            notifications.Add(n);
                        }
                        loadedFromServer = true;
                    }
                }
                catch { }
            }

            if (!loadedFromServer)
            {
                try
                {
                    var products = await _dbService.GetProductsAsync();

                    var lowStock = products
                        .Where(p =>
                            p.StockQuantity <= 0 ||
                            (p.AlertLimit > 0 && p.StockQuantity <= p.AlertLimit) ||
                            (p.AlertLimit <= 0 && p.StockQuantity <= 5)
                        )
                        .OrderBy(p => p.StockQuantity)
                        .ToList();

                    foreach (var p in lowStock)
                    {
                        string status = p.StockQuantity <= 0 ? "نفذ من المخزن بالكامل" : "أوشك على النفاذ";
                        string alertText = p.AlertLimit > 0 ? p.AlertLimit.ToString("0.##") : "غير محدد";

                        notifications.Add(new NotificationModel
                        {
                            Title = "نواقص الفرع ⚠️ (أوفلاين)",
                            Message = $"الصنف '{p.Name}' {status}\nالمتاح حالياً: {p.StockQuantity:0.##} (الحد الأدنى: {alertText})",
                            TimeAgo = string.Empty,
                            ColorCode = "#E74C3C"
                        });
                    }
                }
                catch { }
            }

            try
            {
                int offlineCount = await _dbService.GetOfflineInvoicesCountAsync();
                if (offlineCount > 0)
                {
                    notifications.Add(new NotificationModel
                    {
                        Title = "فواتير معلقة ⏳",
                        Message = $"يوجد {offlineCount} فاتورة تحتاج للمزامنة مع السيرفر.",
                        TimeAgo = string.Empty,
                        ColorCode = "#F1C40F"
                    });
                }
            }
            catch { }

            string lastInvSyncTime = Preferences.Default.Get("LastInventorySyncTime", string.Empty);
            if (!string.IsNullOrEmpty(lastInvSyncTime))
            {
                int changedCount = Preferences.Default.Get("LastInventoryChangedCount", 0);
                string changedNames = Preferences.Default.Get("LastInventoryChangedNames", string.Empty);

                if (changedCount > 0)
                {
                    string msg = $"تم تحديث أرصدة {changedCount} صنف";
                    if (!string.IsNullOrEmpty(changedNames))
                        msg += $" (منها: {changedNames})";

                    notifications.Add(new NotificationModel
                    {
                        Title = "تحديث المخزون 📦",
                        Message = msg,
                        TimeAgo = lastInvSyncTime,
                        ColorCode = "#3498DB"
                    });
                }
            }

            cvNotifications.ItemsSource = notifications;
        }

        private async void OnReloadClicked(object sender, EventArgs e)
        {
            var btn = sender as View;
            if (btn != null) btn.IsEnabled = false;

            await LoadOfflineInvoiceCount();
            await LoadDashboardStatsFromServer();
            await LoadNotificationsAsync();
            UpdateConnectionStatusUI();

            if (btn != null) btn.IsEnabled = true;
        }

        private async void OnNewSaleClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new SalesPage(_dbService));
        }

        private async void OnSyncClicked(object sender, EventArgs e)
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                UpdateConnectionStatusUI();
                await DisplayAlert("لا يوجد اتصال", "يجب الاتصال بالإنترنت لإتمام المزامنة.", "موافق");
                return;
            }

            if (!SyncLock.TryEnter())
            {
                await DisplayAlert("تنبيه", "جارِ المزامنة في الخلفية، يرجى الانتظار...", "موافق");
                return;
            }

            try
            {
                btnSync.IsEnabled = false;
                btnSync.Text = "جاري الرفع... ⏳";

                await SyncOfflineInvoicesAsync();
                await SyncOfflineOrdersAsync();
                await SyncOfflineCashTransactionsAsync();
                await AutoSyncInventorySilently();

                await LoadOfflineInvoiceCount();
                await LoadDashboardStatsFromServer();
                await LoadNotificationsAsync();

                await DisplayAlert("نجاح", "تمت مزامنة البيانات مع السيرفر بنجاح ✅", "موافق");
            }
            catch (Exception)
            {
                await DisplayAlert("تنبيه", "حدث خطأ أثناء المزامنة، تأكد من جودة الاتصال بالسيرفر.", "موافق");
            }
            finally
            {
                SyncLock.Exit();
                btnSync.IsEnabled = true;
                btnSync.Text = "رفع البيانات للسيرفر 🔄";
            }
        }

        private async Task<(int synced, int skipped)> SyncOfflineInvoicesAsync()
        {
            var pending = await _dbService.GetPendingInvoicesAsync();
            if (pending.Count == 0) return (0, 0);

            var allProducts = await _dbService.GetProductsAsync();
            int syncedCount = 0;
            int skippedCount = 0;

            foreach (var invoice in pending)
            {
                var details = await _dbService.GetInvoiceDetailsAsync(invoice.Id);
                var detailDtos = new List<SaleDetailDto>();
                bool hasUnsyncedProduct = false;

                foreach (var d in details)
                {
                    var product = allProducts.FirstOrDefault(p => p.Id == d.ProductId);
                    if (product == null || product.ServerId <= 0)
                    {
                        hasUnsyncedProduct = true;
                        break;
                    }

                    detailDtos.Add(new SaleDetailDto
                    {
                        ProductId = product.ServerId,
                        Quantity = (int)Math.Round(d.Quantity),
                        UnitPrice = d.UnitPrice,
                        TotalPrice = d.Total
                    });
                }

                if (hasUnsyncedProduct)
                {
                    skippedCount++;
                    continue;
                }

                var dto = new SaleInvoiceDto
                {
                    InvoiceNumber = invoice.InvoiceNumber,
                    CustomerId = invoice.CustomerId,
                    LocationId = invoice.LocationId,
                    InvoiceDate = invoice.InvoiceDate,
                    TotalAmount = invoice.TotalAmount,
                    Discount = invoice.Discount,
                    NetAmount = invoice.NetAmount,
                    PaidAmount = invoice.PaidAmount,
                    RemainingAmount = invoice.RemainingAmount,
                    PaymentMethod = invoice.PaymentMethod,
                    Details = detailDtos
                };

                try
                {
                    var response = await ApiClient.Instance.PostAsJsonAsync($"{ApiConfig.BaseUrl}/api/Sales", dto);
                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<SaleSyncResponse>();
                        await _dbService.MarkInvoiceAsSyncedAsync(invoice.Id, result?.ServerId ?? 0);
                        syncedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
                catch
                {
                    skippedCount++;
                }
            }
            return (syncedCount, skippedCount);
        }

        private async Task<int> SyncOfflineOrdersAsync()
        {
            var pendingOrders = await _dbService.GetPendingOrdersAsync();
            if (pendingOrders.Count == 0) return 0;

            int syncedCount = 0;

            foreach (var order in pendingOrders)
            {
                var details = await _dbService.GetOrderDetailsAsync(order.Id);

                var requestDto = new
                {
                    LocationId = BranchContext.LocationId,
                    RequestDate = order.OrderDate,
                    Status = order.Status,
                    Details = details.Select(d => new
                    {
                        ProductId = d.ProductId,
                        RequestedQuantity = (int)Math.Round(d.RequestedQuantity)
                    }).ToList()
                };

                try
                {
                    var response = await ApiClient.Instance.PostAsJsonAsync($"{ApiConfig.BaseUrl}/api/Requests", requestDto);

                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<SaleSyncResponse>();
                        await _dbService.MarkOrderAsSyncedAsync(order.Id, result?.ServerId ?? 0);
                        syncedCount++;
                    }
                }
                catch { }
            }
            return syncedCount;
        }

        private async Task<int> SyncOfflineCashTransactionsAsync()
        {
            var pending = await _dbService.GetPendingCashTransactionsAsync();
            if (pending.Count == 0) return 0;

            var dtos = pending.Select(t => new
            {
                LocationId = BranchContext.LocationId,
                TransactionDate = t.TransactionDate,
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                Description = t.Description
            }).ToList();

            try
            {
                var response = await ApiClient.Instance.PostAsJsonAsync($"{ApiConfig.BaseUrl}/api/Treasury/Transactions", dtos);
                if (response.IsSuccessStatusCode)
                {
                    foreach (var p in pending)
                    {
                        await _dbService.MarkCashTransactionAsSyncedAsync(p.Id);
                    }
                    return pending.Count;
                }
            }
            catch { }

            return 0;
        }

        public class DashboardResponse
        {
            public decimal TodaySales { get; set; }
            public decimal CurrentCash { get; set; }
            public decimal Returns { get; set; }
        }
    }
}