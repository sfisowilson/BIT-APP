import React from 'react';
import { motion } from 'motion/react';
import { Activity, FileSpreadsheet, AlertTriangle, CheckCircle, Check, Bell } from 'lucide-react';
import { EventLog, AlarmItem } from '../types';

interface TelemetryTabProps {
  logList: EventLog[];
  alarmList: AlarmItem[];
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
  logList,
  alarmList,
  handleClearAlarm,
  handleSimulateAlarm,
  alarmSimSeverity,
  setAlarmSimSeverity,
  alarmSimSource,
  setAlarmSimSource,
  alarmSimDesc,
  setAlarmSimDesc,
}) => {
  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -10 }}
      className="grid grid-cols-1 lg:grid-cols-3 gap-8"
      key="telemetry_tab"
    >
      {/* Informational guide */}
      <div className="lg:col-span-3 bg-blue-50 border border-blue-100 rounded-2xl p-5 text-xs text-blue-800 flex items-start gap-3 shadow-xs">
        <Activity className="h-5 w-5 text-blue-600 shrink-0 mt-0.5" />
        <div>
          <h4 className="font-bold text-sm text-blue-900">Step 5: Operational Monitoring, Alarms &amp; Security Logs</h4>
          <p className="mt-1 text-blue-700 leading-normal">
            This operations and maintenance tab allows real-time health inspection. View secure automated transaction event audit logs (<strong>MReq 20</strong>), download CSV compliance logs (<strong>MReq 22</strong>), and simulate or clear hardware alarms/fault triggers to ensure platform redundancy (<strong>MReq 21</strong>).
          </p>
        </div>
      </div>

      {/* Left col - Event logs stream (MReq 20) */}
      <div className="lg:col-span-2 space-y-6">
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-6 border-b border-slate-100 pb-4">
            <div>
              <h2 className="text-lg font-bold text-slate-800 font-display">System Event Audits</h2>
              <p className="text-xs text-slate-400 mt-0.5">MReq 20 Compliance: secure event logs recording functional modifications and task alerts.</p>
            </div>

            <a 
              href="/api/usage/csv" 
              className="inline-flex items-center gap-2 px-3 py-1.5 bg-emerald-50 hover:bg-emerald-100 border border-emerald-200 text-emerald-700 text-xs font-semibold rounded-lg shadow-sm transition-all"
              id="download_csv_button"
            >
              <FileSpreadsheet className="h-4 w-4" />
              Download Secure Audit CSV (MReq 22)
            </a>
          </div>

          {/* Logs stream container - formatted with clean readable dark layout like a high-end cloud provider console */}
          <div className="bg-slate-900 rounded-xl p-4 border border-slate-800 font-mono text-[10px] space-y-3 max-h-[450px] overflow-y-auto scrollbar-thin">
            {logList.map(log => (
              <div key={log.id} className="border-b border-slate-800/60 pb-2.5 last:border-0 last:pb-0 flex items-start gap-3">
                <span className={`px-1.5 py-0.5 rounded text-[8px] font-bold shrink-0 mt-0.5 ${
                  log.severity === 'Critical' ? 'bg-red-500/10 text-red-400' :
                  log.severity === 'Warning' ? 'bg-yellow-500/10 text-yellow-400' :
                  'bg-blue-500/10 text-blue-400'
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
          </div>
        </div>
      </div>

      {/* Alarms Workbench, simulation, minor/major triggers (MReq 21) */}
      <div className="col-span-1 space-y-6">
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <h3 className="text-sm font-bold uppercase tracking-wider text-slate-500 mb-4 font-display">Active Alarm Hubs</h3>
          
          <div className="space-y-3">
            {alarmList.filter(a => a.isActive).map(alarm => (
              <div key={alarm.id} className="bg-red-50/50 border border-red-200 rounded-xl p-4 flex flex-col justify-between space-y-3" id={`active_alarm_${alarm.id}`}>
                <div>
                  <div className="flex items-center justify-between text-2xs font-mono text-red-600">
                    <span className="font-bold flex items-center gap-1">
                      <AlertTriangle className="h-3 w-3 animate-pulse" />
                      {alarm.severity} ALARM
                    </span>
                    <span>{new Date(alarm.timestamp).toLocaleTimeString()}</span>
                  </div>
                  <h4 className="text-xs font-bold text-slate-800 mt-1.5 font-display">Source: {alarm.source}</h4>
                  <p className="text-xs text-slate-500 leading-normal mt-1">{alarm.description}</p>
                </div>
                <button 
                  onClick={() => handleClearAlarm(alarm.id)}
                  className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-1 bg-red-100 hover:bg-red-200 border border-red-200 text-red-700 font-semibold text-xs rounded-lg transition-all cursor-pointer shadow-xs"
                >
                  <Check className="h-3.5 w-3.5" />
                  Clear Active Alarm (Generate log)
                </button>
              </div>
            ))}

            {alarmList.filter(a => a.isActive).length === 0 && (
              <div className="p-4 rounded-xl border border-emerald-200 bg-emerald-50 text-center text-emerald-800 text-xs">
                <CheckCircle className="h-5 w-5 mx-auto mb-2 text-emerald-600" />
                All systems operational. Zero active alarms in corporate streams.
              </div>
            )}
          </div>
        </div>

        {/* Alarm Simulator Form */}
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500 mb-3 font-display">Raise Mock System Alarm</h3>
          <form onSubmit={handleSimulateAlarm} className="space-y-3 font-mono text-xs">
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
              Dispatch Alarm (MReq 21)
            </button>
          </form>
        </div>
      </div>
    </motion.div>
  );
};
