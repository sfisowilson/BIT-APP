import React, { useCallback } from 'react';
import { motion } from 'motion/react';
import { Activity, FileSpreadsheet, AlertTriangle, CheckCircle, Check, Bell, Search } from 'lucide-react';
import { EventLog, AlarmItem } from '../types';
import { usePaginatedData } from '../hooks/usePaginatedData';
import { Pagination } from './Pagination';
import { getToken } from '../apiClient';

interface TelemetryTabProps {
  handleClearAlarm: (id: string) => void;
  handleSimulateAlarm: (e: React.FormEvent) => void;
  alarmSimSeverity: "Minor" | "Major" | "Critical";
  setAlarmSimSeverity: (v: "Minor" | "Major" | "Critical") => void;
  alarmSimSource: string;
  setAlarmSimSource: (v: string) => void;
  alarmSimDesc: string;
  setAlarmSimDesc: (v: string) => void;
}

export const TelemetryTab: React.FC<TelemetryTabProps> = ({
  handleClearAlarm,
  handleSimulateAlarm,
  alarmSimSeverity,
  setAlarmSimSeverity,
  alarmSimSource,
  setAlarmSimSource,
  alarmSimDesc,
  setAlarmSimDesc,
}) => {
  // ── Logs pagination + filters ──
  const {
    data: logData,
    loading: logsLoading,
    error: logsError,
    page: logsPage,
    totalPages: logsTotalPages,
    totalCount: logsTotalCount,
    hasPreviousPage: logsHasPrev,
    hasNextPage: logsHasNext,
    setPage: setLogsPage,
    setFilters: setLogFilters,
    refresh: refreshLogs,
  } = usePaginatedData<EventLog>('/api/logs', {}, { defaultPageSize: 25 });

  const [logSeverityFilter, setLogSeverityFilter] = React.useState('');
  const [logSearchFilter, setLogSearchFilter] = React.useState('');

  const applyLogFilters = useCallback(() => {
    setLogFilters({
      severity: logSeverityFilter || undefined,
      search: logSearchFilter || undefined,
    });
  }, [logSeverityFilter, logSearchFilter, setLogFilters]);

  // Apply filters when they change
  React.useEffect(() => { applyLogFilters(); }, [logSeverityFilter, logSearchFilter]);

  // ── Alarms pagination + filters ──
  const {
    data: alarmData,
    page: alarmsPage,
    totalPages: alarmsTotalPages,
    totalCount: alarmsTotalCount,
    hasPreviousPage: alarmsHasPrev,
    hasNextPage: alarmsHasNext,
    setPage: setAlarmsPage,
    setFilters: setAlarmFilters,
    refresh: refreshAlarms,
  } = usePaginatedData<AlarmItem>('/api/alarms', {}, { defaultPageSize: 20 });

  const [alarmSeverityFilter, setAlarmSeverityFilter] = React.useState('');
  const [alarmActiveOnly, setAlarmActiveOnly] = React.useState(true);

  React.useEffect(() => {
    setAlarmFilters({
      severity: alarmSeverityFilter || undefined,
      isActive: alarmActiveOnly || undefined,
    });
  }, [alarmSeverityFilter, alarmActiveOnly]);

  // Refresh alarms after clearing one
  const wrappedClearAlarm = async (id: string) => {
    await handleClearAlarm(id);
    refreshAlarms();
  };

  // Refresh alarms after simulation
  const wrappedSimulateAlarm = async (e: React.FormEvent) => {
    await handleSimulateAlarm(e);
    refreshAlarms();
    refreshLogs();
  };

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -10 }}
      className="grid grid-cols-1 lg:grid-cols-3 gap-8"
      key="telemetry_tab"
    >
      {/* Informational guide */}
      <div className="lg:col-span-3 bg-brand-50 border border-brand-100 rounded-2xl p-5 text-xs text-brand-800 flex items-start gap-3 shadow-xs">
        <Activity className="h-5 w-5 text-brand-600 shrink-0 mt-0.5" />
        <div>
          <h4 className="font-bold text-sm text-brand-900">Step 5: Operational Monitoring, Alarms &amp; Security Logs</h4>
          <p className="mt-1 text-brand-700 leading-normal">
            This operations and maintenance tab allows real-time health inspection. View secure automated transaction event audit logs, download CSV compliance logs, and simulate or clear hardware alarms/fault triggers to ensure platform redundancy.
          </p>
        </div>
      </div>

      {/* Left col - Event logs stream (MReq 20) */}
      <div className="lg:col-span-2 space-y-6">
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-4 border-b border-slate-100 pb-4">
            <div>
              <h2 className="text-lg font-bold text-slate-800 font-display">System Event Audits</h2>
              <p className="text-xs text-slate-400 mt-0.5">{logsTotalCount} total logs</p>
            </div>
            <a
              href="/api/usage/csv"
              className="inline-flex items-center gap-2 px-3 py-1.5 bg-emerald-50 hover:bg-emerald-100 border border-emerald-200 text-emerald-700 text-xs font-semibold rounded-lg shadow-sm transition-all"
            >
              <FileSpreadsheet className="h-4 w-4" />
              Download CSV
            </a>
          </div>

          {/* Log filter bar */}
          <div className="flex flex-wrap items-center gap-2 mb-4">
            <select
              value={logSeverityFilter}
              onChange={e => setLogSeverityFilter(e.target.value)}
              className="bg-slate-50 border border-slate-200 rounded-lg px-2 py-1 text-[10px] text-slate-700 focus:outline-none focus:border-brand-400"
            >
              <option value="">All Severities</option>
              <option value="Info">Info</option>
              <option value="Warning">Warning</option>
              <option value="Major">Major</option>
              <option value="Critical">Critical</option>
            </select>
            <div className="relative flex-1 min-w-[150px]">
              <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-3 w-3 text-slate-400" />
              <input
                type="text"
                value={logSearchFilter}
                onChange={e => setLogSearchFilter(e.target.value)}
                placeholder="Search logs..."
                className="w-full bg-slate-50 border border-slate-200 rounded-lg pl-6 pr-2 py-1 text-[10px] text-slate-700 focus:outline-none focus:border-brand-400"
              />
            </div>
          </div>

          {/* Logs stream */}
          {logsLoading ? (
            <div className="text-center py-8 text-xs text-slate-400">Loading logs...</div>
          ) : logsError ? (
            <div className="text-center py-8 text-xs text-red-500">{logsError}</div>
          ) : (
            <div className="bg-slate-900 rounded-xl p-4 border border-slate-800 font-mono text-[10px] space-y-3 max-h-[400px] overflow-y-auto scrollbar-thin">
              {logData.map(log => (
                <div key={log.id} className="border-b border-slate-800/60 pb-2.5 last:border-0 last:pb-0 flex items-start gap-3">
                  <span className={`px-1.5 py-0.5 rounded text-[8px] font-bold shrink-0 mt-0.5 ${
                    log.severity === 'Critical' ? 'bg-red-500/10 text-red-400' :
                    log.severity === 'Warning' ? 'bg-yellow-500/10 text-yellow-400' :
                    'bg-brand-500/10 text-brand-400'
                  }`}>{log.severity}</span>
                  <div className="space-y-1">
                    <div className="flex flex-wrap items-center gap-2 text-slate-500">
                      <span className="text-slate-300 font-bold">[{log.eventCode}]</span>
                      <span>{new Date(log.timestamp).toLocaleString()}</span>
                      <span>• Module: {log.module}</span>
                      <span>• User: {log.user}</span>
                    </div>
                    <p className="text-slate-300 leading-normal font-medium">{log.description}</p>
                  </div>
                </div>
              ))}
              {logData.length === 0 && (
                <div className="text-center py-4 text-slate-500">No log entries match your filters.</div>
              )}
            </div>
          )}

          <Pagination
            page={logsPage}
            totalPages={logsTotalPages}
            hasPreviousPage={logsHasPrev}
            hasNextPage={logsHasNext}
            onPageChange={setLogsPage}
          />
        </div>
      </div>

      {/* Alarms Workbench, simulation, minor/major triggers (MReq 21) */}
      <div className="col-span-1 space-y-6">
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <h3 className="text-sm font-bold uppercase tracking-wider text-slate-500 mb-4 font-display">
            Alarm Hubs • {alarmsTotalCount}
          </h3>

          {/* Alarm filter bar */}
          <div className="flex items-center gap-2 mb-3">
            <select
              value={alarmSeverityFilter}
              onChange={e => setAlarmSeverityFilter(e.target.value)}
              className="bg-slate-50 border border-slate-200 rounded-lg px-2 py-1 text-[10px] text-slate-700 focus:outline-none flex-1"
            >
              <option value="">All Severities</option>
              <option value="Minor">Minor</option>
              <option value="Major">Major</option>
              <option value="Critical">Critical</option>
            </select>
            <label className="flex items-center gap-1 text-[10px] text-slate-600 cursor-pointer shrink-0">
              <input
                type="checkbox"
                checked={alarmActiveOnly}
                onChange={e => setAlarmActiveOnly(e.target.checked)}
                className="rounded"
              />
              Active only
            </label>
          </div>
          
          <div className="space-y-3">
            {alarmData.map(alarm => (
              <div key={alarm.id} className={`border rounded-xl p-4 flex flex-col justify-between space-y-3 ${alarm.isActive ? 'bg-red-50/50 border-red-200' : 'bg-slate-50 border-slate-200'}`}>
                <div>
                  <div className="flex items-center justify-between text-2xs font-mono text-red-600">
                    <span className="font-bold flex items-center gap-1">
                      {alarm.isActive && <AlertTriangle className="h-3 w-3 animate-pulse" />}
                      {alarm.severity} {alarm.isActive ? 'ALARM' : '(cleared)'}
                    </span>
                    <span>{new Date(alarm.timestamp).toLocaleTimeString()}</span>
                  </div>
                  <h4 className="text-xs font-bold text-slate-800 mt-1.5 font-display">Source: {alarm.source}</h4>
                  <p className="text-xs text-slate-500 leading-normal mt-1">{alarm.description}</p>
                </div>
                {alarm.isActive && (
                  <button 
                    onClick={() => wrappedClearAlarm(alarm.id)}
                    className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-1 bg-red-100 hover:bg-red-200 border border-red-200 text-red-700 font-semibold text-xs rounded-lg transition-all cursor-pointer shadow-xs"
                  >
                    <Check className="h-3.5 w-3.5" />
                    Clear Active Alarm
                  </button>
                )}
              </div>
            ))}
            {alarmData.length === 0 && (
              <div className="p-4 rounded-xl border border-emerald-200 bg-emerald-50 text-center text-emerald-800 text-xs">
                <CheckCircle className="h-5 w-5 mx-auto mb-2 text-emerald-600" />
                All systems operational. Zero active alarms in corporate streams.
              </div>
            )}
          </div>

          <Pagination
            page={alarmsPage}
            totalPages={alarmsTotalPages}
            hasPreviousPage={alarmsHasPrev}
            hasNextPage={alarmsHasNext}
            onPageChange={setAlarmsPage}
          />
        </div>

        {/* Alarm Simulator Form */}
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500 mb-3 font-display">Test Alarm Trigger</h3>
          <form onSubmit={wrappedSimulateAlarm} className="space-y-3 font-mono text-xs">
            <div>
              <label className="block text-[9px] uppercase tracking-wider font-bold text-slate-500 mb-1">Severity</label>
              <select 
                value={alarmSimSeverity} 
                onChange={(e) => setAlarmSimSeverity(e.target.value as any)}
                className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1 text-xs text-slate-800 focus:outline-none"
              >
                <option value="Minor">Minor Warning</option>
                <option value="Major">Major Incident</option>
                <option value="Critical">Critical Failure</option>
              </select>
            </div>
            <div>
              <label className="block text-[9px] uppercase tracking-wider font-bold text-slate-500 mb-1">Alarm Source</label>
              <input 
                type="text" 
                value={alarmSimSource} 
                onChange={(e) => setAlarmSimSource(e.target.value)} 
                className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1 text-xs text-slate-800"
                required
              />
            </div>
            <div>
              <label className="block text-[9px] uppercase tracking-wider font-bold text-slate-500 mb-1">Description</label>
              <textarea 
                value={alarmSimDesc} 
                onChange={(e) => setAlarmSimDesc(e.target.value)} 
                className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1 text-xs text-slate-800 h-16 focus:outline-none"
                required
              />
            </div>
            <button 
              type="submit" 
              className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-1.5 bg-red-600 hover:bg-red-500 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer"
            >
              <Bell className="h-3.5 w-3.5" />
              Dispatch Alarm
            </button>
          </form>
        </div>
      </div>
    </motion.div>
  );
};
