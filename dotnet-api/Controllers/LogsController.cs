using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers
{
    [ApiController]
    [Route("api")]
    [Authorize]
    public class LogsController : ControllerBase
    {
        private readonly ILogService _logService;

        public LogsController(ILogService logService)
        {
            _logService = logService;
        }

        [HttpGet("logs")]
        public async Task<ActionResult<PaginatedResult<EventLog>>> GetLogs([FromQuery] LogFilterParams filter)
        {
            var result = await _logService.GetLogsAsync(filter);
            return Ok(result);
        }

        [HttpPost("logs")]
        public async Task<IActionResult> CreateLog([FromBody] CreateLogDto dto)
        {
            try
            {
                var log = await _logService.CreateLogAsync(dto);
                return Ok(log);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>MReq 22: Export secure audit event logs as downloadable CSV</summary>
        [HttpGet("usage/csv")]
        public async Task<IActionResult> DownloadUsageCsv()
        {
            var filter = new LogFilterParams { PageSize = 10000 }; // fetch all for CSV export
            var result = await _logService.GetLogsAsync(filter);
            var sb = new StringBuilder();
            sb.AppendLine("ID,Timestamp,EventCode,Severity,Module,User,Description");
            foreach (var log in result.Items)
            {
                var desc = log.Description.Replace("\"", "\"\"");
                sb.AppendLine($"{log.Id},{log.Timestamp:O},{log.EventCode},{log.Severity},{log.Module},{log.User},\"{desc}\"");
            }
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"afrobotics_audit_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
        }
    }
}
