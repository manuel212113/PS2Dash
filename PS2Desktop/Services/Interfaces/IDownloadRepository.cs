using PS2Desktop.Modelos;

namespace PS2Desktop.Services.Interfaces
{
    public interface IDownloadRepository
    {
        Task<List<DownloadItem>> GetAllAsync();
        Task<List<DownloadItem>> GetByGameIdAsync(Guid gameId);
        Task CreateAsync(DownloadItem item);
        Task UpdateAsync(DownloadItem item);
        Task DeleteAsync(Guid id);
    }
}
