using MajesticMobile.Data;
using MajesticMobile.Models;
using MajesticMobile.Services;
using System.Net.Http.Json;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MajesticMobile
{
    public partial class InventoryPage : ContentPage
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
                Timeout = TimeSpan.FromSeconds(20)
            };

            client.DefaultRequestHeaders.Add("X-Tunnel-Skip-AntiPhishing-Page", "true");
            return client;
        }

        private List<ProductModel> _allProducts = new List<ProductModel>();
        private List<CategoryModel> _allCategories = new List<CategoryModel>();
        private int _selectedCategoryId = -99;

        private int _branchCategoryId = 0;
        private const string BranchCategoryName = "منتجات الفرع";

        public InventoryPage(LocalDbService dbService)
        {
            InitializeComponent();
            _dbService = dbService;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadDataFromLocal();

            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                _ = SyncFromServer(false);
            }
        }

        private async Task LoadDataFromLocal()
        {
            try
            {
                _allProducts = await _dbService.GetProductsAsync();
                _allCategories = await _dbService.GetCategoriesAsync();

                var branchCat = _allCategories.FirstOrDefault(c => c.Name == BranchCategoryName);
                _branchCategoryId = branchCat?.Id ?? 0;

                BindCategoriesUI();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ في قاعدة البيانات", "مشكلة في قراءة البيانات المحلية:\n" + ex.Message, "موافق");
            }
        }

        private void BindCategoriesUI()
        {
            var uiCategories = new ObservableCollection<CategoryModel>();

            uiCategories.Add(new CategoryModel { Id = -1, Name = "الكل", BgColor = Color.FromArgb("#34495E") });
            uiCategories.Add(new CategoryModel { Id = -2, Name = "⚠️ النواقص", BgColor = Color.FromArgb("#C0392B") });

            foreach (var cat in _allCategories)
            {
                uiCategories.Add(new CategoryModel { Id = cat.Id, Name = cat.Name, BgColor = Color.FromArgb("#34495E") });
            }

            bool branchCategoryExistsOnServer = _allCategories.Any(c => c.Name == BranchCategoryName);
            if (!branchCategoryExistsOnServer)
            {
                uiCategories.Add(new CategoryModel { Id = _branchCategoryId, Name = BranchCategoryName, BgColor = Color.FromArgb("#34495E") });
            }

            if (_selectedCategoryId != -99)
            {
                var selectedCat = uiCategories.FirstOrDefault(c => c.Id == _selectedCategoryId);
                if (selectedCat != null)
                {
                    selectedCat.BgColor = selectedCat.Id == -2 ? Color.FromArgb("#E74C3C") : Color.FromArgb("#2980B9");
                }
            }

            cvCategories.ItemsSource = null;
            cvCategories.ItemsSource = uiCategories;
        }

        private void OnCategoryClicked(object? sender, EventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            _selectedCategoryId = (int)button.CommandParameter;

            var uiCategories = cvCategories.ItemsSource as ObservableCollection<CategoryModel>;
            if (uiCategories != null)
            {
                foreach (var cat in uiCategories)
                {
                    if (cat.Id == _selectedCategoryId)
                        cat.BgColor = cat.Id == -2 ? Color.FromArgb("#E74C3C") : Color.FromArgb("#2980B9");
                    else
                        cat.BgColor = cat.Id == -2 ? Color.FromArgb("#C0392B") : Color.FromArgb("#34495E");
                }
                cvCategories.ItemsSource = null;
                cvCategories.ItemsSource = uiCategories;
            }

            ApplyFilters();
        }

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            string? searchText = searchBar.Text?.ToLower();
            var filtered = _allProducts.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filtered = filtered.Where(p => p.Name.ToLower().Contains(searchText));
            }

            if (_selectedCategoryId == -2)
            {
                filtered = filtered.Where(p =>
                    p.StockQuantity <= 0 ||
                    (p.AlertLimit > 0 && p.StockQuantity <= p.AlertLimit) ||
                    (p.AlertLimit <= 0 && p.StockQuantity <= 5));
            }
            else if (_selectedCategoryId != -99 && _selectedCategoryId != -1)
            {
                filtered = filtered.Where(p => p.CategoryId == _selectedCategoryId);
            }

            cvProducts.ItemsSource = null;
            cvProducts.ItemsSource = filtered.ToList();
        }

        private async void OnAddBranchProductClicked(object? sender, EventArgs e)
        {
            // إضافة علامة ? لتجنب إيرور الـ Nullable
            string? name = await DisplayPromptAsync("منتج جديد", "أدخل اسم المنتج الخاص بالفرع:", "التالي", "إلغاء");
            if (string.IsNullOrWhiteSpace(name)) return;

            string? qtyStr = await DisplayPromptAsync("الكمية", "أدخل الكمية المتاحة بالفرع:", "التالي", "إلغاء", keyboard: Keyboard.Numeric);
            if (string.IsNullOrWhiteSpace(qtyStr) || !double.TryParse(qtyStr, out double qty)) return;

            string? priceStr = await DisplayPromptAsync("سعر الشراء", "أدخل تكلفة شراء المنتج للفرع (مخفية للرجوع لها فقط):", "حفظ", "إلغاء", keyboard: Keyboard.Numeric);
            if (string.IsNullOrWhiteSpace(priceStr) || !decimal.TryParse(priceStr, out decimal purchasePrice)) return;

            var newProduct = new ProductModel
            {
                ServerId = 0,
                Name = name,
                StockQuantity = qty,
                PurchasePrice = purchasePrice,
                CategoryId = _branchCategoryId
            };

            await _dbService.SaveProductAsync(newProduct);

            _selectedCategoryId = _branchCategoryId;
            await LoadDataFromLocal();
        }

        private async void OnSyncFromServerClicked(object? sender, EventArgs e)
        {
            await SyncFromServer(true);
        }

        private async Task SyncFromServer(bool showAlerts)
        {
            if (!BranchContext.IsConfigured)
            {
                if (showAlerts) await DisplayAlert("تنبيه", "لم يتم تحديد الفرع على هذا الجهاز بعد.", "موافق");
                return;
            }

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                if (showAlerts) await DisplayAlert("لا يوجد اتصال", "يجب الاتصال بالإنترنت لتحديث الأرصدة من السيرفر.", "موافق");
                return;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                btnSync.IsEnabled = false;
                btnSync.Text = "جاري التحديث... ⏳";
            });

            var oldProductsByServerId = _allProducts
                .Where(p => p.ServerId > 0)
                .GroupBy(p => p.ServerId)
                .ToDictionary(g => g.Key, g => g.First());

            try
            {
                await PushLocalProductsToServer();

                string baseUrl = ApiConfig.BaseUrl.TrimEnd('/');
                string categoriesApiUrl = $"{baseUrl}/api/Categories";
                string productsApiUrl = $"{baseUrl}/api/Products?locationId={BranchContext.LocationId}";

                var catResponse = await _httpClient.GetAsync(categoriesApiUrl);
                var prodResponse = await _httpClient.GetAsync(productsApiUrl);

                if (catResponse.IsSuccessStatusCode && prodResponse.IsSuccessStatusCode)
                {
                    var categoriesFromServer = await catResponse.Content.ReadFromJsonAsync<List<CategoryModel>>();
                    if (categoriesFromServer != null)
                    {
                        await _dbService.DeleteAllCategoriesAsync();
                        foreach (var cat in categoriesFromServer) await _dbService.SaveCategoryAsync(cat);
                    }

                    var productsFromServer = await prodResponse.Content.ReadFromJsonAsync<List<ProductModel>>();
                    if (productsFromServer != null)
                    {
                        TrackInventoryChanges(oldProductsByServerId, productsFromServer);
                        await _dbService.DeleteServerProductsAsync();
                        foreach (var product in productsFromServer) await _dbService.SaveProductAsync(product);
                    }

                    _selectedCategoryId = -99;
                    await LoadDataFromLocal();

                    if (showAlerts)
                        await DisplayAlert("نجاح", "تم تحديث الأقسام والأرصدة من السيرفر بنجاح!", "موافق");
                }
                else if (showAlerts)
                {
                    string errorMsg = $"تعذر جلب البيانات بشكل سليم.\n\nكود الأقسام: {(int)catResponse.StatusCode} ({catResponse.ReasonPhrase})\nكود المنتجات: {(int)prodResponse.StatusCode} ({prodResponse.ReasonPhrase})";
                    await DisplayAlert("رفض من السيرفر 🔴", errorMsg, "موافق");
                }
            }
            catch (TaskCanceledException)
            {
                if (showAlerts)
                    await DisplayAlert("انقطاع الاتصال ⏱️", "انتهى وقت الانتظار (Timeout). السيرفر لا يستجيب أو الإنترنت ضعيف جداً.", "موافق");
            }
            catch (HttpRequestException httpEx)
            {
                if (showAlerts)
                    await DisplayAlert("السيرفر مغلق 🔴", $"لم نتمكن من الوصول للسيرفر. تأكد أن الـ API يعمل بشكل صحيح.\n\nالتفاصيل الفنية:\n{httpEx.Message}", "موافق");
            }
            catch (Exception ex)
            {
                if (showAlerts)
                    await DisplayAlert("خطأ غير متوقع ⚠️", $"حدث خطأ منع التحديث:\n\n{ex.Message}", "موافق");
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    btnSync.IsEnabled = true;
                    btnSync.Text = "تحديث الأرصدة من السيرفر 🔄";
                });
            }
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
                        if (changedNames.Count < 3) changedNames.Add(newProduct.Name);
                    }
                }
                else
                {
                    changedCount++;
                    if (changedNames.Count < 3) changedNames.Add(newProduct.Name + " (جديد)");
                }
            }

            if (changedCount > 0)
            {
                Preferences.Default.Set("LastInventorySyncTime", DateTime.Now.ToString("dd/MM hh:mm tt"));
                Preferences.Default.Set("LastInventoryChangedCount", changedCount);
                Preferences.Default.Set("LastInventoryChangedNames", string.Join("، ", changedNames));
            }
        }

        private async Task PushLocalProductsToServer()
        {
            var localOnlyProducts = _allProducts.Where(p => p.ServerId == 0).ToList();
            if (localOnlyProducts.Count == 0) return;

            string baseUrl = ApiConfig.BaseUrl.TrimEnd('/');
            string url = $"{baseUrl}/api/Products";

            foreach (var product in localOnlyProducts)
            {
                try
                {
                    var request = new NewBranchProductRequest
                    {
                        Name = product.Name,
                        StockQuantity = product.StockQuantity,
                        PurchasePrice = product.PurchasePrice,
                        LocationId = BranchContext.LocationId
                    };

                    var response = await _httpClient.PostAsJsonAsync(url, request);

                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<AddProductResponse>();
                        if (result != null && result.ServerId > 0)
                        {
                            product.ServerId = result.ServerId;
                            await _dbService.SaveProductAsync(product);
                        }
                    }
                }
                catch { continue; }
            }
        }
    }

    public class NewBranchProductRequest
    {
        public string Name { get; set; } = string.Empty;
        public double StockQuantity { get; set; }
        public decimal PurchasePrice { get; set; }
        public int LocationId { get; set; }
    }

    public class AddProductResponse
    {
        public int ServerId { get; set; }
    }
}