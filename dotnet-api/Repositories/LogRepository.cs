using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Repositories
{
    public interface ILogRepository : IRepository<EventLog>
    {
        Task<IEnumerable<EventLog>> GetRecentLogsAsync(int count = 50);
    }

    public class LogRepository : Repository<EventLog>, ILogRepository
    {
        public LogRepository(PostgresDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<EventLog>> GetRecentLogsAsync(int count = 50)
        {
            return await _dbSet
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToListAsync();
        }
    }
}
