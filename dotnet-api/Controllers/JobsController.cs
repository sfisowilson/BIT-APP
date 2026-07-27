using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers;

/// <summary>
/// Controller for viewing, stopping, pausing, and resuming background scene detection jobs.
/// </summary>
[ApiController]
[Route("api/jobs")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly PostgresDbContext _context;

    public JobsController(PostgresDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// View all background scene detection jobs and their state.
    /// Combines DB ContentItem metadata with Hangfire monitoring API state.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetJobs()
    {
        var contentsWithJobs = await _context.ContentItems
            .Where(c => c.DetectionJobId != null || c.IngestionStatus == PipelineStages.SceneDetecting || c.IsDetectionPaused)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        var resultList = new List<object>();

        foreach (var c in contentsWithJobs)
        {
            var jobId = c.DetectionJobId;
            string state = c.JobState ?? (c.IsDetectionPaused ? "Paused" : c.IngestionStatus);
            DateTime? createdAt = c.SceneDetectingStartedAt ?? c.CreatedAt;

            if (!string.IsNullOrEmpty(jobId))
            {
                var jobData = monitoringApi.JobDetails(jobId);
                if (jobData != null)
                {
                    var currentState = jobData.History.FirstOrDefault()?.StateName;
                    if (!c.IsDetectionPaused && !string.IsNullOrEmpty(currentState))
                    {
                        state = currentState switch
                        {
                            "Processing" => "Processing",
                            "Enqueued" => "Enqueued",
                            "Succeeded" => "Succeeded",
                            "Failed" => "Failed",
                            "Deleted" => "Cancelled",
                            _ => currentState
                        };
                    }
                }
            }

            resultList.Add(new
            {
                jobId = c.DetectionJobId,
                contentId = c.Id,
                videoTitle = c.Title,
                state = state,
                isPaused = c.IsDetectionPaused,
                progress = c.DetectionProgress,
                ingestionStatus = c.IngestionStatus,
                startedAt = c.SceneDetectingStartedAt,
                completedAt = c.SceneDetectingCompletedAt,
                lastErrorMessage = c.LastErrorMessage
            });
        }

        return Ok(new { data = resultList, count = resultList.Count });
    }

    /// <summary>
    /// Stop/Cancel a background job by Job ID or Content ID.
    /// </summary>
    [HttpPost("{jobId}/stop")]
    public async Task<IActionResult> StopJob(string jobId)
    {
        var content = await _context.ContentItems
            .FirstOrDefaultAsync(c => c.DetectionJobId == jobId || c.Id == jobId);

        if (content == null)
            return NotFound(new { error = "Job or content not found." });

        if (!string.IsNullOrEmpty(content.DetectionJobId))
        {
            try
            {
                BackgroundJob.Delete(content.DetectionJobId);
            }
            catch { }
        }

        content.IsDetectionPaused = false;
        content.JobState = "Cancelled";
        content.IngestionStatus = PipelineStages.Failed;
        content.LastErrorMessage = "Job stopped by user.";
        content.LastErrorAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            jobId = content.DetectionJobId,
            contentId = content.Id,
            message = "Job successfully stopped."
        });
    }

    /// <summary>
    /// Pause a background job by Job ID or Content ID.
    /// </summary>
    [HttpPost("{jobId}/pause")]
    public async Task<IActionResult> PauseJob(string jobId)
    {
        var content = await _context.ContentItems
            .FirstOrDefaultAsync(c => c.DetectionJobId == jobId || c.Id == jobId);

        if (content == null)
            return NotFound(new { error = "Job or content not found." });

        content.IsDetectionPaused = true;
        content.JobState = "Paused";
        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            jobId = content.DetectionJobId,
            contentId = content.Id,
            message = "Job paused. Scene processing will pause safely."
        });
    }

    /// <summary>
    /// Resume a paused background job by Job ID or Content ID.
    /// </summary>
    [HttpPost("{jobId}/resume")]
    public async Task<IActionResult> ResumeJob(string jobId)
    {
        var content = await _context.ContentItems
            .FirstOrDefaultAsync(c => c.DetectionJobId == jobId || c.Id == jobId);

        if (content == null)
            return NotFound(new { error = "Job or content not found." });

        content.IsDetectionPaused = false;
        content.JobState = "Processing";
        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            jobId = content.DetectionJobId,
            contentId = content.Id,
            message = "Job resumed. Scene processing will continue."
        });
    }
}
