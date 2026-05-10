namespace PS2Desktop.Services.Interfaces
{
    public interface IVoteRepository
    {
        Task<bool> VoteAsync(Guid itemId, string itemType, Guid userId, int value);
        Task<(double average, int count)> GetAverageRatingAsync(Guid itemId, string itemType);
        Task<(double average, int count)> GetGlobalAverageRatingAsync();
    }
}
