using MajesticMobile.Data;
using MajesticMobile.Models;
using MajesticMobile.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace MajesticMobile
{
    public class CustomerRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public int LocationId { get; set; }
    }

    public partial class CustomersPage : ContentPage
    {
        private readonly LocalDbService _dbService;
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private List<CustomerModel> _allCustomers = new();

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

        public CustomersPage(LocalDbService dbService)
        {
            InitializeComponent();
            _dbService = dbService;
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

            await LoadLocalData();
            _ = SyncFromServerAsync();
        }

        private async Task LoadLocalData()
        {
            _allCustomers = await _dbService.GetCustomersAsync();
            ApplyFilter();
        }

        private async Task SyncFromServerAsync()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                string url = $"{ApiConfig.BaseUrl.TrimEnd('/')}/api/Customers?locationId={BranchContext.LocationId}";

                var serverCustomers = await _httpClient.GetFromJsonAsync<List<CustomerModel>>(url, options);

                if (serverCustomers != null)
                {
                    await _dbService.DeleteAllCustomersAsync();
                    foreach (var c in serverCustomers)
                    {
                        await _dbService.SaveCustomerAsync(c);
                    }
                    await LoadLocalData();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Sync Failed: " + ex.Message);
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() => {
                    refreshView.IsRefreshing = false;
                });
            }
        }

        private void ApplyFilter()
        {
            var keyword = searchBar.Text?.ToLower() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                cvCustomers.ItemsSource = _allCustomers;
                return;
            }

            cvCustomers.ItemsSource = _allCustomers
                .Where(c => c.Name.ToLower().Contains(keyword) ||
                           (!string.IsNullOrEmpty(c.Phone) && c.Phone.Contains(keyword)) ||
                           (!string.IsNullOrEmpty(c.Region) && c.Region.ToLower().Contains(keyword)))
                .ToList();
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private async void OnRefreshViewRefreshing(object sender, EventArgs e)
        {
            await SyncFromServerAsync();
        }

        private async void OnCustomerSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is CustomerModel customer)
            {
                cvCustomers.SelectedItem = null;
                await Navigation.PushAsync(new CustomerDetailPage(customer));
            }
        }

        private void OnShowAddCustomerFormClicked(object sender, EventArgs e)
        {
            txtNewName.Text = string.Empty;
            txtNewPhone.Text = string.Empty;
            txtNewRegion.Text = string.Empty;
            txtNewBalance.Text = "0";

            overlayAddCustomer.IsVisible = true;
        }

        private void OnCancelAddCustomerClicked(object sender, EventArgs e)
        {
            overlayAddCustomer.IsVisible = false;
        }

        private async void OnSaveCustomerClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNewName.Text))
            {
                await DisplayAlert("تنبيه", "يجب إدخال اسم العميل.", "موافق");
                return;
            }

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await DisplayAlert("تنبيه", "يجب أن تكون متصلاً بالإنترنت لإضافة عميل جديد.", "موافق");
                return;
            }

            decimal.TryParse(txtNewBalance.Text, out decimal balance);

            btnSaveCustomer.IsEnabled = false;
            btnSaveCustomer.Text = "⏳...";

            try
            {
                var request = new CustomerRequest
                {
                    Name = txtNewName.Text.Trim(),
                    Phone = txtNewPhone.Text?.Trim() ?? string.Empty,
                    Region = txtNewRegion.Text?.Trim() ?? string.Empty,
                    Balance = balance,
                    LocationId = BranchContext.LocationId
                };

                string url = $"{ApiConfig.BaseUrl.TrimEnd('/')}/api/Customers";
                var response = await _httpClient.PostAsJsonAsync(url, request);

                if (response.IsSuccessStatusCode)
                {
                    overlayAddCustomer.IsVisible = false;
                    refreshView.IsRefreshing = true;
                    await DisplayAlert("نجاح", "تمت إضافة العميل بنجاح.", "موافق");
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    await DisplayAlert("خطأ", "فشل الحفظ: " + error, "موافق");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ", "حصل مشكلة: " + ex.Message, "موافق");
            }
            finally
            {
                btnSaveCustomer.IsEnabled = true;
                btnSaveCustomer.Text = "حفظ ✔️";
            }
        }
    }
}