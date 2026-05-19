using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PS2Desktop.Services.Interfaces
{
    public interface IFavoriteRepository
    {
        Task<bool> IsFavoriteAsync(Guid userId, Guid itemId, string itemType);
        Task ToggleFavoriteAsync(Guid userId, Guid itemId, string itemType);
        Task<List<(Guid ItemId, string ItemType)>> GetUserFavoritesAsync(Guid userId);
    }
}
