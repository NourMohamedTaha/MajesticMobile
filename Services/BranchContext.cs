using Microsoft.Maui.Storage;

namespace MajesticMobile.Services
{
    public static class BranchContext
    {
        private const string LocationIdKey = "CurrentLocationId";
        private const string BranchNameKey = "CurrentBranchName";

        public static int LocationId
        {
            get => Preferences.Default.Get(LocationIdKey, 0);
            set => Preferences.Default.Set(LocationIdKey, value);
        }

        public static string BranchName
        {
            get => Preferences.Default.Get(BranchNameKey, "غير محدد");
            set => Preferences.Default.Set(BranchNameKey, value);
        }

        public static bool IsConfigured => LocationId > 0;

        public static void SetBranch(int locationId, string branchName)
        {
            LocationId = locationId;
            BranchName = branchName;
        }

        public static void Clear()
        {
            Preferences.Default.Remove(LocationIdKey);
            Preferences.Default.Remove(BranchNameKey);
        }
    }
}