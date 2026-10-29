using MajesticMobile.Data;
using MajesticMobile.Models;
using MajesticMobile.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Networking;

namespace MajesticMobile
{
    public partial class SalesPage : ContentPage
    {
        private readonly LocalDbService _dbService;

        private List<ProductModel> _availableProducts = new();
        private List<CustomerModel> _branchCustomers = new();

        private List<string> _paymentMethods = new() { "نقدي", "انستا باي الكردي", "انستا باي الحكش" };

        private ObservableCollection<InvoiceDetailModel> _invoiceItems = new();
        private InvoiceDetailModel? _selectedItem;
        private InvoiceDetailModel? _editingItem;

        private ProductModel? _selectedProductForAdd;
        private int? _selectedCustomerId = null;

        private bool _isSelectingCustomer = false;
        private bool _isSelectingProduct = false;
        private bool _isInvoiceSaved = false; // التحكم في حالة الإدخال

        public SalesPage(LocalDbService dbService)
        {
            InitializeComponent();
            _dbService = dbService;
            cvInvoiceItems.ItemsSource = _invoiceItems;
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

            lblBranchName.Text = BranchContext.BranchName;
            dpInvoiceDate.Date = DateTime.Now;
            lblInvoiceNumber.Text = await _dbService.GenerateInvoiceNumberAsync();

            await LoadLocalData();
            _ = UpdateDataFromServerSilently();
            ResetInvoiceItems();
            SetInvoiceState(false); // إعادة تعيين الشاشة للحالة الافتراضية
        }

        private async Task LoadLocalData()
        {
            var allProducts = await _dbService.GetProductsAsync();
            _availableProducts = allProducts.Where(p => p.StockQuantity > 0).ToList();
            _branchCustomers = await _dbService.GetCustomersAsync();

            pickerPaymentMethod.ItemsSource = _paymentMethods;
            pickerPaymentMethod.SelectedIndex = -1;
        }

        private async Task UpdateDataFromServerSilently()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;

            try
            {
                string baseUrl = ApiConfig.BaseUrl.TrimEnd('/');

                var custResponse = await ApiClient.Instance.GetAsync($"{baseUrl}/api/Customers?locationId={BranchContext.LocationId}");
                if (custResponse.IsSuccessStatusCode)
                {
                    var customersFromServer = await custResponse.Content.ReadFromJsonAsync<List<CustomerModel>>();
                    if (customersFromServer != null)
                    {
                        await _dbService.DeleteAllCustomersAsync();
                        foreach (var c in customersFromServer) await _dbService.SaveCustomerAsync(c);
                        _branchCustomers = await _dbService.GetCustomersAsync();
                    }
                }
            }
            catch { }
        }

        private void OnNumberFieldFocused(object? sender, FocusEventArgs e)
        {
            if (sender is Entry entry)
            {
                if (entry.Text == "0" || entry.Text == "0.00" || entry.Text == "1")
                {
                    entry.Text = string.Empty;
                }
            }
        }

        // التحكم في حالة الإدخال بالكامل (State Machine)
        private void SetInvoiceState(bool isSaved)
        {
            _isInvoiceSaved = isSaved;

            searchCustomer.IsEnabled = !isSaved;
            searchProduct.IsEnabled = !isSaved;
            entryQuantity.IsEnabled = !isSaved;
            entryUnitPrice.IsEnabled = !isSaved;
            btnAddItem.IsEnabled = !isSaved;
            btnRemoveItem.IsEnabled = !isSaved;
            entryDiscount.IsEnabled = !isSaved;
            entryPaidAmount.IsEnabled = !isSaved;
            pickerPaymentMethod.IsEnabled = !isSaved;
            editorNotes.IsEnabled = !isSaved;

            btnSaveInvoice.IsVisible = !isSaved;
            gridPostSaveActions.IsVisible = isSaved;
            btnNewInvoice.IsVisible = isSaved;
        }

        // ==========================================
        //  العميل 
        // ==========================================
        private void OnToggleCustomersList_Clicked(object? sender, EventArgs e)
        {
            if (searchCustomer.IsFocused) searchCustomer.Unfocus();

            if (frameCustomers.IsVisible)
            {
                frameCustomers.IsVisible = false;
            }
            else
            {
                if (!_branchCustomers.Any())
                {
                    DisplayAlert("تنبيه", "لا يوجد عملاء متاحين حالياً.", "موافق");
                    return;
                }
                cvCustomersList.ItemsSource = _branchCustomers;
                frameCustomers.IsVisible = true;
            }
        }

