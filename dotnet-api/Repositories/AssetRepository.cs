using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Repositories
{
    public interface IAssetRepository : IRepository<CreativeAsset>
    {
        Task<IEnumerable<CreativeAsset>> GetByTypeAsync(string type);
        Task<IEnumerable<CreativeAsset>> GetByCampaignIdAsync(string campaignId);
    }

    public class AssetRepository : Repository<CreativeAsset>, IAssetRepository
    {
        public AssetRepository(PostgresDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<CreativeAsset>> GetByTypeAsync(string type)
        {
            return await _dbSet
                .Where(a => a.Type == type)
                .OrderBy(a => a.Name)
                .ToListAsync();
        }

        public async Task<IEnumerable<CreativeAsset>> GetByCampaignIdAsync(string campaignId)
        {
            return await _dbSet
                .Where(a => a.CampaignId == campaignId)
                .OrderBy(a => a.Name)
                .ToListAsync();
        }
    }
}
