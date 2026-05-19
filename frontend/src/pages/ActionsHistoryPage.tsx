import React, { useEffect, useState, useCallback } from 'react';
import { apiClient } from '../lib/apiClient';
import type { CommandHistoryItem, ActivityLogEntry } from '../lib/apiClient';
import StatusPill from '../components/common/StatusPill';
import Modal from '../components/ui/Modal';
import { formatDistanceToNow } from 'date-fns';
import { RefreshCw, CheckCircle, XCircle, History, Activity, ChevronLeft, ChevronRight, Search, Wrench } from 'lucide-react';

type Tab = 'commands' | 'activity' | 'services';

const ActionsHistoryPage: React.FC = () => {
  const [tab, setTab] = useState<Tab>('commands');

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-indigo-500 to-violet-600 flex items-center justify-center shadow-lg shadow-indigo-500/30">
          <History className="w-5 h-5 text-white" />
        </div>
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">İşlem Geçmişi</h1>
          <p className="text-[11px] text-slate-500 mt-0.5">Komutlar ve audit kayıtları</p>
        </div>
      </div>

      <div className="border-b border-ms-border flex gap-1">
        {([
          ['commands', <History className="h-4 w-4" key="i1" />, 'Komutlar (Agent)'],
          ['activity', <Activity className="h-4 w-4" key="i2" />, 'Sistem İşlemleri (Audit)'],
          ['services', <Wrench className="h-4 w-4" key="i3" />, 'Servis Müdahaleleri'],
        ] as Array<[Tab, React.ReactNode, string]>).map(([t, icon, label]) => (
          <button key={t} onClick={() => setTab(t)}
            className={`flex items-center gap-2 px-4 py-2 text-sm border-b-2 -mb-px transition-colors ${tab === t
              ? 'border-violet-500 text-violet-400 font-medium'
              : 'border-transparent text-ms-text-muted hover:text-ms-text'}`}>
            {icon} {label}
          </button>
        ))}
      </div>

      {tab === 'commands' ? <CommandsTab /> : tab === 'activity' ? <ActivityTab /> : <ServiceActionsTab />}
    </div>
  );
};

// ── Commands tab (eski içerik) ──────────────────────────────────────────

