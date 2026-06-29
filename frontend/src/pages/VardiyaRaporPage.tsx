import { useState } from 'react';
import { apiClient } from '../lib/apiClient';
import {
  ClipboardList,
  WifiOff,
  Wrench,
  Activity,
  Clock,
  AlertTriangle,
  CheckCircle,
  Download,
  RefreshCw,
} from 'lucide-react';

interface ShiftHandover {
  periodFrom: string;
  periodTo: string;
  generatedAt: string;
  summary: {
    totalOfflineEvents: number;
    uniqueStoresAffected: number;
    totalOfflineDurationMinutes: number;
    stillOfflineCount: number;
    serviceIncidentCount: number;
    criticalIncidentCount: number;
    totalActionsPerformed: number;
  };
  offlineEvents: {
    storeCode: string;
    storeName: string;
    offlineAt: string;
    onlineAt?: string;
    durationMinutes: number;
    isStillOffline: boolean;
  }[];
  serviceIncidents: {
    storeCode: string;
    storeName: string;
    deviceName: string;
    serviceName: string;
    severity: string;
    firstDetectedAt: string;
    resolvedAt?: string;
    isResolved: boolean;
  }[];
  statusChanges: {
    storeCode: string;
    deviceType: string;
    wentOnline: boolean;
    changedAt: string;
  }[];
  actions: {
    username: string;
    category: string;
    description: string;
    performedAt: string;
  }[];
}

type ShiftPreset = { label: string; from: () => Date; to: () => Date };

function roundToHour(d: Date, h: number): Date {
  const result = new Date(d);
  result.setHours(h, 0, 0, 0);
  return result;
}

function yesterday(): Date {
  const d = new Date();
  d.setDate(d.getDate() - 1);
  return d;
}

const PRESETS: ShiftPreset[] = [
  {
    label: 'Gece (22:00 – 08:00)',
    from: () => roundToHour(yesterday(), 22),
    to: () => roundToHour(new Date(), 8),
  },
  {
    label: 'Gündüz (08:00 – 17:00)',
    from: () => roundToHour(new Date(), 8),
    to: () => roundToHour(new Date(), 17),
  },
  {
    label: 'Akşam (17:00 – 22:00)',
    from: () => roundToHour(new Date(), 17),
    to: () => roundToHour(new Date(), 22),
  },
  {
    label: 'Son 24 Saat',
    from: () => new Date(Date.now() - 24 * 3600 * 1000),
    to: () => new Date(),
  },
];

