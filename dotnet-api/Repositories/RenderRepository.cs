using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Repositories
{
    public interface IRenderRepository : IRepository<RenderItem>
    {
        Task<IEnumerable<RenderItem>> GetActiveRendersAsync();
    }

    public class RenderRepository : Repository<RenderItem>, IRenderRepository
    {
        public RenderRepository(PostgresDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<RenderItem>> GetActiveRendersAsync()
        {
            return await _dbSet
                .Where(r => r.RenderStatus == "Queued" || r.RenderStatus == "Processing")
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }
    }
}
