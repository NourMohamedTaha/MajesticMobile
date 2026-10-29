using SQLite;
using System;
using System.IO;
using System.Linq;
using MajesticMobile.Models;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MajesticMobile.Data
{
    public class LocalDbService
    {
        private const string DbFileName = "MajesticLocal.db3";
        private SQLiteAsyncConnection _db = null!;

        private async Task Init()
        {
            if (_db != null)
                return;

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, DbFileName);
            _db = new SQLiteAsyncConnection(dbPath);

            await _db.CreateTableAsync<ProductModel>();
            await _db.CreateTableAsync<InvoiceModel>();
            await _db.CreateTableAsync<InvoiceDetailModel>();
            await _db.CreateTableAsync<CategoryModel>();
            await _db.CreateTableAsync<CustomerModel>();
            await _db.CreateTableAsync<OrderModel>();
            await _db.CreateTableAsync<OrderDetailModel>();

            // تم إضافة السطر ده عشان الداتابيز تكريت جدول المصروفات وميضربش إيرور
            await _db.CreateTableAsync<CashTransactionModel>();
        }

        public async Task<List<ProductModel>> GetProductsAsync()
        {
            await Init();
            return await _db.Table<ProductModel>().ToListAsync();
        }

        public async Task<ProductModel?> GetProductByLocalIdAsync(int id)
        {
            await Init();
            return await _db.Table<ProductModel>().Where(p => p.Id == id).FirstOrDefaultAsync();
        }

        public async Task<int> SaveProductAsync(ProductModel product)
        {
            await Init();
            if (product.Id != 0)
                return await _db.UpdateAsync(product);
            else
                return await _db.InsertAsync(product);
        }

        public async Task DeleteAllProductsAsync()
        {
            await Init();
            await _db.DeleteAllAsync<ProductModel>();
        }

        public async Task DeleteServerProductsAsync()
        {
            await Init();
            await _db.Table<ProductModel>().DeleteAsync(p => p.ServerId != 0);
        }

        public async Task UpsertServerProductsAsync(List<ProductModel> serverProducts)
        {
            await Init();

            var existing = await _db.Table<ProductModel>().Where(p => p.ServerId != 0).ToListAsync();
            var existingByServerId = existing.ToDictionary(p => p.ServerId);

            var incomingServerIds = new HashSet<int>();

            foreach (var incoming in serverProducts)
            {
                incomingServerIds.Add(incoming.ServerId);

                if (existingByServerId.TryGetValue(incoming.ServerId, out var localProduct))
                {
                    localProduct.Name = incoming.Name;
                    localProduct.CategoryId = incoming.CategoryId;
                    localProduct.PurchasePrice = incoming.PurchasePrice;
                    localProduct.StockQuantity = incoming.StockQuantity;
                    await _db.UpdateAsync(localProduct);
                }
                else
                {
                    incoming.Id = 0;
                    await _db.InsertAsync(incoming);
                }
            }

            var noLongerOnServer = existing.Where(p => !incomingServerIds.Contains(p.ServerId)).ToList();
            foreach (var product in noLongerOnServer)
            {
                await _db.DeleteAsync(product);
            }
        }

        public async Task<List<CategoryModel>> GetCategoriesAsync()
        {
            await Init();
            return await _db.Table<CategoryModel>().ToListAsync();
        }

        public async Task<int> SaveCategoryAsync(CategoryModel category)
        {
            await Init();
            return await _db.InsertOrReplaceAsync(category);
        }

        public async Task DeleteAllCategoriesAsync()
        {
            await Init();
            await _db.DeleteAllAsync<CategoryModel>();
        }

        public async Task<List<CustomerModel>> GetCustomersAsync()
        {
            await Init();
            return await _db.Table<CustomerModel>().ToListAsync();
        }

        public async Task<int> SaveCustomerAsync(CustomerModel customer)
        {
            await Init();
            return await _db.InsertOrReplaceAsync(customer);
        }

        public async Task DeleteAllCustomersAsync()
        {
            await Init();
            await _db.DeleteAllAsync<CustomerModel>();
        }

        public async Task<string> GenerateInvoiceNumberAsync()
        {
            await Init();
            int totalCount = await _db.Table<InvoiceModel>().CountAsync();
            string datePart = DateTime.Now.ToString("yyyyMMdd");
            return $"MOB-{datePart}-{(totalCount + 1):0000}";
        }

        public async Task SaveFullInvoiceAsync(InvoiceModel invoice, List<InvoiceDetailModel> details)
        {
            await Init();

            await _db.RunInTransactionAsync(tran =>
            {
                tran.Insert(invoice);

                foreach (var detail in details)
                {
                    detail.InvoiceId = invoice.Id;
                    tran.Insert(detail);

                    var product = tran.Find<ProductModel>(detail.ProductId);
                    if (product != null)
                    {
                        product.StockQuantity -= (double)detail.Quantity;
                        tran.Update(product);
                    }
                }
            });
        }

        public async Task<int> GetOfflineInvoicesCountAsync()
        {
            await Init();
            var invoices = await _db.Table<InvoiceModel>().ToListAsync();
            return invoices.Count(i => !i.IsSynced);
        }

        public async Task<List<InvoiceModel>> GetPendingInvoicesAsync()
        {
            await Init();
            return await _db.Table<InvoiceModel>().Where(i => !i.IsSynced).ToListAsync();
        }

        public async Task<List<InvoiceDetailModel>> GetInvoiceDetailsAsync(int invoiceId)
        {
            await Init();
            return await _db.Table<InvoiceDetailModel>().Where(d => d.InvoiceId == invoiceId).ToListAsync();
        }

        public async Task MarkInvoiceAsSyncedAsync(int invoiceId, int serverSaleId)
        {
            await Init();
            var invoice = await _db.Table<InvoiceModel>().Where(i => i.Id == invoiceId).FirstOrDefaultAsync();
            if (invoice != null)
            {
                invoice.IsSynced = true;
                invoice.ServerSaleId = serverSaleId;
                await _db.UpdateAsync(invoice);
            }
        }

        public async Task DeleteInvoiceAsync(int invoiceId)
        {
            await Init();
            await _db.Table<InvoiceDetailModel>().DeleteAsync(d => d.InvoiceId == invoiceId);
            await _db.Table<InvoiceModel>().DeleteAsync(i => i.Id == invoiceId);
        }

        public async Task ClearAllPendingInvoicesAsync()
        {
            await Init();
            var pending = await _db.Table<InvoiceModel>().Where(i => !i.IsSynced).ToListAsync();
            foreach (var invoice in pending)
            {
                await DeleteInvoiceAsync(invoice.Id);
            }
        }

        // ========================================================
        // دوال الطلبيات والاستلام
        // ========================================================

        public async Task SaveOrderAsync(OrderModel order, List<OrderDetailModel> details)
        {
            await Init();

            await _db.RunInTransactionAsync(tran =>
            {
                tran.Insert(order);

                foreach (var item in details)
                {
                    item.OrderId = order.Id;
                    tran.Insert(item);
                }
            });
        }

        public async Task<List<OrderModel>> GetOrdersAsync()
        {
            await Init();
            return await _db.Table<OrderModel>().OrderByDescending(o => o.OrderDate).ToListAsync();
        }

        public async Task<List<OrderDetailModel>> GetOrderDetailsAsync(int orderId)
        {
            await Init();
            return await _db.Table<OrderDetailModel>().Where(d => d.OrderId == orderId).ToListAsync();
        }

        public async Task UpdateOrderAsync(OrderModel order)
        {
            await Init();
            await _db.UpdateAsync(order);
        }

        public async Task<List<OrderModel>> GetPendingOrdersAsync()
        {
            await Init();
            return await _db.Table<OrderModel>().Where(o => !o.IsSynced).ToListAsync();
        }

        public async Task MarkOrderAsSyncedAsync(int localOrderId, int serverId)
        {
            await Init();
            var order = await _db.Table<OrderModel>().Where(o => o.Id == localOrderId).FirstOrDefaultAsync();
            if (order != null)
            {
                order.IsSynced = true;
                order.ServerId = serverId;
                await _db.UpdateAsync(order);
            }
        }

        public async Task<List<OrderModel>> GetPendingDeliveryConfirmationsAsync()
        {
            await Init();
            return await _db.Table<OrderModel>().Where(o => o.PendingDeliverySync).ToListAsync();
        }

        // ========================================================
        // دوال الخزنة والمصروفات
        // ========================================================

        public async Task SaveCashTransactionAsync(CashTransactionModel trans)
        {
            await Init();
            await _db.InsertAsync(trans);
        }

        public async Task<List<CashTransactionModel>> GetTodayCashTransactionsAsync()
        {
            await Init();
            var today = DateTime.Today;
            var all = await _db.Table<CashTransactionModel>().ToListAsync();
            return all.Where(t => t.TransactionDate.Date == today).ToList();
        }

        public async Task<List<InvoiceModel>> GetTodayInvoicesAsync()
        {
            await Init();
            var today = DateTime.Today;
            // تم تصحيح الموديل هنا لـ InvoiceModel بدل SaleInvoiceModel
            var all = await _db.Table<InvoiceModel>().ToListAsync();
            return all.Where(i => i.InvoiceDate.Date == today).ToList();
        }

        public async Task<List<CashTransactionModel>> GetPendingCashTransactionsAsync()
        {
            await Init();
            return await _db.Table<CashTransactionModel>().Where(t => !t.IsSynced).ToListAsync();
        }

        public async Task MarkCashTransactionAsSyncedAsync(int id)
        {
            await Init();
            var trans = await _db.Table<CashTransactionModel>().Where(t => t.Id == id).FirstOrDefaultAsync();
            if (trans != null)
            {
                trans.IsSynced = true;
                await _db.UpdateAsync(trans);
            }
        }
    }

    // ========================================================
    // موديل المصروفات والخزنة
    // ========================================================
    public class CashTransactionModel
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement]
        public int Id { get; set; }
        public DateTime TransactionDate { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public int LocationId { get; set; }
        public bool IsSynced { get; set; }
    }
}