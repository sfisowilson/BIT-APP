using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Repositories
{
    public interface IContentRepository : IRepository<ContentItem>
    {
        Task<IEnumerable<SceneItem>> GetScenesByContentIdAsync(string contentId);
        Task AddSceneAsync(SceneItem scene);
    }

    public class ContentRepository : Repository<ContentItem>, IContentRepository
    {
        public ContentRepository(PostgresDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<SceneItem>> GetScenesByContentIdAsync(string contentId)
        {
            return await _context.SceneItems
                .Where(s => s.ContentId == contentId)
                .OrderBy(s => s.SceneIndex)
                .ToListAsync();
        }

        public async Task AddSceneAsync(SceneItem scene)
        {
            await _context.SceneItems.AddAsync(scene);
        }
    }
}
