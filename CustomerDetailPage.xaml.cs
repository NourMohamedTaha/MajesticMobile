using MajesticMobile.Models;
using MajesticMobile.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace MajesticMobile
{
    public class LedgerRowViewModel
    {
        public string Description { get; set; } = string.Empty;
        public DateTime TransDate { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal RunningBalance { get; set; }
        public bool HasDebit => Debit > 0;
        public bool HasCredit => Credit > 0;
    }

    public partial class CustomerDetailPage : ContentPage
    {
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private CustomerModel _customer;
        private const string CashCustomerName = "عميل نقدي";

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

        public CustomerDetailPage(CustomerModel customer)
        {
            InitializeComponent();
            _customer = customer;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            lblCustomerName.Text = _customer.Name;
            lblCustomerPhone.Text = string.IsNullOrWhiteSpace(_customer.Phone) ? "لا يوجد رقم هاتف" : _customer.Phone;

            if (!string.IsNullOrWhiteSpace(_customer.Region))
            {
                lblCustomerRegion.Text = $"📍 {_customer.Region}";
                lblCustomerRegion.IsVisible = true;
            }
            else
            {
                lblCustomerRegion.IsVisible = false;
            }

            if (_customer.Name == CashCustomerName)
            {
                btnDelete.IsVisible = false;
            }

            refreshViewLedger.IsRefreshing = true;
        }

        private async void OnRefreshLedgerRefreshing(object sender, EventArgs e)
        {
            await LoadLedger();
            refreshViewLedger.IsRefreshing = false;
        }

        private async Task LoadLedger()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await DisplayAlert("لا يوجد اتصال", "هذه الشاشة تحتاج اتصال بالإنترنت لعرض كشف الحساب.", "موافق");
                return;
            }

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                string url = $"{ApiConfig.BaseUrl.TrimEnd('/')}/api/Customers/ledger/{_customer.Id}";

                var data = await _httpClient.GetFromJsonAsync<CustomerLedgerResponse>(url, options);

                if (data != null)
                {
                    decimal running = data.InitialBalance;
                    var rows = new List<LedgerRowViewModel>();

                    if (data.InitialBalance != 0)
                    {
                        rows.Add(new LedgerRowViewModel
                        {
                            Description = "رصيد افتتاحي (سابق)",
                            TransDate = DateTime.Now,
                            Debit = data.InitialBalance > 0 ? data.InitialBalance : 0,
                            Credit = data.InitialBalance < 0 ? Math.Abs(data.InitialBalance) : 0,
                            RunningBalance = running
                        });
                    }

                    if (data.Entries != null)
                    {
                        foreach (var entry in data.Entries)
                        {
                            running += (entry.Debit - entry.Credit);
                            rows.Add(new LedgerRowViewModel
                            {
                                Description = entry.Description,
                                TransDate = entry.TransDate,
                                Debit = entry.Debit,
                                Credit = entry.Credit,
                                RunningBalance = running
                            });
                        }
                    }

                    cvLedger.ItemsSource = rows;
                    lblCurrentBalance.Text = running.ToString("N2") + " ج.م";
                    lblCurrentBalance.TextColor = running > 0 ? Color.FromArgb("#E74C3C") : Color.FromArgb("#2ECC71");

                    _customer.Balance = running;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ", "حدث خطأ في جلب كشف الحساب: " + ex.Message, "موافق");
            }
        }

        private async void OnEditClicked(object sender, EventArgs e)
        {
            if (_customer.Name == CashCustomerName)
            {
                await DisplayAlert("تنبيه", "لا يمكن تعديل بيانات العميل النقدي الافتراضي للفرع.", "موافق");
                return;
            }

            // تم إضافة علامة الاستفهام (?) لحل تحذير Nullable
            string? name = await DisplayPromptAsync("تعديل العميل", "اسم العميل:", "حفظ", "إلغاء", initialValue: _customer.Name);
            if (string.IsNullOrWhiteSpace(name)) return;

            string? phone = await DisplayPromptAsync("تعديل الهاتف", "رقم هاتف العميل:", "حفظ", "إلغاء", initialValue: _customer.Phone, keyboard: Keyboard.Telephone);
            string? region = await DisplayPromptAsync("تعديل المنطقة", "المنطقة / العنوان:", "حفظ", "إلغاء", initialValue: _customer.Region);

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await DisplayAlert("تنبيه", "تتطلب هذه العملية اتصالاً بالإنترنت.", "موافق");
                return;
            }

            try
            {
                var request = new CustomerRequest
                {
                    Name = name.Trim(),
                    Phone = phone?.Trim() ?? string.Empty,
                    Region = region?.Trim() ?? string.Empty,
                    Balance = _customer.Balance,
                    LocationId = BranchContext.LocationId
                };

                string url = $"{ApiConfig.BaseUrl.TrimEnd('/')}/api/Customers/{_customer.Id}";
                var response = await _httpClient.PutAsJsonAsync(url, request);

                if (response.IsSuccessStatusCode)
                {
                    _customer.Name = request.Name;
                    _customer.Phone = request.Phone;
                    _customer.Region = request.Region;

                    lblCustomerName.Text = _customer.Name;
                    lblCustomerPhone.Text = string.IsNullOrWhiteSpace(_customer.Phone) ? "لا يوجد رقم هاتف" : _customer.Phone;

                    if (!string.IsNullOrWhiteSpace(_customer.Region))
                    {
                        lblCustomerRegion.Text = $"📍 {_customer.Region}";
                        lblCustomerRegion.IsVisible = true;
                    }
                    else
                    {
                        lblCustomerRegion.IsVisible = false;
                    }

                    await DisplayAlert("تم", "تم تحديث بيانات العميل بنجاح.", "موافق");
                }
                else
                {
                    await DisplayAlert("خطأ", "فشل في تحديث البيانات.", "موافق");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ", "حدث مشكلة: " + ex.Message, "موافق");
            }
        }

        private async void OnDeleteClicked(object sender, EventArgs e)
        {
            if (_customer.Name == CashCustomerName)
            {
                await DisplayAlert("تنبيه", "العميل النقدي لا يمكن حذفه للحفاظ على سجلات المبيعات النقدية.", "موافق");
                return;
            }

            bool confirm = await DisplayAlert("تأكيد الحذف", "هل أنت متأكد من حذف هذا العميل نهائياً؟", "نعم، احذف", "إلغاء");
            if (!confirm) return;

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await DisplayAlert("تنبيه", "تتطلب هذه العملية اتصالاً بالإنترنت.", "موافق");
                return;
            }

            try
            {
                string url = $"{ApiConfig.BaseUrl.TrimEnd('/')}/api/Customers/{_customer.Id}";
                var response = await _httpClient.DeleteAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    await DisplayAlert("تم", "تم حذف العميل بنجاح.", "موافق");
                    await Navigation.PopAsync();
                }
                else
                {
                    await DisplayAlert("خطأ", "لا يمكن حذف عميل له فواتير أو حركات مالية مسجلة.", "موافق");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ", "حدث مشكلة: " + ex.Message, "موافق");
            }
        }

        private async void OnPayClicked(object sender, EventArgs e)
        {
            if (!decimal.TryParse(entryPaymentAmount.Text, out decimal amount) || amount <= 0)
            {
                await DisplayAlert("تنبيه", "يرجى إدخال مبلغ صحيح للسداد.", "موافق");
                return;
            }

            if (!BranchContext.IsConfigured) return;

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await DisplayAlert("تنبيه", "لا يوجد إنترنت. السداد يتطلب اتصالاً مباشراً بالخادم.", "موافق");
                return;
            }

            btnPay.IsEnabled = false;
            btnPay.Text = "⏳...";

            try
            {
                var request = new PayDebtRequest
                {
                    CustomerId = _customer.Id,
                    Amount = amount,
                    LocationId = BranchContext.LocationId
                };

                string url = $"{ApiConfig.BaseUrl.TrimEnd('/')}/api/Customers/pay";
                var response = await _httpClient.PostAsJsonAsync(url, request);

                if (response.IsSuccessStatusCode)
                {
                    entryPaymentAmount.Text = string.Empty;
                    await DisplayAlert("تم", "تم تسجيل دفعة السداد بنجاح.", "موافق");
                    refreshViewLedger.IsRefreshing = true;
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    await DisplayAlert("خطأ", "فشل السداد: " + error, "موافق");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ", "حدث مشكلة: " + ex.Message, "موافق");
            }
            finally
            {
                btnPay.IsEnabled = true;
                btnPay.Text = "تأكيد ✔";
            }
        }
    }

    public class CustomerLedgerResponse
    {
        public decimal InitialBalance { get; set; }
        public List<LedgerEntryDto> Entries { get; set; } = new();
    }

    public class LedgerEntryDto
    {
        public int RefId { get; set; }
        public DateTime TransDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string TransType { get; set; } = string.Empty;
    }
}