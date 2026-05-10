using PS2Desktop.Modelos;

namespace PS2Desktop.Services.Interfaces
{
    public interface IUserRepository
    {
        Task<User> CreateUserAsync(string email, string password);
        Task<User> AuthenticateUserAsync(string email, string password);
        Task<User> FindOrCreateUserWithGoogleAsync(string googleId, string email, string name, string? avatarUrl);
        Task UpdateUserAvatarAsync(Guid userId, string? avatarUrl);
        Task<int> GetUserCountAsync();
    }
}
