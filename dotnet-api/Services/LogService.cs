using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface ILogService
    {
        Task<IEnumerable<EventLog>> GetLogsAsync();
        Task<EventLog> CreateLogAsync(CreateLogDto dto);
    }

    public class LogService : ILogService
    {
        private readonly ILogRepository _logRepository;

        public LogService(ILogRepository logRepository)
        {
            _logRepository = logRepository;
        }

        public async Task<IEnumerable<EventLog>> GetLogsAsync()
        {
            return await _logRepository.GetRecentLogsAsync(100);
        }

        public async Task<EventLog> CreateLogAsync(CreateLogDto dto)
        {
            var log = new EventLog
            {
                Id = "l-" + Guid.NewGuid().ToString().Substring(0, 4),
                Timestamp = DateTime.UtcNow,
                EventCode = dto.EventCode,
                Severity = dto.Severity,
                Module = dto.Module,
                User = dto.User,
                Description = dto.Description
            };

            await _logRepository.AddAsync(log);
            await _logRepository.SaveChangesAsync();

            return log;
        }
    }
}