const CommandsTab: React.FC = () => {
  const [history, setHistory] = useState<CommandHistoryItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedOutput, setSelectedOutput] = useState<string | null>(null);
  const [outputLoading, setOutputLoading] = useState(false);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [lastRefreshed, setLastRefreshed] = useState<Date | null>(null);

  const loadHistory = useCallback(async () => {
    try {
      const res = await apiClient.getCommandHistory();
      setHistory(res);
      setLastRefreshed(new Date());
    } catch (err) {
      console.error('Komut gecmisi yuklenirken hata:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { loadHistory(); }, [loadHistory]);

  useEffect(() => {
    if (!autoRefresh) return;
    const interval = setInterval(loadHistory, 15000);
    return () => clearInterval(interval);
  }, [autoRefresh, loadHistory]);

  const viewDetails = async (commandId: string) => {
    setOutputLoading(true);
    setSelectedOutput(null);
    try {
      const res = await apiClient.getCommandDetails(commandId);
      setSelectedOutput(res.output || 'Komut çıktısı boş.');
    } catch {
      setSelectedOutput('Hata: Tam çıktı yüklenemedi.');
    } finally {
      setOutputLoading(false);
    }
  };

  const successCount = history.filter(h => h.success).length;
  const failCount = history.filter(h => !h.success).length;

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <div className="w-8 h-8 border-2 border-violet-500 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-[11px] text-slate-500">{lastRefreshed && `Son guncelleme: ${lastRefreshed.toLocaleTimeString('tr-TR')}`}</p>
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2">
            <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-emerald-500/10 border border-emerald-500/20">
              <CheckCircle className="w-3.5 h-3.5 text-emerald-400" />
              <span className="text-xs font-bold text-emerald-400">{successCount}</span>
            </div>
            <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-rose-500/10 border border-rose-500/20">
              <XCircle className="w-3.5 h-3.5 text-rose-400" />
              <span className="text-xs font-bold text-rose-400">{failCount}</span>
            </div>
          </div>
          <button
            onClick={() => setAutoRefresh(prev => !prev)}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all border ${autoRefresh
              ? 'bg-violet-500/10 border-violet-500/20 text-violet-400'
              : 'bg-white/5 border-white/10 text-slate-400'}`}
          >
            <RefreshCw className={`w-3.5 h-3.5 ${autoRefresh ? 'animate-spin' : ''}`} style={autoRefresh ? { animationDuration: '3s' } : undefined} />
            {autoRefresh ? 'Auto' : 'Paused'}
          </button>
          <button onClick={loadHistory} className="p-2 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-all active:scale-95">
            <RefreshCw className="w-4 h-4 text-violet-400" />
          </button>
        </div>
      </div>

      <div className="overflow-hidden rounded-2xl border-white/5 glass-card shadow-xl">
        <table className="w-full text-sm">
          <thead className="bg-white/5 text-slate-400 uppercase tracking-widest text-xs font-semibold border-b border-white/5">
            <tr>
              <th className="text-left px-5 py-4 w-1/5">Hostname</th>
              <th className="text-left px-5 py-4 w-1/10">Type</th>
              <th className="text-left px-5 py-4 w-1/10">Status</th>
              <th className="text-left px-5 py-4 w-1/5">Completed</th>
              <th className="text-left px-5 py-4 w-2/5">Output Snippet</th>
              <th className="text-left px-5 py-4 w-1/12"></th>
            </tr>
          </thead>
          <tbody>
            {history.length > 0 ? (
              history.map((item) => (
                <tr key={item.commandId} className={`border-t border-slate-700/50 hover:bg-slate-800/30 transition-colors ${!item.success ? 'bg-rose-500/5 border-l-2 border-l-rose-500/50' : ''}`}>
                  <td className="px-5 py-3 font-medium text-white">{item.hostname}</td>
                  <td className="px-5 py-3 text-slate-400">{item.typeName}</td>
                  <td className="px-5 py-3">
                    <StatusPill tone={item.success ? 'success' : 'danger'} text={item.success ? 'SUCCESS' : 'FAILED'} />
                  </td>
                  <td className="px-5 py-3 text-slate-400">{formatDistanceToNow(new Date(item.completedAtUtc), { addSuffix: true })}</td>
                  <td className="px-5 py-3 font-mono text-xs text-slate-500 truncate max-w-[200px]" title={item.outputSnippet}>{item.outputSnippet}</td>
                  <td className="px-5 py-3">
                    <button onClick={() => viewDetails(item.commandId)} className="text-xs text-indigo-400 hover:text-indigo-300 font-medium disabled:opacity-50 transition-colors" disabled={outputLoading}>
                      {outputLoading ? 'Loading...' : 'Details'}
                    </button>
                  </td>
                </tr>
              ))
            ) : (
              <tr><td colSpan={6} className="px-5 py-12 text-center text-slate-500 text-sm">No command history found 🎉</td></tr>
            )}
          </tbody>
        </table>
      </div>

      <Modal isOpen={selectedOutput !== null} onClose={() => setSelectedOutput(null)} title="Command Output Details">
        <pre className="bg-gray-800 p-4 rounded-lg text-white whitespace-pre-wrap text-xs font-mono max-h-96 overflow-y-auto">
          {outputLoading ? 'Loading command output...' : selectedOutput}
        </pre>
        <button onClick={() => setSelectedOutput(null)} className="mt-4 px-4 py-2 text-sm font-medium rounded-lg text-white bg-ms-primary hover:bg-ms-primary-dark">Close</button>
      </Modal>
    </div>
  );
};

// ── Activity tab (audit) ────────────────────────────────────────────────

const ServiceActionsTab: React.FC = () => {
  const [items, setItems] = useState<ActivityLogEntry[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const pageSize = 100;
  const [search, setSearch] = useState('');
  const [failuresOnly, setFailuresOnly] = useState(false);
  const [selected, setSelected] = useState<ActivityLogEntry | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [lastRefreshed, setLastRefreshed] = useState<Date | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await apiClient.getActivityLog({
        category: 'ServiceMonitor',
        search: search || undefined,
        failuresOnly: failuresOnly || undefined,
        page,
        pageSize,
      });
      setItems(res.items);
      setTotal(res.total);
      setLastRefreshed(new Date());
    } finally {
      setLoading(false);
    }
  }, [search, failuresOnly, page]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    if (!autoRefresh) return;
    const interval = setInterval(load, 15000);
    return () => clearInterval(interval);
  }, [autoRefresh, load]);

  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const successCount = items.filter(i => i.success).length;
  const failCount = items.filter(i => !i.success).length;

  return (
    <div className="space-y-3">
      <div className="card">
        <div className="flex items-center justify-between mb-3">
          <p className="text-[11px] text-ms-text-muted">
            {lastRefreshed ? `Son guncelleme: ${lastRefreshed.toLocaleTimeString('tr-TR')}` : 'Son guncelleme bekleniyor'}
          </p>
          <button
            onClick={() => setAutoRefresh(prev => !prev)}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all border ${autoRefresh
              ? 'bg-cyan-500/10 border-cyan-500/20 text-cyan-300'
              : 'bg-white/5 border-white/10 text-slate-400'}`}
          >
            <RefreshCw className={`w-3.5 h-3.5 ${autoRefresh ? 'animate-spin' : ''}`} style={autoRefresh ? { animationDuration: '3s' } : undefined} />
            {autoRefresh ? 'Auto' : 'Paused'}
          </button>
        </div>
        <div className="flex flex-wrap gap-2 items-center">
          <div className="relative">
            <Search className="h-4 w-4 absolute left-3 top-1/2 -translate-y-1/2 text-ms-text-muted" />
            <input className="!pl-9 w-72" placeholder="Magaza / cihaz / servis ara..."
              value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
          </div>
          <label className="flex items-center gap-2 text-sm text-ms-text cursor-pointer">
            <input type="checkbox" checked={failuresOnly}
              onChange={(e) => { setFailuresOnly(e.target.checked); setPage(1); }}
              className="accent-rose-500" />
            Sadece basarisiz
          </label>
          <div className="ml-auto flex items-center gap-2">
            <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-emerald-500/10 border border-emerald-500/20">
              <CheckCircle className="w-3.5 h-3.5 text-emerald-400" />
              <span className="text-xs font-bold text-emerald-400">{successCount}</span>
            </div>
            <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-rose-500/10 border border-rose-500/20">
              <XCircle className="w-3.5 h-3.5 text-rose-400" />
              <span className="text-xs font-bold text-rose-400">{failCount}</span>
            </div>
            <button onClick={load} className="btn-secondary !px-3">
              <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} /> Yenile
            </button>
          </div>
        </div>
      </div>

      <div className="card !p-0 overflow-hidden">
        <table className="data-table">
          <thead>
            <tr>
              <th className="w-44">Tarih</th>
              <th>Magaza</th>
              <th className="w-32">Cihaz</th>
              <th>Servis</th>
              <th className="w-36">Durum</th>
              <th className="w-32">Sonuc</th>
              <th>Mesaj</th>
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr><td colSpan={7} className="text-center !py-8 text-ms-text-muted">Yukleniyor...</td></tr>
            )}
            {!loading && items.length === 0 && (
              <tr><td colSpan={7} className="text-center !py-8 text-ms-text-muted">Servis mudahalesi bulunamadi</td></tr>
            )}
            {!loading && items.map(a => {
              const d = parseServiceActionDetails(a.details);
              const store = d.StoreCode ? `[${d.StoreCode}] ${d.StoreName ?? ''}` : '-';
              const service = d.DisplayName || d.ServiceName || a.target || '-';
              const transition = `${d.PreviousStatus ?? '-'} -> ${d.CurrentStatus ?? '-'}`;
              return (
                <tr key={a.id} onClick={() => setSelected(a)} className="cursor-pointer hover:bg-ms-border/30 transition-colors">
                  <td className="text-xs text-ms-text-muted">{new Date(a.createdAt).toLocaleString('tr-TR')}</td>
                  <td className="font-medium text-ms-text">{store}</td>
                  <td className="text-xs text-ms-text-muted">{d.DeviceName || d.DeviceId || '-'}</td>
                  <td>
                    <div className="font-medium text-ms-text">{service}</div>
                    <div className="text-[11px] text-ms-text-muted">{d.IpAddress || ''}</div>
                  </td>
                  <td className="text-xs">
                    <span className="font-mono text-ms-text-muted">{transition}</span>
                  </td>
                  <td>
                    <StatusPill tone={a.success ? 'success' : 'danger'} text={a.success ? 'STARTED' : 'FAILED'} />
                  </td>
                  <td className="text-xs text-ms-text-muted truncate max-w-[360px]" title={a.errorMessage || d.StartMessage || ''}>
                    {a.errorMessage ? <span className="text-rose-300">{a.errorMessage}</span> : (d.StartMessage || 'Otomatik baslatildi')}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>

        <div className="flex items-center justify-between px-4 py-3 border-t border-ms-border text-sm">
          <div className="text-ms-text-muted">
            Toplam <strong className="text-ms-text">{total.toLocaleString('tr-TR')}</strong> - Sayfa {page}/{totalPages}
          </div>
          <div className="flex gap-1">
            <button disabled={page <= 1} onClick={() => setPage(p => p - 1)} className="btn-secondary !px-2 !py-1 disabled:opacity-40">
              <ChevronLeft className="h-4 w-4" />
            </button>
            <button disabled={page >= totalPages} onClick={() => setPage(p => p + 1)} className="btn-secondary !px-2 !py-1 disabled:opacity-40">
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      </div>

      <Modal isOpen={selected !== null} onClose={() => setSelected(null)} title="Servis Mudahale Detayi">
        {selected && (() => {
          const d = parseServiceActionDetails(selected.details);
          return (
            <div className="space-y-2 text-sm">
              <Row label="Tarih" value={new Date(selected.createdAt).toLocaleString('tr-TR')} />
              <Row label="Magaza" value={d.StoreCode ? `[${d.StoreCode}] ${d.StoreName ?? ''}` : '-'} />
              <Row label="Cihaz" value={d.DeviceName || d.DeviceId || '-'} />
              <Row label="IP" value={d.IpAddress || '-'} />
              <Row label="Servis" value={d.DisplayName || d.ServiceName || '-'} />
              <Row label="StartMode" value={d.StartMode || '-'} />
              <Row label="Durum" value={`${d.PreviousStatus ?? '-'} -> ${d.CurrentStatus ?? '-'}`} />
              <Row label="ReturnCode" value={d.StartReturnCode != null ? String(d.StartReturnCode) : '-'} />
              <Row label="Sonuc" value={selected.success ? 'Basarili' : 'Basarisiz'} />
              {(d.StartMessage || selected.errorMessage) && (
                <div>
                  <div className="text-xs text-ms-text-muted mb-1">Mesaj</div>
                  <pre className="bg-ms-bg p-3 rounded text-xs whitespace-pre-wrap font-mono">{selected.errorMessage || d.StartMessage}</pre>
                </div>
              )}
            </div>
          );
        })()}
      </Modal>
    </div>
  );
};

const categoryColors: Record<string, string> = {
  Cleanup: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
  Inventory: 'bg-violet-500/15 text-violet-300 border-violet-500/30',
  RemoteInstall: 'bg-indigo-500/15 text-indigo-300 border-indigo-500/30',
  ServiceMonitor: 'bg-cyan-500/15 text-cyan-300 border-cyan-500/30',
  Settings: 'bg-slate-500/15 text-slate-300 border-slate-500/30',
};

interface ServiceActionDetails {
  DeviceId?: string;
  StoreCode?: number;
  StoreName?: string;
  DeviceName?: string;
  IpAddress?: string;
  ServiceName?: string;
  DisplayName?: string;
  PreviousStatus?: string;
  CurrentStatus?: string;
  StartMode?: string;
  StartReturnCode?: number;
  StartMessage?: string;
  Action?: string;
}

function parseServiceActionDetails(details?: string | null): ServiceActionDetails {
  if (!details) return {};
  try { return JSON.parse(details) as ServiceActionDetails; }
  catch { return {}; }
}

const ActivityTab: React.FC = () => {
  const [items, setItems] = useState<ActivityLogEntry[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const pageSize = 100;
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [failuresOnly, setFailuresOnly] = useState(false);
  const [selected, setSelected] = useState<ActivityLogEntry | null>(null);
  const [categories, setCategories] = useState<Array<{ name: string; count: number }>>([]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await apiClient.getActivityLog({
        search: search || undefined,
        category: category || undefined,
        failuresOnly: failuresOnly || undefined,
        page, pageSize,
      });
      setItems(res.items);
      setTotal(res.total);
    } finally { setLoading(false); }
  }, [search, category, failuresOnly, page]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { apiClient.getActivityLogCategories().then(setCategories).catch(() => { }); }, []);

  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  return (
    <div className="space-y-3">
      <div className="card">
        <div className="flex flex-wrap gap-2 items-center">
          <div className="relative">
            <Search className="h-4 w-4 absolute left-3 top-1/2 -translate-y-1/2 text-ms-text-muted" />
            <input className="!pl-9 w-72" placeholder="Action / target / detay…"
              value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
          </div>
          <select value={category}
            onChange={(e) => { setCategory(e.target.value); setPage(1); }}
            className="bg-ms-bg border border-ms-border text-ms-text rounded-lg px-3 py-2 text-sm">
            <option value="">Tüm Kategoriler</option>
            {categories.map(c => <option key={c.name} value={c.name}>{c.name} ({c.count})</option>)}
          </select>
          <label className="flex items-center gap-2 text-sm text-ms-text cursor-pointer">
            <input type="checkbox" checked={failuresOnly}
              onChange={(e) => { setFailuresOnly(e.target.checked); setPage(1); }}
              className="accent-rose-500" />
            Sadece hatalar
          </label>
          <button onClick={load} className="btn-secondary !px-3 ml-auto">
            <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} /> Yenile
          </button>
        </div>
      </div>

      <div className="card !p-0 overflow-hidden">
        <table className="data-table">
          <thead>
            <tr>
              <th className="w-44">Tarih</th>
              <th className="w-32">Kullanıcı</th>
              <th className="w-32">Kategori</th>
              <th>Action</th>
              <th>Hedef</th>
              <th>Detay / Hata</th>
              <th className="w-20">Durum</th>
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr><td colSpan={7} className="text-center !py-8 text-ms-text-muted">Yükleniyor…</td></tr>
            )}
            {!loading && items.length === 0 && (
              <tr><td colSpan={7} className="text-center !py-8 text-ms-text-muted">Kayıt bulunamadı</td></tr>
            )}
            {!loading && items.map(a => {
              const catCls = categoryColors[a.category] ?? 'bg-slate-500/15 text-slate-300 border-slate-500/30';
              return (
                <tr key={a.id} onClick={() => setSelected(a)} className="cursor-pointer hover:bg-ms-border/30 transition-colors">
                  <td className="text-xs text-ms-text-muted">{new Date(a.createdAt).toLocaleString('tr-TR')}</td>
                  <td className="text-xs">{a.username || '—'}</td>
                  <td><span className={`px-2 py-0.5 rounded text-[11px] border ${catCls}`}>{a.category}</span></td>
                  <td className="font-medium text-ms-text">{a.action}</td>
                  <td className="text-xs text-ms-text-muted truncate max-w-[200px]" title={a.target ?? ''}>{a.target || '—'}</td>
                  <td className="text-xs text-ms-text-muted truncate max-w-[400px]" title={a.errorMessage || a.details || ''}>
                    {a.errorMessage ? <span className="text-rose-300">{a.errorMessage}</span> : (a.details || '—')}
                  </td>
                  <td>
                    <StatusPill tone={a.success ? 'success' : 'danger'} text={a.success ? 'OK' : 'FAIL'} />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>

        <div className="flex items-center justify-between px-4 py-3 border-t border-ms-border text-sm">
          <div className="text-ms-text-muted">
            Toplam <strong className="text-ms-text">{total.toLocaleString('tr-TR')}</strong> — Sayfa {page}/{totalPages}
          </div>
          <div className="flex gap-1">
            <button disabled={page <= 1} onClick={() => setPage(p => p - 1)} className="btn-secondary !px-2 !py-1 disabled:opacity-40">
              <ChevronLeft className="h-4 w-4" />
            </button>
            <button disabled={page >= totalPages} onClick={() => setPage(p => p + 1)} className="btn-secondary !px-2 !py-1 disabled:opacity-40">
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      </div>

      <Modal isOpen={selected !== null} onClose={() => setSelected(null)} title="İşlem Detayı">
        {selected && (
          <div className="space-y-2 text-sm">
            <Row label="Tarih" value={new Date(selected.createdAt).toLocaleString('tr-TR')} />
            <Row label="Kullanıcı" value={selected.username || '—'} />
            <Row label="Kategori" value={selected.category} />
            <Row label="Action" value={selected.action} />
            <Row label="Hedef" value={selected.target || '—'} />
            <Row label="Durum" value={selected.success ? '✓ Başarılı' : '✗ Başarısız'} />
            {selected.details && (
              <div>
                <div className="text-xs text-ms-text-muted mb-1">Detay</div>
                <pre className="bg-ms-bg p-3 rounded text-xs whitespace-pre-wrap font-mono">{selected.details}</pre>
              </div>
            )}
            {selected.errorMessage && (
              <div>
                <div className="text-xs text-rose-300 mb-1">Hata</div>
                <pre className="bg-rose-500/10 border border-rose-500/30 p-3 rounded text-xs whitespace-pre-wrap font-mono text-rose-200">{selected.errorMessage}</pre>
              </div>
            )}
          </div>
        )}
      </Modal>
    </div>
  );
};

const Row: React.FC<{ label: string; value: string }> = ({ label, value }) => (
  <div className="flex gap-3">
    <span className="text-xs text-ms-text-muted w-24 shrink-0">{label}</span>
    <span className="text-sm text-ms-text">{value}</span>
  </div>
);

export default ActionsHistoryPage;
