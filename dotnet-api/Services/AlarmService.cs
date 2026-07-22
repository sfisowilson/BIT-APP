using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface IAlarmService
    {
        Task<PaginatedResult<AlarmItem>> GetAlarmsAsync(AlarmFilterParams filter);
        Task<AlarmItem?> ClearAlarmAsync(string id);
        Task<AlarmItem> CreateAlarmAsync(AlarmItem alarm);
    }

    public class AlarmService : IAlarmService
    {
        private readonly IAlarmRepository _alarmRepository;

        public AlarmService(IAlarmRepository alarmRepository)
        {
            _alarmRepository = alarmRepository;
        }

        public async Task<PaginatedResult<AlarmItem>> GetAlarmsAsync(AlarmFilterParams filter)
        {
            var query = _alarmRepository.GetAllQueryable();

            if (!string.IsNullOrEmpty(filter.Severity))
                query = query.Where(a => a.Severity == filter.Severity);
            if (filter.IsActive.HasValue)
                query = query.Where(a => a.IsActive == filter.IsActive.Value);

            if (!string.IsNullOrEmpty(filter.SortBy))
                query = query.ApplySort(filter.SortBy, filter.SortDescending);
            else
                query = query.OrderByDescending(a => a.Timestamp);

            return await query.ToPaginatedResultAsync(filter.Page, filter.PageSize);
        }

        public async Task<AlarmItem?> ClearAlarmAsync(string id)
        {
            var alarm = await _alarmRepository.GetByIdAsync(id);
            if (alarm == null) return null;

            alarm.IsActive = false;
            await _alarmRepository.UpdateAsync(alarm);
            await _alarmRepository.SaveChangesAsync();

            return alarm;
        }

        public async Task<AlarmItem> CreateAlarmAsync(AlarmItem alarm)
        {
            alarm.Id = "al-" + Guid.NewGuid().ToString().Substring(0, 4);
            alarm.Timestamp = DateTime.UtcNow;
            alarm.IsActive = true;

            await _alarmRepository.AddAsync(alarm);
            await _alarmRepository.SaveChangesAsync();

            return alarm;
        }
    }
}
