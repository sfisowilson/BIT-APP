using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Repositories
{
    public interface IAlarmRepository : IRepository<AlarmItem>
    {
        Task<IEnumerable<AlarmItem>> GetActiveAlarmsAsync();
    }

    public class AlarmRepository : Repository<AlarmItem>, IAlarmRepository
    {
        public AlarmRepository(PostgresDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<AlarmItem>> GetActiveAlarmsAsync()
        {
            return await _dbSet
                .Where(a => a.IsActive)
                .OrderByDescending(a => a.Timestamp)
                .ToListAsync();
        }
    }
}
