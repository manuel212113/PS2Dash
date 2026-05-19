using PS2Desktop.Modelos;

namespace PS2Desktop.Services.Interfaces
{
    public interface IUserRepository
    {
        Task<User> CreateUserAsync(string email, string password);
        Task<User> AuthenticateUserAsync(string email, string password);
        Task<User> FindOrCreateUserWithGoogleAsync(string googleId, string email, string name, string? avatarUrl);
        Task UpdateUserAvatarAsync(Guid userId, string? avatarUrl);
        Task<User> UpdateUserAsync(Guid userId, string displayName);
        Task<List<User>> GetAllUsersAsync();
        Task<User> GetUserByEmailAsync(string email);
        Task UpdateUserRoleAsync(Guid userId, string role);
        Task<string> GenerateResetTokenAsync(string email);
        Task<bool> ResetPasswordAsync(string email, string token, string newPassword);
        Task<int> GetUserCountAsync();
    }
}
