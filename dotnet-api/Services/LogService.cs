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
        Task<PaginatedResult<EventLog>> GetLogsAsync(LogFilterParams filter);
        Task<EventLog> CreateLogAsync(CreateLogDto dto);
    }

    public class LogService : ILogService
    {
        private readonly ILogRepository _logRepository;

        public LogService(ILogRepository logRepository)
        {
            _logRepository = logRepository;
        }

        public async Task<PaginatedResult<EventLog>> GetLogsAsync(LogFilterParams filter)
        {
            var query = _logRepository.GetAllQueryable();

            if (!string.IsNullOrEmpty(filter.Severity))
                query = query.Where(l => l.Severity == filter.Severity);
            if (!string.IsNullOrEmpty(filter.Module))
                query = query.Where(l => l.Module == filter.Module);
            if (filter.DateFrom.HasValue)
                query = query.Where(l => l.Timestamp >= filter.DateFrom.Value);
            if (filter.DateTo.HasValue)
                query = query.Where(l => l.Timestamp <= filter.DateTo.Value);
            if (!string.IsNullOrEmpty(filter.Search))
                query = query.Where(l => l.Description.Contains(filter.Search) || l.EventCode.Contains(filter.Search));

            if (!string.IsNullOrEmpty(filter.SortBy))
                query = query.ApplySort(filter.SortBy, filter.SortDescending);
            else
                query = query.OrderByDescending(l => l.Timestamp);

            return await query.ToPaginatedResultAsync(filter.Page, filter.PageSize);
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
