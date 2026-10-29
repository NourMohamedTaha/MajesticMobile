using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using MajesticMobile.Services;

namespace MajesticMobile
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute("SalesPage", typeof(SalesPage));
            Routing.RegisterRoute("CustomersPage", typeof(CustomersPage));
            Routing.RegisterRoute("InventoryPage", typeof(InventoryPage));
        }

        // دالة يمكنك استدعاؤها من أي زر (Logout)
        public void DoLogout()
        {
            // 1. تنظيف بيانات الفرع
            BranchContext.Clear();

            // 2. تنظيف بيانات المستخدم الأوفلاين لمنع الدخول التلقائي
            Preferences.Default.Remove("OfflineUser");
            Preferences.Default.Remove("OfflinePass");
            Preferences.Default.Remove("OfflineLocId");
            Preferences.Default.Remove("OfflineBranch");

            // 3. إرجاع المستخدم لشاشة تسجيل الدخول
            if (Application.Current?.Windows.Count > 0)
            {
                Application.Current.Windows[0].Page = new LoginPage();
            }
        }
    }
}