function toInputValue(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function fmt(dateStr: string): string {
  return new Date(dateStr).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
}

function fmtFull(dateStr: string): string {
  return new Date(dateStr).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
}

export default function VardiyaRaporPage() {
  const [fromVal, setFromVal] = useState(toInputValue(PRESETS[0].from()));
  const [toVal, setToVal] = useState(toInputValue(PRESETS[0].to()));
  const [report, setReport] = useState<ShiftHandover | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  function applyPreset(preset: ShiftPreset) {
    setFromVal(toInputValue(preset.from()));
    setToVal(toInputValue(preset.to()));
  }

  async function loadReport() {
    setLoading(true);
    setError('');
    try {
      const data = await apiClient.get<ShiftHandover>(`/api/shift-handover?from=${encodeURIComponent(fromVal)}&to=${encodeURIComponent(toVal)}`);
      setReport(data);
    } catch (e: any) {
      setError(e?.message || 'Rapor yüklenemedi.');
    } finally {
      setLoading(false);
    }
  }

  const s = report?.summary;

  return (
    <div className="p-4 md:p-6 max-w-5xl mx-auto">
      {/* Header */}
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-slate-900 dark:text-white flex items-center gap-2">
          <ClipboardList className="h-6 w-6 text-sky-500" />
          Vardiya Devir Raporu
        </h1>
        <p className="text-sm text-slate-500 dark:text-slate-400 mt-0.5">
          Seçili dönemde yaşanan tüm olayların özeti
        </p>
      </div>

      {/* Controls */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-200 dark:border-slate-700 p-4 mb-6">
        {/* Preset buttons */}
        <div className="flex flex-wrap gap-2 mb-4">
          {PRESETS.map(p => (
            <button
              key={p.label}
              onClick={() => applyPreset(p)}
              className="px-3 py-1.5 text-sm rounded-lg border border-slate-300 dark:border-slate-600 text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
            >
              {p.label}
            </button>
          ))}
        </div>

        <div className="flex flex-col sm:flex-row gap-3 items-end">
          <div className="flex-1">
            <label className="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Başlangıç</label>
            <input
              type="datetime-local"
              value={fromVal}
              onChange={e => setFromVal(e.target.value)}
              className="w-full border border-slate-300 dark:border-slate-600 rounded-lg px-3 py-2 text-sm bg-white dark:bg-slate-700 text-slate-900 dark:text-white"
            />
          </div>
          <div className="flex-1">
            <label className="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Bitiş</label>
            <input
              type="datetime-local"
              value={toVal}
              onChange={e => setToVal(e.target.value)}
              className="w-full border border-slate-300 dark:border-slate-600 rounded-lg px-3 py-2 text-sm bg-white dark:bg-slate-700 text-slate-900 dark:text-white"
            />
          </div>
          <button
            onClick={loadReport}
            disabled={loading}
            className="px-5 py-2 bg-sky-600 text-white rounded-lg text-sm font-medium hover:bg-sky-700 disabled:opacity-50 transition-colors flex items-center gap-2 whitespace-nowrap"
          >
            {loading ? <RefreshCw className="h-4 w-4 animate-spin" /> : <ClipboardList className="h-4 w-4" />}
            Rapor Oluştur
          </button>
        </div>

        {error && (
          <div className="mt-3 p-2 bg-red-50 dark:bg-red-900/20 text-red-600 dark:text-red-400 text-sm rounded-lg">
            {error}
          </div>
        )}
      </div>

      {report && s && (
        <>
          {/* Summary Cards */}
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-6">
            <SummaryCard
              icon={<WifiOff className="h-5 w-5" />}
              label="Offline Olay"
              value={s.totalOfflineEvents}
              sub={`${s.uniqueStoresAffected} mağaza`}
              color="red"
            />
            <SummaryCard
              icon={<Clock className="h-5 w-5" />}
              label="Toplam Kesinti"
              value={`${Math.floor(s.totalOfflineDurationMinutes / 60)}s ${s.totalOfflineDurationMinutes % 60}dk`}
              sub={s.stillOfflineCount > 0 ? `${s.stillOfflineCount} hâlâ offline` : 'Tümü çözüldü'}
              color={s.stillOfflineCount > 0 ? 'red' : 'green'}
            />
            <SummaryCard
              icon={<Wrench className="h-5 w-5" />}
              label="Servis Kesintisi"
              value={s.serviceIncidentCount}
              sub={s.criticalIncidentCount > 0 ? `${s.criticalIncidentCount} kritik` : 'Kritik yok'}
              color={s.criticalIncidentCount > 0 ? 'red' : 'slate'}
            />
            <SummaryCard
              icon={<Activity className="h-5 w-5" />}
              label="Yapılan İşlem"
              value={s.totalActionsPerformed}
              sub="müdahale"
              color="sky"
            />
          </div>

          {/* Period info */}
          <div className="text-xs text-slate-500 dark:text-slate-400 mb-4 text-right">
            Rapor: {fmtFull(report.periodFrom)} → {fmtFull(report.periodTo)} · Oluşturuldu: {fmtFull(report.generatedAt)}
          </div>

          {/* Offline Events */}
          {report.offlineEvents.length > 0 && (
            <Section title="Mağaza Offline Olayları" icon={<WifiOff className="h-4 w-4" />} count={report.offlineEvents.length}>
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-slate-500 dark:text-slate-400 border-b border-slate-100 dark:border-slate-700">
                    <th className="pb-2 font-medium">Mağaza</th>
                    <th className="pb-2 font-medium">Offline</th>
                    <th className="pb-2 font-medium">Online</th>
                    <th className="pb-2 font-medium">Süre</th>
                    <th className="pb-2 font-medium">Durum</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 dark:divide-slate-700/50">
                  {report.offlineEvents.map((e, i) => (
                    <tr key={i} className="text-slate-700 dark:text-slate-200">
                      <td className="py-2 font-medium">{e.storeCode} {e.storeName && `— ${e.storeName}`}</td>
                      <td className="py-2">{fmt(e.offlineAt)}</td>
                      <td className="py-2">{e.onlineAt ? fmt(e.onlineAt) : '—'}</td>
                      <td className="py-2">{e.durationMinutes} dk</td>
                      <td className="py-2">
                        {e.isStillOffline
                          ? <span className="flex items-center gap-1 text-red-600 dark:text-red-400 text-xs font-medium"><AlertTriangle className="h-3 w-3" /> Offline</span>
                          : <span className="flex items-center gap-1 text-green-600 dark:text-green-400 text-xs font-medium"><CheckCircle className="h-3 w-3" /> Çözüldü</span>
                        }
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Section>
          )}

          {/* Service Incidents */}
          {report.serviceIncidents.length > 0 && (
            <Section title="Servis Kesintileri" icon={<Wrench className="h-4 w-4" />} count={report.serviceIncidents.length}>
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-slate-500 dark:text-slate-400 border-b border-slate-100 dark:border-slate-700">
                    <th className="pb-2 font-medium">Mağaza / Cihaz</th>
                    <th className="pb-2 font-medium">Servis</th>
                    <th className="pb-2 font-medium">Tespit</th>
                    <th className="pb-2 font-medium">Durum</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 dark:divide-slate-700/50">
                  {report.serviceIncidents.map((inc, i) => (
                    <tr key={i} className="text-slate-700 dark:text-slate-200">
                      <td className="py-2">
                        <div className="font-medium">{inc.storeCode} — {inc.storeName}</div>
                        <div className="text-xs text-slate-500">{inc.deviceName}</div>
                      </td>
                      <td className="py-2">
                        <div>{inc.serviceName}</div>
                        {inc.severity === 'Critical' && (
                          <span className="text-xs px-1.5 py-0.5 bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400 rounded font-medium">Kritik</span>
                        )}
                      </td>
                      <td className="py-2">{fmt(inc.firstDetectedAt)}</td>
                      <td className="py-2">
                        {inc.isResolved
                          ? <span className="flex items-center gap-1 text-green-600 dark:text-green-400 text-xs font-medium"><CheckCircle className="h-3 w-3" /> Çözüldü</span>
                          : <span className="flex items-center gap-1 text-amber-600 dark:text-amber-400 text-xs font-medium"><AlertTriangle className="h-3 w-3" /> Devam ediyor</span>
                        }
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Section>
          )}

          {/* Actions performed */}
          {report.actions.length > 0 && (
            <Section title="Yapılan İşlemler" icon={<Activity className="h-4 w-4" />} count={report.actions.length}>
              <ul className="divide-y divide-slate-100 dark:divide-slate-700/50">
                {report.actions.map((a, i) => (
                  <li key={i} className="py-2 flex items-start gap-3 text-sm">
                    <span className="text-xs text-slate-400 dark:text-slate-500 whitespace-nowrap mt-0.5">{fmt(a.performedAt)}</span>
                    <div className="flex-1">
                      <span className="text-xs px-1.5 py-0.5 bg-slate-100 dark:bg-slate-700 text-slate-600 dark:text-slate-300 rounded font-medium mr-2">{a.category}</span>
                      <span className="text-slate-700 dark:text-slate-200">{a.description}</span>
                    </div>
                    <span className="text-xs text-slate-400 dark:text-slate-500 whitespace-nowrap">{a.username}</span>
                  </li>
                ))}
              </ul>
            </Section>
          )}

          {s.totalOfflineEvents === 0 && s.serviceIncidentCount === 0 && s.totalActionsPerformed === 0 && (
            <div className="text-center py-16 text-slate-500 dark:text-slate-400">
              <CheckCircle className="h-12 w-12 mx-auto mb-3 text-green-500 opacity-60" />
              <p className="text-lg font-medium text-green-700 dark:text-green-400">Temiz Vardiya</p>
              <p className="text-sm mt-1">Bu dönemde kayda değer olay yaşanmadı.</p>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function SummaryCard({
  icon, label, value, sub, color,
}: {
  icon: React.ReactNode;
  label: string;
  value: string | number;
  sub?: string;
  color?: 'red' | 'green' | 'sky' | 'slate';
}) {
  const colorMap = {
    red: 'text-red-600 dark:text-red-400',
    green: 'text-green-600 dark:text-green-400',
    sky: 'text-sky-600 dark:text-sky-400',
    slate: 'text-slate-700 dark:text-slate-200',
  };
  const iconColorMap = {
    red: 'text-red-500',
    green: 'text-green-500',
    sky: 'text-sky-500',
    slate: 'text-slate-400',
  };

  return (
    <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-200 dark:border-slate-700 p-4">
      <div className={`mb-2 ${iconColorMap[color ?? 'slate']}`}>{icon}</div>
      <div className={`text-2xl font-bold ${colorMap[color ?? 'slate']}`}>{value}</div>
      <div className="text-xs text-slate-500 dark:text-slate-400 font-medium mt-0.5">{label}</div>
      {sub && <div className="text-xs text-slate-400 dark:text-slate-500 mt-0.5">{sub}</div>}
    </div>
  );
}

function Section({ title, icon, count, children }: {
  title: string;
  icon: React.ReactNode;
  count: number;
  children: React.ReactNode;
}) {
  return (
    <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-200 dark:border-slate-700 mb-4 overflow-hidden">
      <div className="px-4 py-3 border-b border-slate-100 dark:border-slate-700 flex items-center gap-2">
        <span className="text-slate-500 dark:text-slate-400">{icon}</span>
        <span className="font-semibold text-slate-900 dark:text-white">{title}</span>
        <span className="ml-auto text-sm text-slate-500 dark:text-slate-400">{count} kayıt</span>
      </div>
      <div className="px-4 py-3 overflow-x-auto">{children}</div>
    </div>
  );
}
