using PS2Desktop.Modelos;
using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Services
{
    public class SessionService : ISessionService
    {
        public User CurrentUser { get; set; }
        public bool IsLoggedIn => CurrentUser != null;

        public void Logout()
        {
            CurrentUser = null;
        }
    }
}
