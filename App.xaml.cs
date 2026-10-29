using Microsoft.Maui.Controls;
using MajesticMobile.Services;
using MajesticMobile.Data;
using MajesticMobile.Models;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace MajesticMobile
{
    public static class ApiClient
    {
        public static readonly HttpClient Instance = Create();

        private static HttpClient Create()
        {
            var client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            })
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            client.DefaultRequestHeaders.Add("X-Tunnel-Skip-AntiPhishing-Page", "true");
            return client;
        }
    }

    public partial class App : Application
    {
        private IDispatcherTimer? _backgroundTimer;

        public App()
        {
            InitializeComponent();

            // ⚠️ ممنوع كتابة MainPage = new ... هنا نهائياً لأننا بنستخدم CreateWindow

            if (BranchContext.BranchName == "باب اللوق" || BranchContext.LocationId == 2)
            {
                BranchContext.Clear();
            }
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            Page initialPage;

            // --- التعديل هنا: إجبار التطبيق على فتح شاشة التسجيل دائماً ---
            initialPage = new LoginPage();

            /* 
            // الكود الأصلي (قم بإزالة التعليقات عنه لاحقاً للعمل بشكل طبيعي):
            if (BranchContext.IsConfigured)
            {
                initialPage = new AppShell();
            }
            else
            {
                initialPage = new LoginPage();
            }
            */

            var window = new Window(initialPage);

            _backgroundTimer = Application.Current?.Dispatcher.CreateTimer();
            if (_backgroundTimer != null)
            {
                _backgroundTimer.Interval = TimeSpan.FromSeconds(60);
                _backgroundTimer.Tick += async (s, e) => await PerformBackgroundSync();
                _backgroundTimer.Start();
            }

            return window;
        }

        private async Task PerformBackgroundSync()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
            if (!BranchContext.IsConfigured) return;

            if (!SyncLock.TryEnter()) return;

            try
            {
                var dbService = Handler?.MauiContext?.Services.GetService<LocalDbService>();
                if (dbService == null) return;

                var pendingInvoices = await dbService.GetPendingInvoicesAsync();
                if (pendingInvoices.Count > 0)
                {
                    var allProducts = await dbService.GetProductsAsync();
                    foreach (var invoice in pendingInvoices)
                    {
                        var details = await dbService.GetInvoiceDetailsAsync(invoice.Id);
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

                        if (!hasUnsyncedProduct)
                        {
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

                            var res = await ApiClient.Instance.PostAsJsonAsync($"{ApiConfig.BaseUrl}/api/Sales", dto);
                            if (res.IsSuccessStatusCode)
                            {
                                var result = await res.Content.ReadFromJsonAsync<SaleSyncResponse>();
                                await dbService.MarkInvoiceAsSyncedAsync(invoice.Id, result?.ServerId ?? 0);
                            }
                        }
                    }
                }

                var pendingTrans = await dbService.GetPendingCashTransactionsAsync();
                if (pendingTrans.Count > 0)
                {
                    var dtos = pendingTrans.Select(t => new
                    {
                        LocationId = BranchContext.LocationId,
                        TransactionDate = t.TransactionDate,
                        TransactionType = t.TransactionType,
                        Amount = t.Amount,
                        Description = t.Description
                    }).ToList();

                    var resTrans = await ApiClient.Instance.PostAsJsonAsync($"{ApiConfig.BaseUrl}/api/Treasury/Transactions", dtos);
                    if (resTrans.IsSuccessStatusCode)
                    {
                        foreach (var t in pendingTrans)
                            await dbService.MarkCashTransactionAsSyncedAsync(t.Id);
                    }
                }
            }
            catch { }
            finally
            {
                SyncLock.Exit();
            }
        }
    }
}