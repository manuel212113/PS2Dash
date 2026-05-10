namespace PS2Desktop.Services.Interfaces
{
    public interface IAvatarRepository
    {
        Task<List<(Guid id, string nombre, string image_url)>> GetAvatarsAsync();
        Task CreateAvatarAsync(string nombre, string imageUrl);
        Task DeleteAvatarAsync(Guid id);
    }
}
