using PS2Desktop.Modelos;

namespace PS2Desktop.Services.Interfaces
{
    public interface ISessionService
    {
        User CurrentUser { get; set; }
        bool IsLoggedIn { get; }
        bool IsAdmin { get; }
        void Logout();
    }
}
