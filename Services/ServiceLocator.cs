using BIS.ERP.Services;

namespace BIS.ERP
{
    public static class ServiceLocator
    {
        private static IAuthService? _authService;
        private static InfoBaseManager? _infoBaseManager;

        public static IAuthService AuthService
        {
            get
            {
                _authService ??= new AuthService();
                return _authService;
            }
        }

        public static InfoBaseManager InfoBaseManager
        {
            get
            {
                _infoBaseManager ??= new InfoBaseManager();
                return _infoBaseManager;
            }
        }
    }
}