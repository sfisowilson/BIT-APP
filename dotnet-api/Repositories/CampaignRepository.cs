using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Repositories
{
    public interface ICampaignRepository : IRepository<CampaignItem>
    {
        Task<IEnumerable<CampaignItem>> GetActiveCampaignsAsync();
    }

    public class CampaignRepository : Repository<CampaignItem>, ICampaignRepository
    {
        public CampaignRepository(PostgresDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<CampaignItem>> GetActiveCampaignsAsync()
        {
            return await _dbSet
                .Where(c => c.Status == "Active")
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
        }
    }
}
