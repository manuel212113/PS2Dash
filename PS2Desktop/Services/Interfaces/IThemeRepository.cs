using PS2Desktop.Modelos;

namespace PS2Desktop.Services.Interfaces
{
    public interface IThemeRepository
    {
        Task<List<Theme>> GetThemesAsync(string? search = null, string? sortBy = null);
        Task CreateThemeAsync(Theme theme);
        Task DeleteThemeAsync(Guid id);
        Task<int> GetThemeCountAsync(string? search = null);
    }
}