        private void OnCustomerSearchFocused(object? sender, FocusEventArgs e)
        {
            var keyword = searchCustomer.Text?.ToLower() ?? string.Empty;
            var list = string.IsNullOrWhiteSpace(keyword)
                ? _branchCustomers
                : _branchCustomers.Where(c => c.Name.ToLower().Contains(keyword)).ToList();

            cvCustomersList.ItemsSource = list;
            frameCustomers.IsVisible = list.Any();
        }

        private void OnCustomerSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_isSelectingCustomer) return;

            _selectedCustomerId = null;
            var keyword = e.NewTextValue?.ToLower() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                cvCustomersList.ItemsSource = _branchCustomers;
                frameCustomers.IsVisible = _branchCustomers.Any();
                return;
            }

            var filtered = _branchCustomers.Where(c => c.Name.ToLower().Contains(keyword)).ToList();
            cvCustomersList.ItemsSource = filtered;
            frameCustomers.IsVisible = filtered.Any();
        }

        private void OnCustomerSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is CustomerModel customer)
            {
                _isSelectingCustomer = true;
                searchCustomer.Text = customer.Name;
                _selectedCustomerId = customer.Id;
                frameCustomers.IsVisible = false;
                cvCustomersList.SelectedItem = null;
                searchCustomer.Unfocus();
                _isSelectingCustomer = false;
            }
        }

        // ==========================================
        //  المنتجات 
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
                if (!_availableProducts.Any())
                {
                    DisplayAlert("تنبيه", "لا توجد منتجات متاحة في المخزن حالياً.", "موافق");
                    return;
                }
                cvProductsList.ItemsSource = _availableProducts;
                frameProducts.IsVisible = true;
            }
        }

        private void OnProductSearchFocused(object? sender, FocusEventArgs e)
        {
            var keyword = searchProduct.Text?.ToLower() ?? string.Empty;
            var list = string.IsNullOrWhiteSpace(keyword)
                ? _availableProducts
                : _availableProducts.Where(p => p.Name.ToLower().Contains(keyword)).ToList();

            cvProductsList.ItemsSource = list;
            frameProducts.IsVisible = list.Any();
        }

        private void OnProductSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_isSelectingProduct) return;

            if (_selectedProductForAdd != null && searchProduct.Text != _selectedProductForAdd.Name)
            {
                _selectedProductForAdd = null;
                entryUnitPrice.Text = string.Empty;
                entryQuantity.Text = string.Empty;
                lblAvailableQty.Text = "المتاح: -";
            }

            var keyword = e.NewTextValue?.ToLower() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                cvProductsList.ItemsSource = _availableProducts;
                frameProducts.IsVisible = _availableProducts.Any();
                return;
            }

            var filtered = _availableProducts.Where(p => p.Name.ToLower().Contains(keyword)).ToList();
            cvProductsList.ItemsSource = filtered;
            frameProducts.IsVisible = filtered.Any();
        }

        private void OnProductSelectedFromSearch(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is ProductModel product)
            {
                _isSelectingProduct = true;
                searchProduct.Text = product.Name;
                _selectedProductForAdd = product;

                entryUnitPrice.Text = product.PurchasePrice > 0 ? product.PurchasePrice.ToString("0.00") : string.Empty;

                decimal otherItemsQty = _invoiceItems
                    .Where(i => i.ProductId == product.Id && i != _editingItem)
                    .Sum(i => i.Quantity);
                decimal availableNow = (decimal)product.StockQuantity - otherItemsQty;
                lblAvailableQty.Text = $"المتاح: {availableNow:0.##}";

                entryQuantity.Text = _editingItem != null ? _editingItem.Quantity.ToString("0.##") : string.Empty;

                frameProducts.IsVisible = false;
                cvProductsList.SelectedItem = null;
                searchProduct.Unfocus();
                _isSelectingProduct = false;
            }
        }

        // ==========================================
        //  إدارة الفاتورة والحفظ
        // ==========================================
        private void ResetInvoiceItems()
        {
            _invoiceItems.Clear();
            _isSelectingCustomer = true;
            searchCustomer.Text = string.Empty;
            _selectedCustomerId = null;
            _isSelectingCustomer = false;

            entryDiscount.Text = string.Empty;
            entryPaidAmount.Text = string.Empty;
            pickerPaymentMethod.SelectedIndex = -1;
            editorNotes.Text = string.Empty;

            ResetProductEntry();
            UpdateTotals();
        }

        private void ResetProductEntry()
        {
            _isSelectingProduct = true;
            searchProduct.Text = string.Empty;
            _isSelectingProduct = false;

            entryQuantity.Text = string.Empty;
            entryUnitPrice.Text = string.Empty;
            lblAvailableQty.Text = "المتاح: -";
            _selectedProductForAdd = null;
            _editingItem = null;
            frameProducts.IsVisible = false;

            lblAddSectionTitle.Text = "إضافة منتج";
            frameAddProduct.BorderColor = Colors.Transparent;
            btnAddItem.Text = "+ إضافة للفاتورة";
            btnAddItem.BackgroundColor = Color.FromArgb("#27AE60");
            btnCancelEdit.IsVisible = false;
        }

        private void OnInvoiceItemDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (_isInvoiceSaved) return; // الحماية من التعديل بعد الحفظ

            if (sender is not Frame frame) return;
            if (frame.BindingContext is not InvoiceDetailModel item) return;

            _editingItem = item;
            _selectedItem = item;

            var product = _availableProducts.FirstOrDefault(p => p.Id == item.ProductId);
            _selectedProductForAdd = product;

            _isSelectingProduct = true;
            searchProduct.Text = item.ProductName;
            _isSelectingProduct = false;

            entryQuantity.Text = item.Quantity.ToString("0.##");
            entryUnitPrice.Text = item.UnitPrice.ToString("0.00");
            frameProducts.IsVisible = false;

            if (product != null)
            {
                decimal otherItemsQty = _invoiceItems
                    .Where(i => i.ProductId == item.ProductId && i != item)
                    .Sum(i => i.Quantity);
                decimal availableForEdit = (decimal)product.StockQuantity - otherItemsQty;
                lblAvailableQty.Text = $"المتاح: {availableForEdit:0.##}";
            }
            else lblAvailableQty.Text = "المتاح: -";

            lblAddSectionTitle.Text = "تعديل الصنف";
            frameAddProduct.BorderColor = Color.FromArgb("#F39C12");
            btnAddItem.Text = "تحديث الصنف";
            btnAddItem.BackgroundColor = Color.FromArgb("#F39C12");
            btnCancelEdit.IsVisible = true;
        }

        private void OnCancelEditClicked(object? sender, EventArgs e)
        {
            ResetProductEntry();
        }

        private async void OnAddItemClicked(object? sender, EventArgs e)
        {
            if (_selectedProductForAdd == null)
            {
                await DisplayAlert("تنبيه", "من فضلك ابحث عن المنتج واختره من القائمة أولاً.", "موافق");
                return;
            }

            var product = _selectedProductForAdd;

            if (!decimal.TryParse(entryQuantity.Text, out decimal qty) || qty <= 0)
            {
                await DisplayAlert("تنبيه", "من فضلك أدخل كمية صحيحة.", "موافق");
                return;
            }

            if (!decimal.TryParse(entryUnitPrice.Text, out decimal price) || price < 0)
            {
                await DisplayAlert("تنبيه", "من فضلك أدخل سعر بيع صحيح.", "موافق");
                return;
            }

            decimal otherItemsQty = _invoiceItems
                .Where(i => i.ProductId == product.Id && i != _editingItem)
                .Sum(i => i.Quantity);

            if (otherItemsQty + qty > (decimal)product.StockQuantity)
            {
                await DisplayAlert("تنبيه", $"الكمية المطلوبة أكبر من المتاح ({product.StockQuantity:0.##}).", "موافق");
                return;
            }

            if (_editingItem != null)
            {
                _editingItem.ProductId = product.Id;
                _editingItem.ProductName = product.Name;
                _editingItem.Quantity = qty;
                _editingItem.UnitPrice = price;
                _editingItem.Total = qty * price;
                RefreshItemsUI();
            }
            else
            {
                var existing = _invoiceItems.FirstOrDefault(i => i.ProductId == product.Id && i.UnitPrice == price);
                if (existing != null)
                {
                    existing.Quantity += qty;
                    existing.Total = existing.Quantity * existing.UnitPrice;
                    RefreshItemsUI();
                }
                else
                {
                    _invoiceItems.Add(new InvoiceDetailModel
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Quantity = qty,
                        UnitPrice = price,
                        Total = qty * price
                    });
                }
            }

            ResetProductEntry();
            UpdateTotals();
        }

        private void RefreshItemsUI()
        {
            cvInvoiceItems.ItemsSource = null;
            cvInvoiceItems.ItemsSource = _invoiceItems;
        }

        private void OnItemSelected(object? sender, SelectionChangedEventArgs e)
        {
            _selectedItem = e.CurrentSelection.FirstOrDefault() as InvoiceDetailModel;
        }

        private async void OnRemoveItemClicked(object? sender, EventArgs e)
        {
            if (_selectedItem == null)
            {
                await DisplayAlert("تنبيه", "اختر الصنف اللي عايز تحذفه من القايمة الأول.", "موافق");
                return;
            }

            if (_editingItem == _selectedItem) ResetProductEntry();

            _invoiceItems.Remove(_selectedItem);
            _selectedItem = null;
            UpdateTotals();
        }

        private void OnDiscountOrPaidChanged(object? sender, TextChangedEventArgs e)
        {
            UpdateTotals();
        }

        private void UpdateTotals()
        {
            decimal total = _invoiceItems.Sum(i => i.Total);

            decimal discAmount = string.IsNullOrWhiteSpace(entryDiscount.Text) ? 0 : decimal.TryParse(entryDiscount.Text, out decimal parsedDisc) ? parsedDisc : 0;
            decimal paidAmount = string.IsNullOrWhiteSpace(entryPaidAmount.Text) ? 0 : decimal.TryParse(entryPaidAmount.Text, out decimal parsedPaid) ? parsedPaid : 0;

            decimal netRequired = Math.Max(total - discAmount, 0);
            decimal remaining = Math.Max(netRequired - paidAmount, 0);

            lblTotal.Text = total.ToString("N2") + " ج.م";
            lblNetRequired.Text = netRequired.ToString("N2") + " ج.م";
            lblRemaining.Text = remaining.ToString("N2") + " ج.م";
        }

        private async void OnSaveInvoiceClicked(object? sender, EventArgs e)
        {
            if (_invoiceItems.Count == 0)
            {
                await DisplayAlert("تنبيه", "لا يوجد أصناف مضافة للفاتورة.", "موافق");
                return;
            }

            if (pickerPaymentMethod.SelectedIndex == -1)
            {
                await DisplayAlert("تنبيه", "الرجاء اختيار طريقة الدفع أولاً.", "موافق");
                return;
            }

            decimal total = _invoiceItems.Sum(i => i.Total);
            decimal discAmount = string.IsNullOrWhiteSpace(entryDiscount.Text) ? 0 : decimal.TryParse(entryDiscount.Text, out decimal parsedDisc) ? parsedDisc : 0;
            decimal paidAmount = string.IsNullOrWhiteSpace(entryPaidAmount.Text) ? 0 : decimal.TryParse(entryPaidAmount.Text, out decimal parsedPaid) ? parsedPaid : 0;

            decimal netRequired = Math.Max(total - discAmount, 0);
            decimal remaining = Math.Max(netRequired - paidAmount, 0);

            string safePaymentMethod = pickerPaymentMethod.SelectedItem?.ToString() ?? "نقدي";

            bool confirm = await DisplayAlert("تأكيد", $"تأكيد حفظ الفاتورة بإجمالي {netRequired:N2} ج.م؟", "نعم", "إلغاء");
            if (!confirm) return;

            btnSaveInvoice.IsEnabled = false;
            btnSaveInvoice.Text = "جاري الحفظ... ⏳";

            try
            {
                var invoice = new InvoiceModel
                {
                    InvoiceNumber = lblInvoiceNumber.Text,
                    InvoiceDate = dpInvoiceDate.Date,
                    CustomerName = string.IsNullOrWhiteSpace(searchCustomer.Text) ? "عميل نقدي" : searchCustomer.Text.Trim(),
                    CustomerId = _selectedCustomerId,
                    LocationId = BranchContext.LocationId,
                    TotalAmount = total,
                    Discount = discAmount,
                    NetAmount = netRequired,
                    PaidAmount = paidAmount,
                    RemainingAmount = remaining,
                    PaymentMethod = safePaymentMethod,
                    Notes = editorNotes.Text?.Trim() ?? string.Empty,
                    IsReturn = false,
                    IsSynced = false
                };

                var savedItems = _invoiceItems.ToList();
                await _dbService.SaveFullInvoiceAsync(invoice, savedItems);

                if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                {
                    if (SyncLock.TryEnter())
                    {
                        try
                        {
                            var detailDtos = new List<SaleDetailDto>();
                            foreach (var item in savedItems)
                            {
                                var prod = _availableProducts.FirstOrDefault(p => p.Id == item.ProductId);
                                detailDtos.Add(new SaleDetailDto
                                {
                                    ProductId = prod?.ServerId ?? 0,
                                    Quantity = (int)Math.Round(item.Quantity),
                                    UnitPrice = item.UnitPrice,
                                    TotalPrice = item.Total
                                });
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
                                Notes = invoice.Notes,
                                Details = detailDtos
                            };

                            string baseUrl = ApiConfig.BaseUrl.TrimEnd('/');
                            var response = await ApiClient.Instance.PostAsJsonAsync($"{baseUrl}/api/Sales", dto);
                            if (response.IsSuccessStatusCode)
                            {
                                var result = await response.Content.ReadFromJsonAsync<SaleSyncResponse>();
                                await _dbService.MarkInvoiceAsSyncedAsync(invoice.Id, result?.ServerId ?? 0);
                            }
                        }
                        catch { }
                        finally { SyncLock.Exit(); }
                    }
                }

                await DisplayAlert("نجاح", "تم حفظ الفاتورة بنجاح. يمكنك الآن طباعتها أو مشاركتها.", "موافق");

                // إغلاق الشاشة ومنع التعديل وتفعيل أزرار الطباعة
                SetInvoiceState(true);
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ", "حصل مشكلة أثناء حفظ الفاتورة: " + ex.Message, "موافق");
            }
            finally
            {
                btnSaveInvoice.IsEnabled = true;
                btnSaveInvoice.Text = "حفظ واعتماد الفاتورة ✔️";
            }
        }

        private async void OnNewInvoiceClicked(object? sender, EventArgs e)
        {
            // إعادة ضبط الشاشة لفاتورة جديدة
            ResetInvoiceItems();
            SetInvoiceState(false);

            lblInvoiceNumber.Text = await _dbService.GenerateInvoiceNumberAsync();
            dpInvoiceDate.Date = DateTime.Now;
            await LoadLocalData();
        }

        private string GenerateInvoiceSummary()
        {
            if (_invoiceItems.Count == 0) return string.Empty;

            string custName = string.IsNullOrWhiteSpace(searchCustomer.Text) ? "عميل نقدي" : searchCustomer.Text.Trim();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"📄 *فاتورة مبيعات*");
            sb.AppendLine($"رقم: {lblInvoiceNumber.Text}");
            sb.AppendLine($"الفرع: {BranchContext.BranchName}");
            sb.AppendLine($"التاريخ: {dpInvoiceDate.Date.ToString("yyyy/MM/dd")}");
            sb.AppendLine($"العميل: {custName}");
            sb.AppendLine("--------------------");

            foreach (var item in _invoiceItems)
            {
                sb.AppendLine($"▫️ {item.ProductName}");
                sb.AppendLine($"الكمية: {item.Quantity} × {item.UnitPrice:N2} = {item.Total:N2} ج.م");
            }

            sb.AppendLine("--------------------");
            sb.AppendLine($"الإجمالي: {lblTotal.Text}");
            if (entryDiscount.Text != "0" && !string.IsNullOrWhiteSpace(entryDiscount.Text))
                sb.AppendLine($"الخصم: {entryDiscount.Text} ج.م");
            sb.AppendLine($"*الصافي المطلوب: {lblNetRequired.Text}*");
            sb.AppendLine("--------------------");

            string paymentMethodText = pickerPaymentMethod.SelectedIndex != -1 ? pickerPaymentMethod.SelectedItem?.ToString() ?? "غير محدد" : "غير محدد";
            sb.AppendLine($"المدفوع: {(string.IsNullOrWhiteSpace(entryPaidAmount.Text) ? "0" : entryPaidAmount.Text)} ج.م ({paymentMethodText})");

            if (lblRemaining.Text != "0.00 ج.م")
                sb.AppendLine($"المتبقي: {lblRemaining.Text}");

            if (!string.IsNullOrWhiteSpace(editorNotes.Text))
                sb.AppendLine($"\nملاحظات: {editorNotes.Text}");

            sb.AppendLine("\nشكراً لتعاملكم معنا 🙏");
            return sb.ToString();
        }

        private async void OnShareInvoiceClicked(object? sender, EventArgs e)
        {
            string summary = GenerateInvoiceSummary();
            if (string.IsNullOrWhiteSpace(summary))
            {
                await DisplayAlert("تنبيه", "لا توجد بيانات للمشاركة. يرجى إضافة أصناف أولاً.", "موافق");
                return;
            }

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Text = summary,
                Title = "مشاركة الفاتورة"
            });
        }

        private async void OnPrintInvoiceClicked(object? sender, EventArgs e)
        {
            if (_invoiceItems.Count == 0)
            {
                await DisplayAlert("تنبيه", "لا توجد بيانات للطباعة. يرجى إضافة أصناف.", "موافق");
                return;
            }

            string paidValue = string.IsNullOrWhiteSpace(entryPaidAmount.Text) ? "0" : entryPaidAmount.Text;
            string custName = string.IsNullOrWhiteSpace(searchCustomer.Text) ? "عميل نقدي" : searchCustomer.Text.Trim();

            string htmlContent = $@"
    <!DOCTYPE html>
    <html dir='rtl' lang='ar'>
    <head>
        <meta charset='UTF-8'>
        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
        <style>
            @import url('https://fonts.googleapis.com/css2?family=Tajawal:wght@400;700&display=swap');
            body {{
                font-family: 'Tajawal', Tahoma, Arial, sans-serif;
                background-color: #f4f6f9;
                padding: 10px;
                color: #2c3e50;
            }}
            .invoice-box {{
                max-width: 400px;
                margin: auto;
                background: #ffffff;
                padding: 20px;
                border: 1px solid #e2e8f0;
                border-radius: 8px;
                box-shadow: 0 4px 6px rgba(0,0,0,0.05);
            }}
            /* ---- تصميم اللوجو والهيدر الجديد ---- */
            .header {{
                text-align: center;
                border-bottom: 2px solid #e2e8f0;
                padding-bottom: 15px;
                margin-bottom: 20px;
            }}
            .brand-icon {{
                display: inline-block;
                background-color: #0f172a;
                color: #facc15;
                width: 45px;
                height: 45px;
                line-height: 45px;
                border-radius: 50%;
                font-size: 26px;
                font-weight: bold;
                vertical-align: middle;
                margin-left: 10px;
                box-shadow: 0 2px 4px rgba(0,0,0,0.1);
            }}
            .brand-text {{
                font-size: 28px;
                font-weight: bold;
                color: #0f172a;
                vertical-align: middle;
            }}
            .header p {{
                margin: 5px 0 0 0;
                color: #64748b;
                font-size: 13px;
            }}
            /* ---------------------------------- */
            .info-grid {{
                display: flex;
                justify-content: space-between;
                margin-bottom: 15px;
                font-size: 14px;
            }}
            .info-grid div span {{ font-weight: bold; color: #0f172a; }}
            table {{
                width: 100%;
                border-collapse: collapse;
                margin-bottom: 15px;
                font-size: 14px;
            }}
            th {{
                background-color: #f1f5f9;
                border-bottom: 2px solid #cbd5e1;
                padding: 10px 5px;
                text-align: right;
                font-weight: bold;
            }}
            td {{
                padding: 10px 5px;
                border-bottom: 1px solid #e2e8f0;
            }}
            .totals {{
                background-color: #f8fafc;
                padding: 15px;
                border-radius: 6px;
                font-size: 14px;
            }}
            .totals-row {{
                display: flex;
                justify-content: space-between;
                padding: 5px 0;
            }}
            .net-total {{
                font-size: 18px;
                font-weight: bold;
                color: #16a085;
                border-top: 2px solid #e2e8f0;
                padding-top: 10px;
                margin-top: 5px;
            }}
            .footer {{
                text-align: center;
                margin-top: 20px;
                font-size: 13px;
                color: #94a3b8;
                border-top: 2px dashed #cbd5e1;
                padding-top: 15px;
            }}
        </style>
    </head>
    <body>
        <div class='invoice-box'>
            
            <!-- الهيدر الجديد اللي ضفناه -->
            <div class='header'>
                <div>
                    <span class='brand-icon'>M</span>
                    <span class='brand-text'>نظام ماجستيك</span>
                </div>
                <p>فاتورة مبيعات فرعية</p>
            </div>

            <div class='info-grid'>
                <div>
                    <div>رقم الفاتورة: <span>{lblInvoiceNumber.Text}</span></div>
                    <div style='margin-top:5px;'>التاريخ: <span>{dpInvoiceDate.Date:yyyy/MM/dd}</span></div>
                </div>
                <div style='text-align: left;'>
                    <div>الفرع: <span>{BranchContext.BranchName}</span></div>
                    <div style='margin-top:5px;'>العميل: <span>{custName}</span></div>
                </div>
            </div>

            <table>
                <thead>
                    <tr>
                        <th>الصنف</th>
                        <th style='text-align: center;'>الكمية</th>
                        <th style='text-align: center;'>السعر</th>
                        <th style='text-align: left;'>الإجمالي</th>
                    </tr>
                </thead>
                <tbody>";

            foreach (var item in _invoiceItems)
            {
                htmlContent += $@"
                    <tr>
                        <td>{item.ProductName}</td>
                        <td style='text-align: center;'>{item.Quantity:0.##}</td>
                        <td style='text-align: center;'>{item.UnitPrice:N2}</td>
                        <td style='text-align: left; font-weight: bold;'>{item.Total:N2}</td>
                    </tr>";
            }

            htmlContent += $@"
                </tbody>
            </table>

            <div class='totals'>
                <div class='totals-row'>
                    <span>إجمالي الأصناف:</span>
                    <span>{lblTotal.Text}</span>
                </div>";

            if (entryDiscount.Text != "0" && !string.IsNullOrWhiteSpace(entryDiscount.Text))
            {
                htmlContent += $@"
                <div class='totals-row' style='color: #e74c3c;'>
                    <span>الخصم:</span>
                    <span>{entryDiscount.Text} ج.م</span>
                </div>";
            }

            htmlContent += $@"
                <div class='totals-row net-total'>
                    <span>الصافي المطلوب:</span>
                    <span>{lblNetRequired.Text}</span>
                </div>
                <div class='totals-row' style='margin-top: 10px;'>
                    <span>المدفوع:</span>
                    <span style='color: #2980b9; font-weight: bold;'>{paidValue} ج.م</span>
                </div>";

            if (lblRemaining.Text != "0.00 ج.م")
            {
                htmlContent += $@"
                <div class='totals-row'>
                    <span>المتبقي:</span>
                    <span style='color: #e67e22; font-weight: bold;'>{lblRemaining.Text}</span>
                </div>";
            }

            htmlContent += $@"
            </div>";

            if (!string.IsNullOrWhiteSpace(editorNotes.Text))
            {
                htmlContent += $"<div style='margin-top:15px; font-size:13px; color:#64748b;'><strong>ملاحظات:</strong> {editorNotes.Text}</div>";
            }

            htmlContent += $@"
            <div class='footer'>
                <p>شكراً لتعاملكم معنا 🙏</p>
            </div>
        </div>
    </body>
    </html>";

            var webView = new WebView { Source = new HtmlWebViewSource { Html = htmlContent } };
            var printPage = new ContentPage
            {
                Title = "معاينة الفاتورة للطباعة",
                Content = webView,
                BackgroundColor = Color.FromArgb("#f4f6f9")
            };

            await Navigation.PushAsync(printPage);
        }

        private async void OnViewTodayInvoicesClicked(object? sender, EventArgs e)
        {
            await DisplayAlert("قريباً", "ستتم إضافة شاشة مراجعة الفواتير اليومية في التحديث القادم.", "موافق");
        }
    }

    public class SaleInvoiceDto
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public int? CustomerId { get; set; }
        public int LocationId { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal Discount { get; set; }
        public decimal NetAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal RemainingAmount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public List<SaleDetailDto> Details { get; set; } = new();
    }

    public class SaleDetailDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }

    public class SaleSyncResponse
    {
        public int ServerId { get; set; }
    }
}