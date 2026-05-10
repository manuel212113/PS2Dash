namespace PS2Desktop.Services.Interfaces
{
    public interface IGoogleAuthService
    {
        bool IsConfigured { get; }
        Task<GoogleUserInfo> LoginAsync();
        void ClearDataStore();
    }
}
