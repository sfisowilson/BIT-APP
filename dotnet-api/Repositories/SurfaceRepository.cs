using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Repositories
{
    public interface ISurfaceRepository : IRepository<SurfaceItem>
    {
        Task<IEnumerable<SurfaceItem>> GetSurfacesBySceneIdAsync(string sceneId);
        Task AddAdSlotAsync(AdSlotItem adSlot);
        Task AddApprovalAsync(ApprovalItem approval);
    }

    public class SurfaceRepository : Repository<SurfaceItem>, ISurfaceRepository
    {
        public SurfaceRepository(PostgresDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<SurfaceItem>> GetSurfacesBySceneIdAsync(string sceneId)
        {
            return await _dbSet
                .Where(s => s.SceneId == sceneId)
                .ToListAsync();
        }

        public async Task AddAdSlotAsync(AdSlotItem adSlot)
        {
            await _context.AdSlots.AddAsync(adSlot);
        }

        public async Task AddApprovalAsync(ApprovalItem approval)
        {
            await _context.Approvals.AddAsync(approval);
        }
    }
}
