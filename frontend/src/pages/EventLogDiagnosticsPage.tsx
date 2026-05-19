import React, { useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle, CheckCircle2, ChevronDown, ChevronRight, Cloud, Cpu,
  HardDrive, Power, RefreshCw, Search, ServerCrash, Shield, Zap,
} from 'lucide-react';
import { apiClient, type SqlDeviceWithStatus } from '../lib/apiClient';
import type {
  BootShutdownEvent, Device, EventLogAnalysisResult, EventLogHypothesis,
  EventLogTimelineItem, ShutdownChain,
} from '../types';

const HOURS_OPTIONS = [
  { label: '24sa', value: 24 },
  { label: '72sa', value: 72 },
  { label: '7g', value: 168 },
  { label: '30g', value: 720 },
];

interface DeviceOption {
  key: string;
  displayDeviceId: string;
  analysisDeviceId: string;
  title: string;
  ip: string;
  storeCode: number;
  storeName: string;
  typeLabel: string;
  online: boolean;
  hasAgent: boolean;
}

function formatDate(value?: string | null) {
  if (!value) return '—';
  return new Date(value).toLocaleString('tr-TR', { dateStyle: 'short', timeStyle: 'short' });
}

function timeAgo(value?: string | null) {
  if (!value) return '—';
  const diff = Date.now() - new Date(value).getTime();
  const m = Math.floor(diff / 60000);
  if (m < 1) return 'az önce';
  if (m < 60) return `${m}dk önce`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}sa önce`;
  return `${Math.floor(h / 24)}g önce`;
}

function mergeDeviceOptions(agents: Device[], sql: SqlDeviceWithStatus[]): DeviceOption[] {
  const out: DeviceOption[] = [];
  const agentByIp = new Map<string, Device>();
  const consumed = new Set<string>();

  for (const a of agents) {
    if (a.ipAddress && !agentByIp.has(a.ipAddress)) agentByIp.set(a.ipAddress, a);
  }
  for (const s of sql) {
    if ((s.deviceType || '').toLowerCase() === 'router') continue;
    const matched = agentByIp.get(s.calculatedIpAddress);
    if (matched) consumed.add(matched.id);
    out.push({
      key: s.deviceId,
      displayDeviceId: s.deviceId,
      analysisDeviceId: matched?.id || s.deviceId,
      title: s.deviceName || s.deviceId,
      ip: s.calculatedIpAddress,
      storeCode: s.storeCode,
      storeName: s.storeName || `Mağaza ${s.storeCode}`,
      typeLabel: s.deviceType || '—',
      online: matched ? (matched.online || s.isOnline) : s.isOnline,
      hasAgent: Boolean(matched),
    });
  }
  for (const a of agents) {
    if (consumed.has(a.id)) continue;
    out.push({
      key: a.id,
      displayDeviceId: a.id,
      analysisDeviceId: a.id,
      title: a.hostname || a.id,
      ip: a.ipAddress,
      storeCode: a.storeCode ?? 0,
      storeName: a.storeName || (a.storeCode ? `Mağaza ${a.storeCode}` : '—'),
      typeLabel: a.type || '—',
      online: a.online,
      hasAgent: true,
    });
  }
  return out.sort((a, b) => {
    if (a.online !== b.online) return a.online ? -1 : 1;
    if (a.storeCode !== b.storeCode) return a.storeCode - b.storeCode;
    return a.title.localeCompare(b.title, 'tr');
  });
}

const SEVERITY_COLORS: Record<string, { text: string; bg: string; border: string }> = {
  Critical: { text: 'text-red-300', bg: 'bg-red-500/10', border: 'border-red-500/30' },
  Error: { text: 'text-red-300', bg: 'bg-red-500/10', border: 'border-red-500/30' },
  Warning: { text: 'text-amber-300', bg: 'bg-amber-500/10', border: 'border-amber-500/30' },
  Info: { text: 'text-emerald-300', bg: 'bg-emerald-500/10', border: 'border-emerald-500/30' },
};

function severityClass(level: string) {
  return SEVERITY_COLORS[level] || SEVERITY_COLORS.Info;
}

function categoryIcon(category: string) {
  const c = category.toLowerCase();
  if (c.includes('disk')) return <HardDrive className="h-4 w-4" />;
  if (c.includes('güç') || c.includes('guc') || c.includes('hard reset')) return <Zap className="h-4 w-4" />;
  if (c.includes('bsod') || c.includes('sürücü') || c.includes('surucu')) return <ServerCrash className="h-4 w-4" />;
  if (c.includes('whea')) return <Cpu className="h-4 w-4" />;
  if (c.includes('gpu') || c.includes('tdr')) return <ServerCrash className="h-4 w-4" />;
  if (c.includes('servis')) return <Shield className="h-4 w-4" />;
  if (c.includes('uygulama')) return <ServerCrash className="h-4 w-4" />;
  if (c.includes('ag') || c.includes('ağ')) return <Cloud className="h-4 w-4" />;
  return <AlertTriangle className="h-4 w-4" />;
}

function verdictTone(summary: EventLogAnalysisResult['summary']) {
  const score = summary.blueScreenCount * 4 + summary.unexpectedShutdownCount * 3 + summary.wheaCount * 3
    + summary.diskIssueCount * 2 + summary.tdrCount * 2 + summary.serviceCrashCount + summary.appCrashCount;
  if (score >= 10) return { label: 'Kritik', cls: 'border-red-500/40 bg-red-500/10 text-red-200', icon: <ServerCrash className="h-5 w-5" /> };
  if (score >= 4) return { label: 'Riskli', cls: 'border-amber-500/40 bg-amber-500/10 text-amber-200', icon: <AlertTriangle className="h-5 w-5" /> };
  if (score > 0) return { label: 'Dikkat', cls: 'border-sky-500/40 bg-sky-500/10 text-sky-200', icon: <AlertTriangle className="h-5 w-5" /> };
  return { label: 'Saglikli', cls: 'border-emerald-500/40 bg-emerald-500/10 text-emerald-200', icon: <CheckCircle2 className="h-5 w-5" /> };
}

const BootTimelineStrip: React.FC<{ events: BootShutdownEvent[] }> = ({ events }) => {
  if (events.length === 0) {
    return <div className="text-xs text-zinc-500">Boot/kapanma olayı kayıtlı değil.</div>;
  }
  return (
    <div className="flex flex-wrap gap-1.5">
      {events.slice(-30).map((e, idx) => {
        const tone = e.type === 'ShutdownUnexpected'
          ? 'bg-red-500/80 border-red-400'
          : e.type === 'BootClean'
            ? 'bg-emerald-500/70 border-emerald-400'
            : e.type === 'ShutdownClean'
              ? 'bg-sky-500/70 border-sky-400'
              : e.type === 'UserShutdown'
                ? 'bg-zinc-500/70 border-zinc-400'
                : 'bg-zinc-700 border-zinc-600';
        const tooltip = `${formatDate(e.timeGenerated)} — ${e.source} #${e.eventId} — ${e.detail}`;
        return (
          <div
            key={`${e.timeGenerated}-${idx}`}
            title={tooltip}
            className={`h-6 w-3 rounded-sm border ${tone} cursor-help`}
          />
        );
      })}
    </div>
  );
};

