using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
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
    private readonly IEventLogService _eventLog;

    public JobsController(PostgresDbContext context, IEventLogService eventLog)
    {
        _context = context;
        _eventLog = eventLog;
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
            catch (Exception ex)
            {
                Debug.WriteLine($"[JobsController] Failed to delete Hangfire job {content.DetectionJobId}: {ex.Message}");
            }
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

    /// <summary>
    /// Recurring job (registered in Program.cs): Hangfire leaves a job's state as "Processing"
    /// forever if the server running it is killed non-gracefully (a dev restart, a crash, a
    /// force-kill) — there's no built-in reaping for this. This scans for "Processing" jobs
    /// whose owning server no longer exists and marks their DB records Failed instead of
    /// leaving them silently stuck.
    /// </summary>
    public async Task ReapOrphanedJobsAsync()
    {
        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        var liveServerIds = monitoringApi.Servers().Select(s => s.Name).ToHashSet();
        var processingJobs = monitoringApi.ProcessingJobs(0, 500);
        var cutoff = DateTime.UtcNow.AddMinutes(-1); // grace period for jobs that just started

        foreach (var entry in processingJobs)
        {
            var jobId = entry.Key;
            var jobData = entry.Value;
            if (jobData?.ServerId == null || liveServerIds.Contains(jobData.ServerId))
                continue; // still owned by a live server — genuinely running
            if (jobData.StartedAt.HasValue && jobData.StartedAt.Value > cutoff)
                continue; // too recent — server may not have registered its heartbeat yet

            var methodName = jobData.Job?.Method?.Name;
            var args = jobData.Job?.Args;
            const string orphanMessage = "Job orphaned by a server restart — please retry.";

            try
            {
                switch (methodName)
                {
                    case nameof(SceneDetectionJobService.RunDetectionPipeline):
                    {
                        var contentId = args?.ElementAtOrDefault(0) as string;
                        var content = await _context.ContentItems.FindAsync(contentId);
                        // Guard against clobbering a legitimate newer job: if the content has
                        // since been reset/re-triggered, its DetectionJobId no longer matches
                        // this stale job, even though IngestionStatus is still SceneDetecting
                        // for both (same status, different job) — only touch it if this dead
                        // job is still the one actually on record for it.
                        if (content != null && content.IngestionStatus == PipelineStages.SceneDetecting
                            && content.DetectionJobId == jobId)
                        {
                            content.IngestionStatus = PipelineStages.Failed;
                            content.JobState = "Failed";
                            content.LastErrorMessage = orphanMessage;
                            content.LastErrorAt = DateTime.UtcNow;
                            await _context.SaveChangesAsync();
                            await _eventLog.LogEventAsync("BackgroundJobs", "ORPHANED_JOB_REAPED", "Warning",
                                $"Detection job {jobId} for content {contentId} was orphaned by a server restart and marked Failed.");
                        }
                        break;
                    }
                    case nameof(SceneDetectionJobService.RunSceneSurfaceDetection):
                    {
                        var sceneId = args?.ElementAtOrDefault(0) as string;
                        var scene = await _context.SceneItems.FindAsync(sceneId);
                        if (scene != null && scene.SurfaceStatus == "Detecting")
                        {
                            scene.SurfaceStatus = "Failed";
                            await _context.SaveChangesAsync();
                            await _eventLog.LogEventAsync("BackgroundJobs", "ORPHANED_JOB_REAPED", "Warning",
                                $"Surface detection job {jobId} for scene {sceneId} was orphaned by a server restart and marked Failed.");
                        }
                        break;
                    }
                    case nameof(RenderJobService.ProcessPlanarRenderJob):
                    case nameof(RenderJobService.ProcessGenerativeRenderJob):
                    case nameof(RenderJobService.ProcessPromptPreviewJob):
                    case nameof(RenderJobService.ProcessPromptSpliceJob):
                    {
                        var renderId = args?.ElementAtOrDefault(0) as string;
                        var render = await _context.Renders.FindAsync(renderId);
                        if (render != null && render.RenderStatus == "Processing")
                        {
                            render.RenderStatus = "Failed";
                            render.LastErrorMessage = orphanMessage;
                            await _context.SaveChangesAsync();
                            await _eventLog.LogEventAsync("BackgroundJobs", "ORPHANED_JOB_REAPED", "Warning",
                                $"Render job {jobId} for render {renderId} was orphaned by a server restart and marked Failed.");
                        }
                        break;
                    }
                    // GenerateProxyAsync / email sends etc. have no polled DB status to reset —
                    // deleting the zombie Hangfire entry below is enough.
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JobsController] Failed to reap orphaned job {jobId}: {ex.Message}");
            }

            try { BackgroundJob.Delete(jobId); } catch { /* best effort */ }
        }
    }
}
