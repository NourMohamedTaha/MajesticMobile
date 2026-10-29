namespace MajesticMobile.Services
{
    public static class SyncLock
    {
        private static readonly object _gate = new object();
        private static bool _isSyncing = false;

        public static bool TryEnter()
        {
            lock (_gate)
            {
                if (_isSyncing) return false;
                _isSyncing = true;
                return true;
            }
        }

        public static void Exit()
        {
            lock (_gate)
            {
                _isSyncing = false;
            }
        }
    }
}