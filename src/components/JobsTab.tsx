import React, { useState, useEffect, useCallback } from 'react';
import { motion } from 'motion/react';
import {
  Cpu,
  Play,
  Pause,
  Square,
  RefreshCw,
  Clock,
  AlertTriangle,
  CheckCircle,
  XCircle,
  Circle,
  Loader2,
} from 'lucide-react';
import { DetectionJob, JobsListResponse } from '../types';
import { getJobs, stopJob, pauseJob, resumeJob } from '../apiClient';

interface JobsTabProps {
  /** Refresh content list in App when a job completes/fails (cascade refresh) */
  onJobChanged?: () => void;
}

/** Map job states to colors and icons for visual display */
const STATE_STYLE: Record<string, { color: string; bg: string; icon: React.FC<{ className?: string }> }> = {
  Enqueued:    { color: 'text-slate-500', bg: 'bg-slate-50', icon: Circle },
  Processing:  { color: 'text-brand-600',  bg: 'bg-brand-50',  icon: Loader2 },
  Paused:      { color: 'text-amber-600', bg: 'bg-amber-50', icon: Pause },
  Succeeded:   { color: 'text-emerald-600', bg: 'bg-emerald-50', icon: CheckCircle },
  Failed:      { color: 'text-red-600',    bg: 'bg-red-50',    icon: XCircle },
  Cancelled:   { color: 'text-slate-400',  bg: 'bg-slate-50',  icon: XCircle },
  SceneDetecting: { color: 'text-brand-600', bg: 'bg-brand-50',  icon: Loader2 },
  Completed:   { color: 'text-emerald-600', bg: 'bg-emerald-50', icon: CheckCircle },
};