const HypothesisCard: React.FC<{ h: EventLogHypothesis; index: number }> = ({ h, index }) => {
  const [open, setOpen] = useState(index === 0);
  return (
    <div className="rounded-lg border border-zinc-800 bg-zinc-950">
      <button
        onClick={() => setOpen((v) => !v)}
        className="flex w-full items-start gap-3 px-4 py-3 text-left"
      >
        <div className="mt-0.5 rounded-md bg-zinc-900 p-1.5 text-zinc-400">{categoryIcon(h.category)}</div>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-[10px] font-medium uppercase tracking-wide text-zinc-500">{h.category}</span>
            <span className="rounded border border-zinc-700 bg-zinc-900 px-1.5 py-0.5 text-[10px] text-zinc-400">
              {h.confidence} · skor {h.score}
            </span>
          </div>
          <div className="mt-1 text-sm font-medium text-white">{h.title}</div>
          {!open && <div className="mt-1 line-clamp-1 text-xs text-zinc-500">{h.why}</div>}
        </div>
        {open ? <ChevronDown className="h-4 w-4 text-zinc-500" /> : <ChevronRight className="h-4 w-4 text-zinc-500" />}
      </button>
      {open && (
        <div className="border-t border-zinc-800 px-4 py-3">
          <p className="text-xs leading-5 text-zinc-300">{h.why}</p>
          {h.evidence.length > 0 && (
            <div className="mt-3">
              <div className="text-[10px] font-semibold uppercase tracking-wide text-zinc-500">Kanıt</div>
              <ul className="mt-1.5 space-y-1">
                {h.evidence.map((e) => (
                  <li key={e} className="rounded border border-zinc-800 bg-zinc-900/50 px-2 py-1 font-mono text-[11px] text-zinc-300">
                    {e}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {h.recommendedActions.length > 0 && (
            <div className="mt-3">
              <div className="text-[10px] font-semibold uppercase tracking-wide text-zinc-500">Önerilen Aksiyon</div>
              <ul className="mt-1.5 space-y-1">
                {h.recommendedActions.map((a) => (
                  <li key={a} className="flex items-start gap-2 text-xs text-zinc-300">
                    <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0 text-emerald-400" />
                    <span>{a}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </div>
  );
};

const ShutdownChainCard: React.FC<{ chain: ShutdownChain }> = ({ chain }) => {
  const [open, setOpen] = useState(false);
  return (
    <div className="rounded-lg border border-red-500/20 bg-red-500/5">
      <button
        onClick={() => setOpen((v) => !v)}
        className="flex w-full items-center justify-between gap-3 px-4 py-2.5 text-left"
      >
        <div className="flex items-center gap-2">
          <Power className="h-4 w-4 text-red-400" />
          <span className="text-sm font-medium text-white">{formatDate(chain.shutdownAt)}</span>
          <span className="rounded border border-zinc-700 bg-zinc-900 px-1.5 py-0.5 text-[10px] text-zinc-400">
            {chain.shutdownSource} #{chain.shutdownEventId}
          </span>
          <span className="text-[11px] text-zinc-500">{chain.precedingEvents.length} önceki olay</span>
        </div>
        {open ? <ChevronDown className="h-4 w-4 text-zinc-500" /> : <ChevronRight className="h-4 w-4 text-zinc-500" />}
      </button>
      {open && (
        <div className="border-t border-red-500/20 px-4 py-3">
          {chain.precedingEvents.length === 0 ? (
            <div className="text-xs text-zinc-500">Bu kapanmadan önceki 30 dakikada kritik olay yok — saf güç kesintisi/hard reset şüphesi.</div>
          ) : (
            <ul className="space-y-1">
              {chain.precedingEvents.map((e, i) => (
                <li key={`${e.timeGenerated}-${i}`} className="flex items-baseline gap-2 text-[11px]">
                  <span className="font-mono text-zinc-500">{new Date(e.timeGenerated).toLocaleTimeString('tr-TR')}</span>
                  <span className={`rounded px-1 py-0.5 text-[10px] ${severityClass(e.level).bg} ${severityClass(e.level).text}`}>
                    {e.level}
                  </span>
                  <span className="text-zinc-400">{e.source} #{e.eventId}</span>
                  <span className="text-zinc-300">{e.summary}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
};

const TimelineItem: React.FC<{ item: EventLogTimelineItem }> = ({ item }) => {
  const sev = severityClass(item.level);
  return (
    <div className="flex items-start gap-2 border-b border-zinc-900 px-3 py-1.5 text-[11px] last:border-0 hover:bg-zinc-900/40">
      <span className="w-[88px] shrink-0 font-mono text-zinc-500">
        {new Date(item.timeGenerated).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })}
      </span>
      <span className={`shrink-0 rounded px-1.5 py-0.5 text-[10px] ${sev.bg} ${sev.text}`}>{item.level}</span>
      <span className="w-[180px] shrink-0 truncate text-zinc-400" title={item.source}>{item.source}</span>
      <span className="w-12 shrink-0 font-mono text-zinc-500">#{item.eventId}</span>
      <span className="min-w-0 flex-1 truncate text-zinc-300" title={item.rawMessage}>{item.summary}</span>
    </div>
  );
};

const DeviceCombo: React.FC<{
  devices: DeviceOption[];
  selectedKey: string;
  onSelect: (key: string) => void;
}> = ({ devices, selectedKey, onSelect }) => {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState('');
  const selected = devices.find((d) => d.key === selectedKey);

  const filtered = useMemo(() => {
    const s = q.trim().toLocaleLowerCase('tr');
    if (!s) return devices.slice(0, 200);
    return devices.filter((d) => {
      const hay = `${d.title} ${d.storeName} ${d.storeCode} ${d.ip} ${d.displayDeviceId} ${d.typeLabel}`.toLocaleLowerCase('tr');
      return hay.includes(s);
    }).slice(0, 200);
  }, [devices, q]);

  return (
    <div className="relative w-full max-w-xl">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="flex w-full items-center justify-between gap-3 rounded-lg border border-zinc-800 bg-zinc-950 px-3 py-2 text-left text-sm hover:border-zinc-700"
      >
        <div className="min-w-0 flex-1">
          {selected ? (
            <div className="flex items-center gap-2">
              <span className={`h-2 w-2 rounded-full ${selected.online ? 'bg-emerald-500' : 'bg-zinc-600'}`} />
              <span className="font-medium text-white">{selected.storeName}</span>
              <span className="text-zinc-500">·</span>
              <span className="text-zinc-300">{selected.title}</span>
              <span className="text-zinc-500">·</span>
              <span className="text-zinc-400">{selected.typeLabel}</span>
              <span className="text-zinc-500">·</span>
              <span className="font-mono text-zinc-400">{selected.ip}</span>
              {!selected.hasAgent && (
                <span className="rounded border border-amber-500/30 bg-amber-500/10 px-1.5 py-0.5 text-[10px] text-amber-300">
                  agentsız
                </span>
              )}
            </div>
          ) : (
            <span className="text-zinc-500">Cihaz seçin…</span>
          )}
        </div>
        <ChevronDown className={`h-4 w-4 text-zinc-500 transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>
      {open && (
        <div className="absolute z-30 mt-1 w-[640px] max-w-[90vw] rounded-lg border border-zinc-800 bg-zinc-950 shadow-xl">
          <div className="border-b border-zinc-800 p-2">
            <div className="relative">
              <Search className="pointer-events-none absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-zinc-500" />
              <input
                autoFocus
                value={q}
                onChange={(e) => setQ(e.target.value)}
                placeholder="Mağaza, isim, IP ara…"
                className="w-full rounded-md border border-zinc-800 bg-zinc-900 px-7 py-1.5 text-xs text-white outline-none placeholder:text-zinc-500 focus:border-cyan-500/50"
              />
            </div>
          </div>
          <div className="max-h-80 overflow-y-auto">
            {filtered.length === 0 ? (
              <div className="px-3 py-6 text-center text-xs text-zinc-500">Sonuç yok</div>
            ) : (
              filtered.map((d) => (
                <button
                  key={d.key}
                  onClick={() => { onSelect(d.key); setOpen(false); setQ(''); }}
                  className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-xs hover:bg-zinc-900 ${
                    d.key === selectedKey ? 'bg-cyan-500/10' : ''
                  }`}
                >
                  <span className={`h-1.5 w-1.5 rounded-full ${d.online ? 'bg-emerald-500' : 'bg-zinc-600'}`} />
                  <span className="w-12 shrink-0 font-mono text-zinc-500">{d.storeCode || '—'}</span>
                  <span className="w-44 shrink-0 truncate text-white">{d.storeName}</span>
                  <span className="w-40 shrink-0 truncate text-zinc-300">{d.title}</span>
                  <span className="w-16 shrink-0 text-zinc-400">{d.typeLabel}</span>
                  <span className="flex-1 truncate font-mono text-zinc-500">{d.ip}</span>
                  {!d.hasAgent && (
                    <span className="rounded border border-amber-500/30 bg-amber-500/10 px-1 py-0.5 text-[9px] text-amber-300">
                      agentsız
                    </span>
                  )}
                </button>
              ))
            )}
          </div>
        </div>
      )}
    </div>
  );
};

const EventLogDiagnosticsPage: React.FC = () => {
  const [devices, setDevices] = useState<DeviceOption[]>([]);
  const [selectedKey, setSelectedKey] = useState('');
  const [hours, setHours] = useState(24);
  const [analysis, setAnalysis] = useState<EventLogAnalysisResult | null>(null);
  const [loadingDevices, setLoadingDevices] = useState(true);
  const [loadingAnalysis, setLoadingAnalysis] = useState(false);
  const [pulling, setPulling] = useState(false);
  const [pullStatus, setPullStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [levelFilter, setLevelFilter] = useState<'all' | 'critical' | 'error' | 'warning'>('all');
  const [sourceFilter, setSourceFilter] = useState('');

  useEffect(() => {
    let m = true;
    (async () => {
      try {
        setLoadingDevices(true);
        const [a, s] = await Promise.allSettled([
          apiClient.getDevices(),
          apiClient.getSqlDevicesWithStatus({ timeoutMs: 1500, maxConcurrency: 40 }),
        ]);
        if (!m) return;
        setDevices(mergeDeviceOptions(
          a.status === 'fulfilled' ? a.value : [],
          s.status === 'fulfilled' ? s.value : [],
        ));
      } catch (e) {
        if (!m) return;
        setError(e instanceof Error ? e.message : 'Cihaz listesi yüklenemedi.');
      } finally {
        if (m) setLoadingDevices(false);
      }
    })();
    return () => { m = false; };
  }, []);

  useEffect(() => {
    if (!selectedKey && devices.length > 0) setSelectedKey(devices[0].key);
  }, [devices, selectedKey]);

  const selected = devices.find((d) => d.key === selectedKey) ?? null;

  const normalizeAnalysis = (raw: EventLogAnalysisResult): EventLogAnalysisResult => ({
    ...raw,
    hypotheses: raw.hypotheses ?? [],
    recentTimeline: raw.recentTimeline ?? [],
    bootShutdownTimeline: raw.bootShutdownTimeline ?? [],
    shutdownChains: raw.shutdownChains ?? [],
    dataQuality: raw.dataQuality ?? {
      hasEventLogData: (raw.recentTimeline?.length ?? 0) > 0,
      latestEventLogReport: null,
      hasUptimeData: false,
      hasDiskHealthData: false,
      hasTemperatureData: false,
    },
    summary: {
      ...raw.summary,
      wheaCount: raw.summary?.wheaCount ?? 0,
      appCrashCount: raw.summary?.appCrashCount ?? 0,
      tdrCount: raw.summary?.tdrCount ?? 0,
    },
  });

  const loadAnalysis = async () => {
    if (!selected) return;
    setLoadingAnalysis(true);
    setError(null);
    setPullStatus(null);
    try {
      const r = await apiClient.getEventLogAnalysis(selected.analysisDeviceId, hours, 300);
      setAnalysis(normalizeAnalysis(r));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Analiz alınamadı.');
    } finally {
      setLoadingAnalysis(false);
    }
  };

  useEffect(() => {
    if (selected) loadAnalysis();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedKey, hours]);

  const pullFromDevice = async () => {
    if (!selected) return;
    setPulling(true);
    setError(null);
    setPullStatus(null);
    try {
      const res = await apiClient.pullEventLogsFromDevice(selected.displayDeviceId, hours);
      setAnalysis(normalizeAnalysis(res.analysis));
      const partial = (res.partialErrors?.length ?? 0) > 0 ? ` · ${res.partialErrors.length} log kısmi hata` : '';
      setPullStatus(`${res.host} üzerinden ${res.eventCount} olay çekildi${partial}.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Cihazdan tazeleme başarısız.');
    } finally {
      setPulling(false);
    }
  };

  const filteredTimeline = useMemo(() => {
    if (!analysis) return [];
    const s = sourceFilter.trim().toLocaleLowerCase();
    return analysis.recentTimeline.filter((it) => {
      if (levelFilter === 'critical' && it.level !== 'Critical') return false;
      if (levelFilter === 'error' && it.level !== 'Error' && it.level !== 'Critical') return false;
      if (levelFilter === 'warning' && it.level === 'Info') return false;
      if (s && !`${it.source} ${it.eventId} ${it.summary}`.toLocaleLowerCase().includes(s)) return false;
      return true;
    });
  }, [analysis, levelFilter, sourceFilter]);

  const verdict = analysis ? verdictTone(analysis.summary) : null;
  const summary = analysis?.summary;
  const dq = analysis?.dataQuality;

  return (
    <div className="space-y-4 p-4">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-800 pb-3">
        <div className="flex items-center gap-3">
          <div className="rounded-md bg-cyan-500/10 p-1.5 text-cyan-300">
            <ServerCrash className="h-4 w-4" />
          </div>
          <div>
            <h1 className="text-base font-semibold text-white">Event Log Teşhis</h1>
            <p className="text-[11px] text-zinc-500">
              Cihazda ne olduğunu Windows Event Log üzerinden analiz eder · agent + agentless (WinRM)
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <div className="flex overflow-hidden rounded-md border border-zinc-800">
            {HOURS_OPTIONS.map((o) => (
              <button
                key={o.value}
                onClick={() => setHours(o.value)}
                className={`px-2.5 py-1 text-xs ${
                  hours === o.value ? 'bg-cyan-500/15 text-cyan-200' : 'bg-zinc-950 text-zinc-400 hover:text-white'
                }`}
              >{o.label}</button>
            ))}
          </div>
          <button
            onClick={loadAnalysis}
            disabled={loadingAnalysis || !selected}
            className="inline-flex items-center gap-1.5 rounded-md border border-zinc-800 bg-zinc-950 px-2.5 py-1 text-xs text-zinc-300 hover:border-zinc-700 disabled:opacity-50"
            title="Cached veriden yeniden analiz et"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${loadingAnalysis ? 'animate-spin' : ''}`} />
            Analizi yenile
          </button>
          <button
            onClick={pullFromDevice}
            disabled={pulling || !selected}
            className="inline-flex items-center gap-1.5 rounded-md border border-cyan-500/30 bg-cyan-500/10 px-2.5 py-1 text-xs font-medium text-cyan-200 hover:bg-cyan-500/20 disabled:opacity-50"
            title="Backend → cihaza WinRM/RPC ile bağlanıp event log'u sıcak olarak çeker (agent gerekmez)"
          >
            <Cloud className={`h-3.5 w-3.5 ${pulling ? 'animate-spin' : ''}`} />
            Sahadan tazele
          </button>
        </div>
      </div>

      {/* Device picker row */}
      <div className="flex flex-wrap items-center gap-3">
        {loadingDevices ? (
          <div className="text-xs text-zinc-500">Cihazlar yükleniyor…</div>
        ) : (
          <DeviceCombo devices={devices} selectedKey={selectedKey} onSelect={setSelectedKey} />
        )}
        {dq && (
          <div className="flex items-center gap-1.5 text-[11px]">
            <span className={`rounded border px-1.5 py-0.5 ${dq.hasEventLogData ? 'border-emerald-500/30 bg-emerald-500/5 text-emerald-300' : 'border-zinc-700 bg-zinc-900 text-zinc-500'}`}>
              EventLog {dq.hasEventLogData ? '✓' : '—'}
            </span>
            <span className={`rounded border px-1.5 py-0.5 ${dq.hasUptimeData ? 'border-emerald-500/30 bg-emerald-500/5 text-emerald-300' : 'border-zinc-700 bg-zinc-900 text-zinc-500'}`}>
              Uptime {dq.hasUptimeData ? '✓' : '—'}
            </span>
            <span className={`rounded border px-1.5 py-0.5 ${dq.hasDiskHealthData ? 'border-emerald-500/30 bg-emerald-500/5 text-emerald-300' : 'border-zinc-700 bg-zinc-900 text-zinc-500'}`}>
              Disk {dq.hasDiskHealthData ? '✓' : '—'}
            </span>
            {dq.latestEventLogReport && (
              <span className="text-zinc-500">son rapor: {timeAgo(dq.latestEventLogReport)}</span>
            )}
          </div>
        )}
      </div>

      {error && (
        <div className="rounded-md border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs text-red-200">{error}</div>
      )}
      {pullStatus && (
        <div className="rounded-md border border-cyan-500/30 bg-cyan-500/10 px-3 py-2 text-xs text-cyan-200">{pullStatus}</div>
      )}

      {/* Verdict */}
      {analysis && summary && verdict && (
        <div className={`rounded-lg border px-4 py-3 ${verdict.cls}`}>
          <div className="flex items-start gap-3">
            <div className="mt-0.5">{verdict.icon}</div>
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2 text-[10px] font-semibold uppercase tracking-wide">
                <span>{verdict.label}</span>
                <span className="text-zinc-500">·</span>
                <span className="text-zinc-300">{summary.primaryCategory}</span>
                <span className="text-zinc-500">·</span>
                <span className="text-zinc-400">güven: {summary.primaryConfidence}</span>
                <span className="text-zinc-500">·</span>
                <span className="text-zinc-400">{analysis.hoursAnalyzed}sa analiz</span>
              </div>
              <div className="mt-1 text-sm font-medium text-white">{summary.overallAssessment}</div>
              <div className="mt-2 flex flex-wrap gap-3 text-[11px] text-zinc-400">
                <span>BSOD: <span className="font-semibold text-white">{summary.blueScreenCount}</span></span>
                <span>Ani kapanma: <span className="font-semibold text-white">{summary.unexpectedShutdownCount}</span></span>
                <span>Disk: <span className="font-semibold text-white">{summary.diskIssueCount}</span></span>
                <span>WHEA: <span className="font-semibold text-white">{summary.wheaCount}</span></span>
                <span>TDR: <span className="font-semibold text-white">{summary.tdrCount}</span></span>
                <span>Servis çökme: <span className="font-semibold text-white">{summary.serviceCrashCount}</span></span>
                <span>Uyg. çökme: <span className="font-semibold text-white">{summary.appCrashCount}</span></span>
                <span>Ağ: <span className="font-semibold text-white">{summary.networkIssueCount}</span></span>
                <span>Son ani kapanma: <span className="text-white">{formatDate(summary.lastUnexpectedShutdownAt)}</span></span>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Empty/loading states */}
      {loadingAnalysis && !analysis && (
        <div className="flex items-center justify-center py-16 text-zinc-500">
          <RefreshCw className="h-5 w-5 animate-spin" />
        </div>
      )}
      {!selected && !loadingDevices && (
        <div className="rounded-lg border border-zinc-800 bg-zinc-950 px-4 py-10 text-center text-xs text-zinc-500">
          Soldan bir cihaz seçin.
        </div>
      )}
      {analysis && summary && !summary.unexpectedShutdownCount && !summary.blueScreenCount && analysis.hypotheses.length === 0 && (
        <div className="rounded-lg border border-emerald-500/20 bg-emerald-500/5 px-4 py-3 text-xs text-emerald-200">
          Bu pencerede dikkat edilecek bir bulgu yok. Daha uzun aralık deneyin veya cihazdan tazeleyin.
        </div>
      )}

      {/* Body */}
      {analysis && summary && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
          {/* LEFT: hypotheses + boot strip + shutdown chains */}
          <div className="space-y-4">
            <div className="rounded-lg border border-zinc-800 bg-zinc-950 p-3">
              <div className="mb-2 flex items-center justify-between">
                <div className="text-[10px] font-semibold uppercase tracking-wide text-zinc-500">
                  Boot/Kapanma akışı (kronolojik, son {analysis.bootShutdownTimeline.length})
                </div>
                <div className="flex items-center gap-2 text-[10px] text-zinc-500">
                  <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-sm bg-emerald-500" /> açılış</span>
                  <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-sm bg-sky-500" /> temiz kapanış</span>
                  <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-sm bg-red-500" /> ani kapanış</span>
                  <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-sm bg-zinc-500" /> kullanıcı</span>
                </div>
              </div>
              <BootTimelineStrip events={analysis.bootShutdownTimeline} />
            </div>

            <div>
              <div className="mb-2 text-[10px] font-semibold uppercase tracking-wide text-zinc-500">
                Kök neden hipotezleri ({analysis.hypotheses.length})
              </div>
              <div className="space-y-2">
                {analysis.hypotheses.length === 0 ? (
                  <div className="rounded-lg border border-zinc-800 bg-zinc-950 px-3 py-6 text-center text-xs text-zinc-500">
                    Bu pencerede tetikleyici bir hipotez kuracak yeterli kanıt yok.
                  </div>
                ) : analysis.hypotheses.map((h, i) => (
                  <HypothesisCard key={`${h.category}-${h.title}-${i}`} h={h} index={i} />
                ))}
              </div>
            </div>

            {analysis.shutdownChains.length > 0 && (
              <div>
                <div className="mb-2 text-[10px] font-semibold uppercase tracking-wide text-zinc-500">
                  Her kapanmadan önceki 30dk
                </div>
                <div className="space-y-2">
                  {analysis.shutdownChains.map((c) => (
                    <ShutdownChainCard key={c.shutdownAt} chain={c} />
                  ))}
                </div>
              </div>
            )}
          </div>

          {/* RIGHT: filterable event timeline */}
          <div className="flex min-h-0 flex-col rounded-lg border border-zinc-800 bg-zinc-950">
            <div className="flex items-center justify-between gap-2 border-b border-zinc-800 px-3 py-2">
              <div className="text-[10px] font-semibold uppercase tracking-wide text-zinc-500">
                Event timeline ({filteredTimeline.length}/{analysis.recentTimeline.length})
              </div>
              <div className="flex items-center gap-1.5">
                <select
                  value={levelFilter}
                  onChange={(e) => setLevelFilter(e.target.value as typeof levelFilter)}
                  className="rounded border border-zinc-800 bg-zinc-900 px-1.5 py-0.5 text-[11px] text-zinc-300 outline-none"
                >
                  <option value="all">Hepsi</option>
                  <option value="critical">Critical</option>
                  <option value="error">Error+</option>
                  <option value="warning">Warning+</option>
                </select>
                <input
                  placeholder="kaynak/id/metin"
                  value={sourceFilter}
                  onChange={(e) => setSourceFilter(e.target.value)}
                  className="w-44 rounded border border-zinc-800 bg-zinc-900 px-2 py-0.5 text-[11px] text-zinc-300 outline-none placeholder:text-zinc-600"
                />
              </div>
            </div>
            <div className="max-h-[640px] overflow-y-auto">
              {filteredTimeline.length === 0 ? (
                <div className="px-3 py-10 text-center text-xs text-zinc-500">Eşleşen olay yok.</div>
              ) : (
                filteredTimeline.map((it, i) => (
                  <TimelineItem key={`${it.timeGenerated}-${it.source}-${it.eventId}-${i}`} item={it} />
                ))
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default EventLogDiagnosticsPage;
