using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Services
{
    public class GoogleAuthServiceWrapper : IGoogleAuthService
    {
        public bool IsConfigured => GoogleAuthService.IsConfigured;

        public Task<GoogleUserInfo> LoginAsync()
        {
            return GoogleAuthService.LoginAsync();
        }

        public void ClearDataStore()
        {
            GoogleAuthService.ClearDataStore();
        }

        public static void Configure(string clientId, string clientSecret)
        {
            GoogleAuthService.Configure(clientId, clientSecret);
        }
    }
}