export const JobsTab: React.FC<JobsTabProps> = ({ onJobChanged }) => {
  const [jobs, setJobs] = useState<DetectionJob[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionLoading, setActionLoading] = useState<string | null>(null); // jobId being acted on

  const fetchJobs = useCallback(async () => {
    try {
      const data: JobsListResponse = await getJobs();
      setJobs(data.data || []);
      setError(null);
    } catch (err: any) {
      setError(err.message || 'Failed to fetch jobs.');
    } finally {
      setLoading(false);
    }
  }, []);

  // Initial fetch + auto-refresh every 5 seconds while there are active jobs
  useEffect(() => {
    fetchJobs();
    const hasActiveJobs = jobs.some(j =>
      j.state === 'Processing' || j.state === 'Enqueued' || j.state === 'Paused' || j.state === 'SceneDetecting'
    );
    const interval = setInterval(() => {
      fetchJobs();
    }, hasActiveJobs ? 5_000 : 15_000);
    return () => clearInterval(interval);
  }, [fetchJobs, jobs]);

  const handleStop = async (jobId: string) => {
    setActionLoading(jobId);
    try {
      await stopJob(jobId);
      await fetchJobs();
      onJobChanged?.();
    } catch (err: any) {
      setError(err.message);
    } finally {
      setActionLoading(null);
    }
  };

  const handlePause = async (jobId: string) => {
    setActionLoading(jobId);
    try {
      await pauseJob(jobId);
      await fetchJobs();
    } catch (err: any) {
      setError(err.message);
    } finally {
      setActionLoading(null);
    }
  };

  const handleResume = async (jobId: string) => {
    setActionLoading(jobId);
    try {
      await resumeJob(jobId);
      await fetchJobs();
    } catch (err: any) {
      setError(err.message);
    } finally {
      setActionLoading(null);
    }
  };

  const activeCount = jobs.filter(j =>
    j.state === 'Processing' || j.state === 'Enqueued' || j.state === 'Paused' || j.state === 'SceneDetecting'
  ).length;

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      className="space-y-6"
      key="jobs_tab"
    >
      {/* Header */}
      <div className="bg-gradient-to-r from-violet-600 to-purple-700 rounded-2xl p-6 text-white shadow-lg">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-bold">Background Detection Jobs</h2>
            <p className="text-sm text-violet-100 mt-1">
              Monitor, pause, resume, and cancel AI scene detection jobs
            </p>
          </div>
          <button
            onClick={fetchJobs}
            className="p-2 rounded-lg bg-white/10 hover:bg-white/20 transition-colors"
            title="Refresh job list"
          >
            <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
        </div>
        {activeCount > 0 && (
          <div className="mt-3 flex items-center gap-2 text-sm text-violet-100">
            <Cpu className="h-4 w-4" />
            <span>{activeCount} active job{activeCount !== 1 ? 's' : ''}</span>
          </div>
        )}
      </div>

      {/* Error banner */}
      {error && (
        <div className="bg-red-50 border border-red-200 rounded-xl p-4 flex items-start gap-3">
          <AlertTriangle className="h-5 w-5 text-red-500 mt-0.5 flex-shrink-0" />
          <div>
            <p className="text-sm font-medium text-red-800">Error</p>
            <p className="text-sm text-red-600 mt-0.5">{error}</p>
          </div>
          <button
            onClick={() => setError(null)}
            className="ml-auto p-1 text-red-400 hover:text-red-600"
          >
            <XCircle className="h-4 w-4" />
          </button>
        </div>
      )}

      {/* Loading state */}
      {loading && jobs.length === 0 && (
        <div className="flex items-center justify-center py-20">
          <div className="animate-spin h-8 w-8 border-2 border-violet-500 border-t-transparent rounded-full" />
        </div>
      )}

      {/* Empty state */}
      {!loading && jobs.length === 0 && (
        <div className="text-center py-16 bg-white rounded-2xl border border-slate-200">
          <Cpu className="h-12 w-12 text-slate-300 mx-auto mb-3" />
          <p className="text-slate-500 font-medium">No detection jobs found</p>
          <p className="text-sm text-slate-400 mt-1">
            Scene detection jobs will appear here when you run detection on a video.
          </p>
        </div>
      )}

      {/* Job list */}
      {jobs.length > 0 && (
        <div className="space-y-3">
          {jobs.map(job => {
            const style = STATE_STYLE[job.state] || STATE_STYLE.Processing;
            const isActive = job.state === 'Processing' || job.state === 'Enqueued' ||
                             job.state === 'Paused' || job.state === 'SceneDetecting';
            const isPaused = job.isPaused || job.state === 'Paused';
            const isLoading = actionLoading === (job.jobId || job.contentId);

            return (
              <div
                key={job.jobId || job.contentId}
                className="bg-white rounded-xl border border-slate-200 shadow-sm p-5 hover:shadow-md transition-shadow"
              >
                <div className="flex items-start justify-between gap-4">
                  {/* Left: Job info */}
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2 mb-1">
                      <style.icon className={`h-4 w-4 ${style.color} ${job.state === 'Processing' || job.state === 'SceneDetecting' ? 'animate-spin' : ''}`} />
                      <span className={`text-xs font-mono uppercase tracking-wider px-2 py-0.5 rounded-full ${style.bg} ${style.color}`}>
                        {job.state}
                      </span>
                      {isPaused && (
                        <span className="text-xs font-mono uppercase tracking-wider px-2 py-0.5 rounded-full bg-amber-100 text-amber-700">
                          Paused
                        </span>
                      )}
                    </div>
                    <h3 className="font-semibold text-slate-800 truncate" title={job.videoTitle}>{job.videoTitle}</h3>
                    <p className="text-xs text-slate-400 font-mono mt-0.5">{job.contentId}</p>

                    {/* Progress bar */}
                    {isActive && (
                      <div className="mt-3">
                        <div className="flex items-center justify-between text-xs text-slate-500 mb-1">
                          <span>Progress</span>
                          <span>{job.progress}%</span>
                        </div>
                        <div className="w-full bg-slate-100 rounded-full h-2 overflow-hidden">
                          <motion.div
                            className={`h-full rounded-full ${isPaused ? 'bg-amber-400' : 'bg-gradient-to-r from-violet-500 to-purple-600'}`}
                            initial={{ width: 0 }}
                            animate={{ width: `${job.progress}%` }}
                            transition={{ duration: 0.5, ease: 'easeOut' }}
                          />
                        </div>
                      </div>
                    )}

                    {/* Timestamps */}
                    <div className="flex flex-wrap gap-3 mt-2 text-xs text-slate-400">
                      {job.startedAt && (
                        <span className="flex items-center gap-1">
                          <Clock className="h-3 w-3" />
                          Started: {new Date(job.startedAt).toLocaleString()}
                        </span>
                      )}
                      {job.completedAt && (
                        <span className="flex items-center gap-1">
                          <CheckCircle className="h-3 w-3 text-emerald-500" />
                          Completed: {new Date(job.completedAt).toLocaleString()}
                        </span>
                      )}
                    </div>

                    {/* Error message for failed jobs */}
                    {job.state === 'Failed' && job.lastErrorMessage && (
                      <div className="mt-2 p-2 bg-red-50 border border-red-100 rounded-lg text-xs text-red-700 font-mono">
                        {job.lastErrorMessage}
                      </div>
                    )}
                  </div>

                  {/* Right: Actions */}
                  {isActive && (
                    <div className="flex items-center gap-2 flex-shrink-0">
                      {isPaused ? (
                        <button
                          onClick={() => handleResume(job.jobId || job.contentId)}
                          disabled={!!actionLoading}
                          className="p-2 rounded-lg bg-emerald-50 text-emerald-600 hover:bg-emerald-100 transition-colors disabled:opacity-50"
                          title="Resume job"
                        >
                          {isLoading ? (
                            <Loader2 className="h-4 w-4 animate-spin" />
                          ) : (
                            <Play className="h-4 w-4" />
                          )}
                        </button>
                      ) : (
                        <button
                          onClick={() => handlePause(job.jobId || job.contentId)}
                          disabled={!!actionLoading}
                          className="p-2 rounded-lg bg-amber-50 text-amber-600 hover:bg-amber-100 transition-colors disabled:opacity-50"
                          title="Pause job"
                        >
                          {isLoading ? (
                            <Loader2 className="h-4 w-4 animate-spin" />
                          ) : (
                            <Pause className="h-4 w-4" />
                          )}
                        </button>
                      )}
                      <button
                        onClick={() => handleStop(job.jobId || job.contentId)}
                        disabled={!!actionLoading}
                        className="p-2 rounded-lg bg-red-50 text-red-600 hover:bg-red-100 transition-colors disabled:opacity-50"
                        title="Stop job"
                      >
                        {isLoading ? (
                          <Loader2 className="h-4 w-4 animate-spin" />
                        ) : (
                          <Square className="h-4 w-4" />
                        )}
                      </button>
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </motion.div>
  );
};
