using MajesticMobile.Services;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace MajesticMobile
{
    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int UserId { get; set; }
        public int LocationId { get; set; }
        public string BranchName { get; set; } = string.Empty;
    }

    public partial class LoginPage : ContentPage
    {
        private readonly NetworkBackgroundDrawable _backgroundDrawable;
        private IDispatcherTimer? _animationTimer;

        public LoginPage()
        {
            InitializeComponent();

            // تجهيز الخلفية المتحركة
            _backgroundDrawable = new NetworkBackgroundDrawable();
            backgroundGraphicsView.Drawable = _backgroundDrawable;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // تشغيل التايمر اللي بيحدث الخلفية (Animation Loop) بسرعة 60 فريم تقريباً
            _animationTimer = Application.Current?.Dispatcher.CreateTimer();
            if (_animationTimer != null)
            {
                _animationTimer.Interval = TimeSpan.FromMilliseconds(16);
                _animationTimer.Tick += (s, e) => backgroundGraphicsView.Invalidate();
                _animationTimer.Start();
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            // إيقاف التايمر لما نسيب الشاشة عشان نوفر بطارية
            _animationTimer?.Stop();
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            string user = entryUsername.Text?.Trim() ?? "";
            string pass = entryPassword.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            {
                await ShakeCard();
                await DisplayAlert("تنبيه", "برجاء إدخال اسم المستخدم وكلمة المرور.", "موافق");
                return;
            }

            await ShowLoading(true, "جاري التحقق...");

            // ==========================================================
            // 1. لو الموبايل فاصل واي فاي وداتا تماماً (دخول أوفلاين سريع)
            // ==========================================================
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                if (TryOfflineLogin(user, pass))
                {
                    NavigateToMain();
                }
                else
                {
                    await ShowLoading(false);
                    await ShakeCard();
                    await DisplayAlert("خطأ اتصال", "لا يوجد إنترنت. أدخل بيانات آخر حساب تم تسجيله أوفلاين.", "موافق");
                }
                return;
            }

            // ==========================================================
            // 2. الدخول أونلاين (الموبايل متصل بشبكة)
            // ==========================================================
            try
            {
                var loginData = new { Username = user, Password = pass };
                string url = $"{ApiConfig.BaseUrl.TrimEnd('/')}/api/Auth/Login";

                var response = await ApiClient.Instance.PostAsJsonAsync(url, loginData);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

                    if (result != null && result.Success)
                    {
                        BranchContext.SetBranch(result.LocationId, result.BranchName);

                        // حفظ البيانات للأوفلاين بشكل مشفر
                        Preferences.Default.Set("OfflineUser", user);
                        Preferences.Default.Set("OfflinePass", EncodeBase64(pass));
                        Preferences.Default.Set("OfflineLocId", result.LocationId);
                        Preferences.Default.Set("OfflineBranch", result.BranchName);

                        lblLoadingText.Text = "تم الدخول بنجاح ✔️";
                        lblLoadingText.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#22C55E");
                        await Task.Delay(500);

                        NavigateToMain();
                    }
                    else
                    {
                        await ShowLoading(false);
                        await ShakeCard();
                        await DisplayAlert("خطأ", result?.Message ?? "فشل الدخول", "موافق");
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    await ShowLoading(false);
                    await ShakeCard();
                    await DisplayAlert("مرفوض", "بيانات الدخول غير صحيحة.", "موافق");
                }
                else
                {
                    throw new Exception($"خطأ في السيرفر (Code: {response.StatusCode})"); // رمي الخطأ للـ catch
                }
            }
            catch (Exception ex)
            {
                // ==========================================================
                // 3. الخطة البديلة (السيرفر واقع أو الموبايل مش قادر يوصله)
                // ==========================================================
                if (TryOfflineLogin(user, pass))
                {
                    // تم الدخول أوفلاين بنجاح رغم إن السيرفر واقع
                    lblLoadingText.Text = "السيرفر لا يستجيب.. تم الدخول أوفلاين ⚠️";
                    lblLoadingText.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#F59E0B"); // لون برتقالي تحذيري
                    await Task.Delay(1500); // ينتظر ثانية ونص عشان اليوزر يقرأ الرسالة
                    NavigateToMain();
                }
                else
                {
                    // السيرفر واقع، واليوزر ده ملوش بيانات محفوظة أوفلاين
                    await ShowLoading(false);
                    await ShakeCard();
                    await DisplayAlert("مشكلة اتصال", "السيرفر لا يستجيب والبيانات غير محفوظة للأوفلاين.\nالتفاصيل: " + ex.Message, "موافق");
                }
            }
        }

        // ==========================================================
        // دالة مساعدة للتحقق من بيانات الأوفلاين لتقليل تكرار الكود
        // ==========================================================
        private bool TryOfflineLogin(string user, string pass)
        {
            string savedUser = Preferences.Default.Get("OfflineUser", "");
            string savedPassEncoded = Preferences.Default.Get("OfflinePass", "");
            int savedLocId = Preferences.Default.Get("OfflineLocId", 0);
            string savedBranch = Preferences.Default.Get("OfflineBranch", "");

            string savedPass = DecodeBase64(savedPassEncoded);

            if (!string.IsNullOrEmpty(savedUser) && user == savedUser && pass == savedPass)
            {
                BranchContext.SetBranch(savedLocId, savedBranch);
                return true;
            }
            return false;
        }

        private async Task ShowLoading(bool show, string message = "")
        {
            if (show)
            {
                lblLoadingText.Text = message;
                lblLoadingText.TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#94A3B8");
                await Task.WhenAll(btnLogin.FadeTo(0, 150), btnLogin.ScaleTo(0.9, 150));
                btnLogin.IsVisible = false;
                layoutLoading.IsVisible = true;
                await layoutLoading.FadeTo(1, 150);
            }
            else
            {
                await layoutLoading.FadeTo(0, 150);
                layoutLoading.IsVisible = false;
                btnLogin.IsVisible = true;
                await Task.WhenAll(btnLogin.FadeTo(1, 150), btnLogin.ScaleTo(1, 150));
            }
        }

        private async Task ShakeCard()
        {
            uint t = 50;
            await loginCard.TranslateTo(-15, 0, t);
            await loginCard.TranslateTo(15, 0, t);
            await loginCard.TranslateTo(-10, 0, t);
            await loginCard.TranslateTo(10, 0, t);
            loginCard.TranslationX = 0;
        }

        private void NavigateToMain()
        {
            if (Application.Current?.Windows.Count > 0)
                Application.Current.Windows[0].Page = new AppShell();
        }

        private void OnEntryFocused(object sender, FocusEventArgs e)
        {
            if (sender is Entry entry && entry.Parent is Border border)
                border.Stroke = Microsoft.Maui.Graphics.Color.FromArgb("#3B82F6");
        }

        private void OnEntryUnfocused(object sender, FocusEventArgs e)
        {
            if (sender is Entry entry && entry.Parent is Border border)
                border.Stroke = Microsoft.Maui.Graphics.Color.FromArgb("#2C3A52");
        }

        private string EncodeBase64(string plain) => string.IsNullOrEmpty(plain) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));

        private string DecodeBase64(string encoded)
        {
            try { return string.IsNullOrEmpty(encoded) ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(encoded)); }
            catch { return ""; }
        }
    }

    // =========================================================================
    // كلاس الرسم: بيرسم النقاط المتصلة بخطوط (Particle Network Animation)
    // =========================================================================
    public class NetworkBackgroundDrawable : IDrawable
    {
        private class Particle { public float X, Y, VX, VY, Radius; }

        private readonly List<Particle> _particles = new();
        private readonly Random _rnd = new();
        private bool _initialized;
        private DateTime _lastUpdate = DateTime.Now;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0) return;

            // إنشاء الجزيئات أول مرة فقط
            if (!_initialized)
            {
                int count = 35; // عدد النقاط
                for (int i = 0; i < count; i++)
                {
                    _particles.Add(new Particle
                    {
                        X = _rnd.Next((int)dirtyRect.Width),
                        Y = _rnd.Next((int)dirtyRect.Height),
                        VX = (_rnd.NextSingle() - 0.5f) * 30f,
                        VY = (_rnd.NextSingle() - 0.5f) * 30f,
                        Radius = _rnd.NextSingle() * 1.5f + 1f
                    });
                }
                _initialized = true;
            }

            // حساب الوقت بين الفريم الحالي والسابق للحركة السلسة
            var now = DateTime.Now;
            float dt = (float)(now - _lastUpdate).TotalSeconds;
            _lastUpdate = now;
            if (dt > 0.1f) dt = 0.1f;

            // تحديث الأماكن ورسم الخطوط
            float maxDist = 110f;
            float maxDistSq = maxDist * maxDist;

            for (int i = 0; i < _particles.Count; i++)
            {
                var p1 = _particles[i];

                // تحريك النقطة
                p1.X += p1.VX * dt;
                p1.Y += p1.VY * dt;

                // لو طلعت بره الشاشة، رجعها من الناحية التانية
                if (p1.X < -10) p1.X = dirtyRect.Width + 10;
                else if (p1.X > dirtyRect.Width + 10) p1.X = -10;

                if (p1.Y < -10) p1.Y = dirtyRect.Height + 10;
                else if (p1.Y > dirtyRect.Height + 10) p1.Y = -10;

                // ربط النقطة بالنقاط القريبة بخطوط
                for (int j = i + 1; j < _particles.Count; j++)
                {
                    var p2 = _particles[j];
                    float dx = p1.X - p2.X;
                    float dy = p1.Y - p2.Y;
                    float dSq = dx * dx + dy * dy;

                    if (dSq < maxDistSq)
                    {
                        float d = (float)Math.Sqrt(dSq);
                        float alpha = 1f - (d / maxDist);

                        // تم إضافة الـ Casting (byte) لضمان عدم حدوث خطأ Ambiguous Method
                        canvas.StrokeColor = Microsoft.Maui.Graphics.Color.FromRgba((byte)255, (byte)255, (byte)255, (byte)(alpha * 60));
                        canvas.StrokeSize = 1;
                        canvas.DrawLine(p1.X, p1.Y, p2.X, p2.Y);
                    }
                }

                // رسم النقطة نفسها
                canvas.FillColor = Microsoft.Maui.Graphics.Color.FromRgba((byte)255, (byte)255, (byte)255, (byte)100);
                canvas.FillCircle(p1.X, p1.Y, p1.Radius);
            }
        }
    }
}