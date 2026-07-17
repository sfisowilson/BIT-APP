using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers
{
    [ApiController]
    [Route("api/alarms")]
    [Authorize]
    public class AlarmsController : ControllerBase
    {
        private readonly IAlarmService _alarmService;

        public AlarmsController(IAlarmService alarmService)
        {
            _alarmService = alarmService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<AlarmItem>>> GetAlarms()
        {
            var alarms = await _alarmService.GetAlarmsAsync();
            return Ok(alarms);
        }

        [HttpPost("{id}/clear")]
        public async Task<IActionResult> ClearAlarm(string id)
        {
            try
            {
                var alarm = await _alarmService.ClearAlarmAsync(id);
                if (alarm == null)
                {
                    return NotFound(new { error = "Alarm not found" });
                }
                return Ok(alarm);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
