using PS2Desktop.Modelos;

namespace PS2Desktop.Services.Interfaces
{
    public interface IGameRepository
    {
        Task<List<Game>> GetGamesAsync();
        Task<Game> GetGameByIdAsync(Guid id);
        Task CreateGameAsync(Game game);
        Task DeleteGameAsync(Guid id);
        Task<int> GetGameCountAsync();
    }
}
