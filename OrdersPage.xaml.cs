using MajesticMobile.Data;
using MajesticMobile.Models;
using MajesticMobile.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using System;
using Microsoft.Maui.Graphics;

namespace MajesticMobile
{
    public partial class OrdersPage : ContentPage
    {
        private readonly LocalDbService _dbService;

        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { return true; }
            })
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            client.DefaultRequestHeaders.Add("X-Tunnel-Skip-AntiPhishing-Page", "true");
            return client;
        }

        private List<ProductModel> _allProducts = new();
        private ObservableCollection<OrderDetailModel> _cartItems = new();

        private ProductModel? _selectedProduct;

        private const string StatusDelivered = "تم الاستلام (Delivered)";

        public OrdersPage(LocalDbService dbService)
        {
            InitializeComponent();
            _dbService = dbService;
            cvCartItems.ItemsSource = _cartItems;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (!BranchContext.IsConfigured)
            {
                await DisplayAlert("تنبيه", "لم يتم تحديد الفرع على هذا الجهاز بعد.", "موافق");
                await Navigation.PopAsync();
                return;
            }

            _allProducts = await _dbService.GetProductsAsync();

            await SyncPendingDeliveryConfirmationsAsync();
            await LoadOrdersAsync();
        }

        private async void OnRefreshOrders(object sender, EventArgs e)
        {
            await SyncPendingDeliveryConfirmationsAsync();
            await LoadOrdersAsync();

            refreshActiveOrders.IsRefreshing = false;
            refreshHistoryOrders.IsRefreshing = false;
        }

        private async void OnTabClicked(object? sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            btnTabNewOrder.BackgroundColor = Color.FromArgb("#162130");
            btnTabNewOrder.TextColor = Color.FromArgb("#BDC3C7");

            btnTabActive.BackgroundColor = Color.FromArgb("#162130");
            btnTabActive.TextColor = Color.FromArgb("#BDC3C7");

            btnTabHistory.BackgroundColor = Color.FromArgb("#162130");
            btnTabHistory.TextColor = Color.FromArgb("#BDC3C7");

            btn.BackgroundColor = Color.FromArgb("#3498DB");
            btn.TextColor = Colors.White;

            viewNewOrder.IsVisible = false;
            viewActiveOrders.IsVisible = false;
            viewHistory.IsVisible = false;

            string tab = btn.CommandParameter?.ToString() ?? "";

            if (tab == "New") viewNewOrder.IsVisible = true;
            else if (tab == "Active")
            {
                viewActiveOrders.IsVisible = true;
                await LoadOrdersAsync();
            }
            else if (tab == "History")
            {
                viewHistory.IsVisible = true;
                await LoadOrdersAsync();
            }
        }

        // ==========================================
        //  إدارة الكومبوبوكس (البحث عن منتج)
        // ==========================================
        private void OnToggleProductsList_Clicked(object? sender, EventArgs e)
        {
            if (searchProduct.IsFocused) searchProduct.Unfocus();

            if (frameProducts.IsVisible)
            {
                frameProducts.IsVisible = false;
            }
            else
            {
                if (!_allProducts.Any())
                {
                    DisplayAlert("تنبيه", "لا توجد منتجات متاحة في المخزن حالياً.", "موافق");
                    return;
                }
                cvProductsList.ItemsSource = _allProducts;
                frameProducts.IsVisible = true;
            }
        }

        private void OnProductSearchFocused(object? sender, FocusEventArgs e)
        {
            var keyword = searchProduct.Text?.ToLower() ?? string.Empty;
            var list = string.IsNullOrWhiteSpace(keyword)
                ? _allProducts
                : _allProducts.Where(p => p.Name.ToLower().Contains(keyword)).ToList();

            cvProductsList.ItemsSource = list;
            frameProducts.IsVisible = list.Any();
        }

        private void OnProductSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            _selectedProduct = null;
            var keyword = e.NewTextValue?.ToLower() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                cvProductsList.ItemsSource = _allProducts;
                frameProducts.IsVisible = _allProducts.Any();
                return;
            }

            var filtered = _allProducts.Where(p => p.Name.ToLower().Contains(keyword)).ToList();
            cvProductsList.ItemsSource = filtered;
            frameProducts.IsVisible = filtered.Any();
        }

        private void OnProductSelectedFromSearch(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is ProductModel product)
            {
                _selectedProduct = product;
                searchProduct.Text = product.Name;
                frameProducts.IsVisible = false;
                cvProductsList.SelectedItem = null;
                searchProduct.Unfocus();
            }
        }

        // ==========================================
        //  إدارة سلة الطلبيات
        // ==========================================
        private void OnAddToCartClicked(object? sender, EventArgs e)
        {
            if (_selectedProduct == null)
            {
                DisplayAlert("تنبيه", "برجاء اختيار الصنف أولاً من القائمة.", "موافق");
                return;
            }

            if (!int.TryParse(entryQuantity.Text, out int qty) || qty <= 0)
            {
                DisplayAlert("تنبيه", "برجاء إدخال كمية صحيحة (أرقام صحيحة فقط).", "موافق");
                return;
            }

            var existingItem = _cartItems.FirstOrDefault(i => i.ProductId == (_selectedProduct.ServerId > 0 ? _selectedProduct.ServerId : _selectedProduct.Id));
            if (existingItem != null)
            {
                existingItem.RequestedQuantity += qty;
                cvCartItems.ItemsSource = null;
                cvCartItems.ItemsSource = _cartItems;
            }
            else
            {
                _cartItems.Add(new OrderDetailModel
                {
                    ProductId = _selectedProduct.ServerId > 0 ? _selectedProduct.ServerId : _selectedProduct.Id,
                    ProductName = _selectedProduct.Name ?? "بدون اسم",
                    RequestedQuantity = qty
                });
            }

            _selectedProduct = null;
            searchProduct.Text = "";
            entryQuantity.Text = "";
        }

        private void OnRemoveCartItemClicked(object? sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn?.BindingContext is OrderDetailModel item)
            {
                _cartItems.Remove(item);
            }
        }

        private async void OnSubmitOrderClicked(object? sender, EventArgs e)
        {
            if (_cartItems.Count == 0)
            {
                await DisplayAlert("تنبيه", "الطلبية فارغة، يرجى إضافة أصناف أولاً.", "موافق");
                return;
            }

            bool confirm = await DisplayAlert("تأكيد", "هل أنت متأكد من إرسال هذه الطلبية للمخزن الرئيسي؟", "نعم", "إلغاء");
            if (!confirm) return;

            var newOrder = new OrderModel
            {
                OrderDate = DateTime.Now,
                Status = "قيد الانتظار",
                IsSynced = false
            };

            await _dbService.SaveOrderAsync(newOrder, _cartItems.ToList());

            _cartItems.Clear();

            // محاولة رفع الطلبية فوراً لو فيه إنترنت
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                await SyncPendingOrdersAsync();
            }

            await DisplayAlert("نجاح", "تم حفظ الطلبية وإرسالها للمخزن بنجاح.", "موافق");
            OnTabClicked(btnTabActive, EventArgs.Empty);
        }

        // ==========================================
        //  مزامنة وتحميل الطلبيات
        // ==========================================
        private async Task SyncPendingOrdersAsync()
        {
            try
            {
                var pendingOrders = await _dbService.GetPendingOrdersAsync();
                foreach (var order in pendingOrders)
                {
                    var details = await _dbService.GetOrderDetailsAsync(order.Id);

                    var dto = new NewRequestDto
                    {
                        LocationId = BranchContext.LocationId,
                        RequestDate = order.OrderDate,
                        Status = order.Status,
                        Details = details.Select(d => new NewRequestDetailDto
                        {
                            ProductId = d.ProductId,
                            RequestedQuantity = (int)d.RequestedQuantity
                        }).ToList()
                    };

                    var response = await _httpClient.PostAsJsonAsync($"{ApiConfig.BaseUrl}/api/Requests", dto);
                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<dynamic>();
                        int serverId = result?.GetProperty("ServerId").GetInt32() ?? 0;
                        if (serverId > 0)
                        {
                            await _dbService.MarkOrderAsSyncedAsync(order.Id, serverId);
                        }
                    }
                }
            }
            catch { /* فشل صامت */ }
        }

        private async Task LoadOrdersAsync()
        {
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet && BranchContext.IsConfigured)
            {
                try
                {
                    string url = $"{ApiConfig.BaseUrl}/api/Requests/Status?locationId={BranchContext.LocationId}";

                    var serverStatuses = await _httpClient.GetFromJsonAsync<List<OrderStatusDto>>(url);
                    if (serverStatuses != null && serverStatuses.Count > 0)
                    {
                        var localOrders = await _dbService.GetOrdersAsync();

                        foreach (var serverOrder in serverStatuses)
                        {
                            var local = localOrders.FirstOrDefault(o => o.ServerId == serverOrder.ServerId);
                            if (local == null) continue;

                            if (local.PendingDeliverySync) continue;

                            if (serverOrder.Status.Contains("استلام"))
                            {
                                local.Status = StatusDelivered;
                            }
                            else if (serverOrder.Status.Contains("تم الصرف") || serverOrder.Status.Contains("تم"))
                            {
                                local.Status = "في الطريق (Shipped)";
                            }
                            else if (serverOrder.Status.Contains("Rejected") || serverOrder.Status.Contains("رفض"))
                            {
                                local.Status = "مرفوض (Rejected)";
                            }
                            else
                            {
                                local.Status = serverOrder.Status;
                            }

                            local.WarehouseMessage = serverOrder.Message ?? "";
                            await _dbService.UpdateOrderAsync(local);
                        }
                    }
                }
                catch { }
            }

            var allOrders = await _dbService.GetOrdersAsync();

            var displayOrders = allOrders.Select(o => new OrderDisplayViewModel
            {
                Order = o,
                HasMessage = !string.IsNullOrWhiteSpace(o.WarehouseMessage),
                IsShipped = o.Status == "في الطريق (Shipped)"
            }).ToList();

            var activeOrders = displayOrders.Where(o => o.Order.Status.Contains("Pending") || o.Order.Status.Contains("انتظار") || o.Order.Status.Contains("قيد") || o.Order.Status.Contains("Shipped") || o.Order.Status.Contains("الطريق")).OrderByDescending(o => o.OrderDate).ToList();
            cvActiveOrders.ItemsSource = activeOrders;

            var historyOrders = displayOrders.Where(o => o.Order.Status.Contains("Delivered") || o.Order.Status.Contains("استلام") || o.Order.Status.Contains("Rejected") || o.Order.Status.Contains("مرفوض")).OrderByDescending(o => o.OrderDate).ToList();
            cvHistoryOrders.ItemsSource = historyOrders;
        }

        private async void OnReceiveOrderClicked(object? sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var vm = btn.BindingContext as OrderDisplayViewModel;

            if (vm == null)
            {
                await DisplayAlert("عذراً", "حدث خطأ في قراءة بيانات الطلبية، برجاء إعادة فتح الشاشة.", "موافق");
                return;
            }

            bool confirm = await DisplayAlert("تأكيد استلام", "هل أنت متأكد من تأكيد استلام الطلبية ونقلها للأرشيف؟\n(ملاحظة: الأرصدة زادت بالفعل في الموبايل من السيرفر)", "نعم استلمت", "إلغاء");
            if (!confirm) return;

            vm.Order.Status = StatusDelivered;
            vm.Order.PendingDeliverySync = true;
            await _dbService.UpdateOrderAsync(vm.Order);

            await DisplayAlert("نجاح", "تم تأكيد الاستلام محليًا ونقل الطلبية للأرشيف بنجاح!", "موافق");

            await SyncPendingDeliveryConfirmationsAsync();
            await LoadOrdersAsync();
        }

        private async Task SyncPendingDeliveryConfirmationsAsync()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;

            List<OrderModel> pending;
            try
            {
                pending = await _dbService.GetPendingDeliveryConfirmationsAsync();
            }
            catch
            {
                return;
            }

            foreach (var order in pending.Where(o => o.ServerId > 0))
            {
                try
                {
                    string url = $"{ApiConfig.BaseUrl}/api/Requests/{order.ServerId}/Confirm";
                    var response = await _httpClient.PutAsync(url, null);

                    if (response.IsSuccessStatusCode)
                    {
                        order.PendingDeliverySync = false;
                        await _dbService.UpdateOrderAsync(order);
                    }
                }
                catch { }
            }
        }
    }

    // ==========================================
    // الكلاسات المضافة لحل الخطأ الخاص بنقل الداتا للسيرفر
    // ==========================================
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

    public class OrderStatusDto
    {
        public int ServerId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class OrderDisplayViewModel
    {
        public OrderModel Order { get; set; } = new();
        public bool HasMessage { get; set; }
        public bool IsShipped { get; set; }

        public string Status => Order.Status;
        public DateTime OrderDate => Order.OrderDate;
        public string WarehouseMessage => Order.WarehouseMessage;

        public Color StatusColor
        {
            get
            {
                if (Status.Contains("استلام") || Status.Contains("Delivered"))
                    return Color.FromArgb("#2ECC71");
                if (Status.Contains("رفض") || Status.Contains("Rejected"))
                    return Color.FromArgb("#E74C3C");
                return Color.FromArgb("#BDC3C7");
            }
        }
    }
}