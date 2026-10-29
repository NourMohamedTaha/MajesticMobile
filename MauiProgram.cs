using Microsoft.Extensions.Logging;

namespace MajesticMobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // تسجيل خدمة الداتابيز المحلية (Singleton عشان تبقى نسخة واحدة شغالة على مستوى الأبلكيشن)
        builder.Services.AddSingleton<MajesticMobile.Data.LocalDbService>();

        // تسجيل الشاشات (Transient عشان يفتح نسخة جديدة ناضيفة كل ما تفتح الشاشة)
        builder.Services.AddTransient<MajesticMobile.MainPage>();
        builder.Services.AddTransient<MajesticMobile.InventoryPage>();
        builder.Services.AddTransient<MajesticMobile.SalesPage>();
        builder.Services.AddTransient<MajesticMobile.CustomersPage>();
        builder.Services.AddTransient<MajesticMobile.TreasuryPage>();

        // تم إضافة سطر شاشة الطلبيات هنا
        builder.Services.AddTransient<MajesticMobile.OrdersPage>();

        return builder.Build();
    }
}