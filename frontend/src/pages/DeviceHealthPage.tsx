import React, { useEffect, useState, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import {
  Activity, HardDrive, Wifi, Thermometer,
  Shield, Clock, Trash2, AlertTriangle, CheckCircle,
  XCircle, ArrowLeft, RefreshCw,
} from 'lucide-react';
import { apiClient } from '../lib/apiClient';

// ─── Types ───
interface CollectorEntry {
  collectorName: string;
  timestampUtc: string;
  severity: string;
  jsonData: string;
  success: boolean;
  errorMessage?: string;
}

interface EventLogEntry {
  logName: string;
  source: string;
  eventId: number;
  level: string;
  timeGenerated: string;
  message: string;
  translatedMessage?: string;
  suggestedAction?: string;
}

// ─── Helpers ───
function toCamelCase(obj: unknown): unknown {
  if (Array.isArray(obj)) return obj.map(toCamelCase);
  if (obj !== null && typeof obj === 'object') {
    return Object.fromEntries(
      Object.entries(obj as Record<string, unknown>).map(([k, v]) => [
        k.charAt(0).toLowerCase() + k.slice(1),
        toCamelCase(v),
      ])
    );
  }
  return obj;
}

function parseJson<T>(json: string): T | null {
  try { return toCamelCase(JSON.parse(json)) as T; } catch { return null; }
}

function timeAgo(utc: string): string {
  const diff = Date.now() - new Date(utc).getTime();
  const min = Math.floor(diff / 60000);
  if (min < 1) return 'az önce';
  if (min < 60) return `${min} dk önce`;
  const hr = Math.floor(min / 60);
  if (hr < 24) return `${hr} saat önce`;
  return `${Math.floor(hr / 24)} gün önce`;
}

const severityColor: Record<string, string> = {
  Info: 'text-emerald-400',
  Warning: 'text-amber-400',
  Critical: 'text-red-400',
};

const severityBg: Record<string, string> = {
  Info: 'bg-emerald-500/10 border-emerald-500/20',
  Warning: 'bg-amber-500/10 border-amber-500/20',
  Critical: 'bg-red-500/10 border-red-500/20',
};

// ─── Card Component ───
const CollectorCard: React.FC<{
  title: string;
  icon: React.ReactNode;
  entry: CollectorEntry | null;
  children: React.ReactNode;
}> = ({ title, icon, entry, children }) => {
  const sev = entry?.severity ?? 'Info';
  return (
    <div className={`rounded-xl border p-4 ${severityBg[sev] || severityBg.Info}`}>
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-2">
          {icon}
          <span className="font-semibold text-sm text-ms-text">{title}</span>
        </div>
        <div className="flex items-center gap-2">
          {entry ? (
            <>
              <span className={`text-xs font-medium ${severityColor[sev]}`}>{sev}</span>
              <span className="text-[10px] text-zinc-500">{timeAgo(entry.timestampUtc)}</span>
            </>
          ) : (
            <span className="text-xs text-zinc-600">Veri yok</span>
          )}
        </div>
      </div>
      <div className="space-y-1">{children}</div>
    </div>
  );
};

// ─── Main Page ───
const DeviceHealthPage: React.FC = () => {
  const { deviceId } = useParams<{ deviceId: string }>();
  const [collectors, setCollectors] = useState<CollectorEntry[]>([]);
  const [eventLogs, setEventLogs] = useState<EventLogEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [activeTab, setActiveTab] = useState<'overview' | 'eventlogs'>('overview');

  const fetchData = useCallback(async () => {
    if (!deviceId) return;
    setLoading(true);
    try {
      const [latest, logs] = await Promise.all([
        apiClient.getCollectorLatest(deviceId),
        apiClient.getEventLogs(deviceId, 50),
      ]);
      setCollectors(latest);
      setEventLogs(logs);
    } catch (err) {
      console.error('Health data fetch error:', err);
    } finally {
      setLoading(false);
    }
  }, [deviceId]);

  useEffect(() => { fetchData(); }, [fetchData]);

  // Auto-refresh every 30s
  useEffect(() => {
    const iv = setInterval(fetchData, 30000);
    return () => clearInterval(iv);
  }, [fetchData]);

  const getCollector = (name: string) => collectors.find(c => c.collectorName === name) ?? null;

  // Parse collector data
  const serviceData = parseJson<Array<{ serviceName: string; displayName: string; status: string; actionTaken?: string }>>(
    getCollector('ServiceMonitor')?.jsonData ?? '[]'
  );
  const diskData = parseJson<Array<{ driveLetter: string; label: string; totalGB: number; freeGB: number; usedPercent: number; smartStatus?: string }>>(
    getCollector('DiskHealth')?.jsonData ?? '[]'
  );
  const uptimeData = parseJson<{ bootTime: string; uptimeHours: number; uptimeDays: number; lastShutdown?: string; shutdownReason: string }>(
    getCollector('UptimeReport')?.jsonData ?? '{}'
  );
  const networkData = parseJson<{ downloadMbps: number; latencyMs: number; testedAt: string }>(
    getCollector('NetworkSpeed')?.jsonData ?? '{}'
  );
  const tempData = parseJson<Array<{ sensorName: string; temperatureCelsius: number; status: string }>>(
    getCollector('Temperature')?.jsonData ?? '[]'
  );
  const cleanupData = parseJson<Array<{ targetPath: string; filesDeleted: number; freedMB: number; error?: string }>>(
    getCollector('ScheduledCleanup')?.jsonData ?? '[]'
  );

  return (
    <div className="p-6 space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <Link to={`/devices/${deviceId}`} className="p-2 rounded-lg hover:bg-zinc-800 text-zinc-400 hover:text-ms-text transition-colors">
            <ArrowLeft className="w-5 h-5" />
          </Link>
          <Activity className="w-6 h-6 text-violet-400" />
          <div>
            <h1 className="text-lg font-bold text-ms-text">Cihaz Sağlık Durumu</h1>
            <p className="text-xs text-zinc-500">{deviceId}</p>
          </div>
        </div>
        <button
          onClick={fetchData}
          disabled={loading}
          className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-violet-600/20 text-violet-400 hover:bg-violet-600/30 text-sm transition-colors"
        >
          <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          Yenile
        </button>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 bg-zinc-900 rounded-lg p-1 w-fit">
        <button
          onClick={() => setActiveTab('overview')}
          className={`px-4 py-1.5 rounded-md text-sm font-medium transition-colors ${
            activeTab === 'overview' ? 'bg-violet-600 text-white' : 'text-zinc-400 hover:text-ms-text'
          }`}
        >
          Genel Bakış
        </button>
        <button
          onClick={() => setActiveTab('eventlogs')}
          className={`px-4 py-1.5 rounded-md text-sm font-medium transition-colors ${
            activeTab === 'eventlogs' ? 'bg-violet-600 text-white' : 'text-zinc-400 hover:text-ms-text'
          }`}
        >
          Event Logları ({eventLogs.length})
        </button>
      </div>

      {loading && collectors.length === 0 ? (
        <div className="flex items-center justify-center py-20">
          <RefreshCw className="w-8 h-8 animate-spin text-violet-400" />
        </div>
      ) : activeTab === 'overview' ? (
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {/* Service Monitor */}
          <CollectorCard title="Servis Durumu" icon={<Shield className="w-4 h-4 text-violet-400" />} entry={getCollector('ServiceMonitor')}>
            {serviceData && serviceData.length > 0 ? serviceData.map((s, i) => (
              <div key={i} className="flex items-center justify-between text-sm">
                <span className="text-zinc-300 truncate text-xs">{s.displayName || s.serviceName}</span>
                <div className="flex items-center gap-2">
                  {s.actionTaken && <span className="text-[10px] text-amber-400">{s.actionTaken}</span>}
                  <span className={`text-xs font-medium ${s.status === 'Running' ? 'text-emerald-400' : s.status === 'Stopped' ? 'text-red-400' : 'text-zinc-400'}`}>
                    {s.status}
                  </span>
                </div>
              </div>
            )) : <span className="text-xs text-zinc-600">Veri bekleniyor...</span>}
          </CollectorCard>

          {/* Disk Health */}
          <CollectorCard title="Disk Sağlığı" icon={<HardDrive className="w-4 h-4 text-orange-400" />} entry={getCollector('DiskHealth')}>
            {diskData && diskData.length > 0 ? diskData.map((d, i) => (
              <div key={i} className="space-y-1">
                <div className="flex items-center justify-between text-xs">
                  <span className="text-zinc-300 font-medium">{d.driveLetter} {d.label && `(${d.label})`}</span>
                  <span className={`${d.usedPercent > 90 ? 'text-red-400' : d.usedPercent > 75 ? 'text-amber-400' : 'text-emerald-400'}`}>
                    {d.usedPercent}%
                  </span>
                </div>
                <div className="w-full bg-zinc-800 rounded-full h-1.5">
                  <div
                    className={`h-1.5 rounded-full transition-all ${d.usedPercent > 90 ? 'bg-red-500' : d.usedPercent > 75 ? 'bg-amber-500' : 'bg-emerald-500'}`}
                    style={{ width: `${d.usedPercent}%` }}
                  />
                </div>
                <div className="flex justify-between text-[10px] text-zinc-500">
                  <span>{d.freeGB} GB boş</span>
                  <span>{d.totalGB} GB toplam</span>
                </div>
              </div>
            )) : <span className="text-xs text-zinc-600">Veri bekleniyor...</span>}
          </CollectorCard>

          {/* Uptime */}
          <CollectorCard title="Çalışma Süresi" icon={<Clock className="w-4 h-4 text-teal-400" />} entry={getCollector('UptimeReport')}>
            {uptimeData && uptimeData.bootTime ? (
              <div className="space-y-2">
                <div className="flex justify-between text-xs">
                  <span className="text-zinc-500">Uptime</span>
                  <span className={`font-bold ${uptimeData.uptimeDays > 30 ? 'text-amber-400' : 'text-emerald-400'}`}>
                    {uptimeData.uptimeDays} gün {Math.round(uptimeData.uptimeHours % 24)} saat
                  </span>
                </div>
                <div className="flex justify-between text-xs">
                  <span className="text-zinc-500">Boot</span>
                  <span className="text-zinc-300">{new Date(uptimeData.bootTime).toLocaleString('tr-TR')}</span>
                </div>
                {uptimeData.lastShutdown && (
                  <div className="flex justify-between text-xs">
                    <span className="text-zinc-500">Son kapanma</span>
                    <span className={`${uptimeData.shutdownReason === 'Unexpected' ? 'text-red-400' : 'text-zinc-300'}`}>
                      {uptimeData.shutdownReason === 'Unexpected' ? 'Beklenmedik' : 'Planlı'}
                    </span>
                  </div>
                )}
              </div>
            ) : <span className="text-xs text-zinc-600">Veri bekleniyor...</span>}
          </CollectorCard>

          {/* Network Speed */}
          <CollectorCard title="Ağ Hızı" icon={<Wifi className="w-4 h-4 text-green-400" />} entry={getCollector('NetworkSpeed')}>
            {networkData && networkData.downloadMbps ? (
              <div className="space-y-2">
                <div className="flex justify-between text-xs">
                  <span className="text-zinc-500">Download</span>
                  <span className={`font-bold ${networkData.downloadMbps < 1 ? 'text-red-400' : networkData.downloadMbps < 10 ? 'text-amber-400' : 'text-emerald-400'}`}>
                    {networkData.downloadMbps} Mbps
                  </span>
                </div>
                <div className="flex justify-between text-xs">
                  <span className="text-zinc-500">Latency</span>
                  <span className={`${networkData.latencyMs > 100 ? 'text-amber-400' : 'text-zinc-300'}`}>
                    {networkData.latencyMs > 0 ? `${networkData.latencyMs} ms` : 'N/A'}
                  </span>
                </div>
              </div>
            ) : <span className="text-xs text-zinc-600">Veri bekleniyor...</span>}
          </CollectorCard>

          {/* Temperature */}
          <CollectorCard title="Sıcaklık" icon={<Thermometer className="w-4 h-4 text-red-400" />} entry={getCollector('Temperature')}>
            {tempData && tempData.length > 0 ? (
              tempData[0].status === 'NotAvailable' ? (
                <span className="text-xs text-zinc-500">Sıcaklık sensörü bulunamadı</span>
              ) : tempData.map((t, i) => (
                <div key={i} className="flex items-center justify-between text-xs">
                  <span className="text-zinc-300">{t.sensorName}</span>
                  <span className={`font-bold ${t.status === 'Critical' ? 'text-red-400' : t.status === 'Warning' ? 'text-amber-400' : 'text-emerald-400'}`}>
                    {t.temperatureCelsius}°C
                  </span>
                </div>
              ))
            ) : <span className="text-xs text-zinc-600">Veri bekleniyor...</span>}
          </CollectorCard>

          {/* Cleanup */}
          <CollectorCard title="Otomatik Temizlik" icon={<Trash2 className="w-4 h-4 text-pink-400" />} entry={getCollector('ScheduledCleanup')}>
            {cleanupData && cleanupData.length > 0 ? (
              <div className="space-y-1">
                {cleanupData.map((c, i) => (
                  <div key={i} className="flex items-center justify-between text-xs">
                    <span className="text-zinc-300 truncate max-w-[200px]">{c.targetPath}</span>
                    <span className="text-zinc-400">{c.filesDeleted} dosya, {c.freedMB} MB</span>
                  </div>
                ))}
              </div>
            ) : <span className="text-xs text-zinc-600">Veri bekleniyor...</span>}
          </CollectorCard>
        </div>
      ) : (
        /* Event Logs Tab */
        <div className="space-y-2">
          {eventLogs.length === 0 ? (
            <div className="text-center py-10 text-zinc-500">Event log verisi bulunamadı</div>
          ) : eventLogs.map((e, i) => (
            <div key={i} className={`rounded-lg border p-3 ${e.level === 'Error' ? 'bg-red-500/5 border-red-500/20' : 'bg-amber-500/5 border-amber-500/20'}`}>
              <div className="flex items-start justify-between gap-4">
                <div className="flex items-center gap-2 shrink-0">
                  {e.level === 'Error' ? <XCircle className="w-4 h-4 text-red-400" /> : <AlertTriangle className="w-4 h-4 text-amber-400" />}
                  <span className={`text-xs font-medium ${e.level === 'Error' ? 'text-red-400' : 'text-amber-400'}`}>{e.level}</span>
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 mb-1">
                    <span className="text-xs font-semibold text-ms-text">{e.source}</span>
                    <span className="text-[10px] text-zinc-500">ID: {e.eventId}</span>
                    <span className="text-[10px] text-zinc-600">{e.logName}</span>
                  </div>
                  {e.translatedMessage ? (
                    <p className="text-xs text-violet-300 font-medium mb-1">{e.translatedMessage}</p>
                  ) : null}
                  <p className="text-xs text-zinc-400 line-clamp-2">{e.message}</p>
                  {e.suggestedAction && (
                    <p className="text-xs text-emerald-400/80 mt-1 flex items-center gap-1">
                      <CheckCircle className="w-3 h-3 shrink-0" />
                      {e.suggestedAction}
                    </p>
                  )}
                </div>
                <span className="text-[10px] text-zinc-600 shrink-0">
                  {new Date(e.timeGenerated).toLocaleString('tr-TR')}
                </span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default DeviceHealthPage;
