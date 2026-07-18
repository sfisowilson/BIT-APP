using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface IAlarmService
    {
        Task<IEnumerable<AlarmItem>> GetAlarmsAsync();
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

        public async Task<IEnumerable<AlarmItem>> GetAlarmsAsync()
        {
            return await _alarmRepository.GetAllAsync();
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
