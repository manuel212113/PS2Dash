using PS2Desktop.Modelos;

namespace PS2Desktop.Services.Interfaces
{
    public interface IGameRepository
    {
        Task<List<Game>> GetGamesAsync(int limit = 50, int offset = 0, string? search = null, string? sortBy = null, string? genre = null);
        Task<Game> GetGameByIdAsync(Guid id);
        Task CreateGameAsync(Game game);
        Task DeleteGameAsync(Guid id);
        Task<int> GetGameCountAsync(string? search = null, string? genre = null);
    }
}
