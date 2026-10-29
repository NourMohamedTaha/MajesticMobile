using MajesticMobile.Data;
using MajesticMobile.Models;
using MajesticMobile.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using System.Text.Json;

namespace MajesticMobile
{
    public partial class InvoicesHistoryPage : ContentPage
    {
        private readonly LocalDbService _dbService;
        private List<InvoiceModel> _allInvoices = new();

        // ضفنا الـ HttpClient عشان نكلم السيرفر
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { return true; }
            })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            client.DefaultRequestHeaders.Add("X-Tunnel-Skip-AntiPhishing-Page", "true");
            return client;
        }

        public InvoicesHistoryPage()
        {
            InitializeComponent();
            _dbService = IPlatformApplication.Current?.Services.GetService<LocalDbService>();
        }

        public InvoicesHistoryPage(LocalDbService dbService)
        {
            InitializeComponent();
            _dbService = dbService;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_dbService == null) return;

            refreshViewInvoices.IsRefreshing = true;
            await LoadInvoicesAsync();
            refreshViewInvoices.IsRefreshing = false;
        }

        private async void OnRefreshRequested(object sender, EventArgs e)
        {
            await LoadInvoicesAsync();
            refreshViewInvoices.IsRefreshing = false;
        }

        private async Task LoadInvoicesAsync()
        {
            try
            {
                // 1. لو فيه إنترنت: هنجيب الفواتير من السيرفر مباشرة عشان نشوف أي تعديل حصل من الديسكتوب
                if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet && BranchContext.IsConfigured)
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    string url = $"{ApiConfig.BaseUrl.TrimEnd('/')}/api/Sales/DailyInvoices?locationId={BranchContext.LocationId}";

                    var serverInvoices = await _httpClient.GetFromJsonAsync<List<InvoiceModel>>(url, options);

                    if (serverInvoices != null)
                    {
                        _allInvoices = serverInvoices.OrderByDescending(i => i.InvoiceDate).ToList();
                    }
                }
                else
                {
                    // 2. لو مفيش إنترنت (أوفلاين): هنعرض الفواتير اللي الموبايل كان حافظها محلياً
                    var localInvoices = await _dbService.GetTodayInvoicesAsync();
                    if (localInvoices != null)
                    {
                        _allInvoices = localInvoices.OrderByDescending(i => i.InvoiceDate).ToList();
                    }
                }

                ApplyFilter();
            }
            catch (Exception ex)
            {
                await DisplayAlert("خطأ", "حدث مشكلة أثناء تحميل الفواتير: " + ex.Message, "موافق");
            }
        }

        private void ApplyFilter()
        {
            var keyword = searchBar.Text?.ToLower() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                cvInvoices.ItemsSource = _allInvoices;
                return;
            }

            cvInvoices.ItemsSource = _allInvoices
                .Where(i => (i.InvoiceNumber != null && i.InvoiceNumber.ToLower().Contains(keyword)) ||
                            (i.CustomerName != null && i.CustomerName.ToLower().Contains(keyword)))
                .ToList();
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }
    }
}