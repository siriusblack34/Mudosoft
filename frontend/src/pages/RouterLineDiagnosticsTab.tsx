import React, { useEffect, useMemo, useState, useCallback } from "react";
import {
    apiClient, RouterClassification, RouterDiagnosticsSummary,
    RouterLineClass, RouterLatencyPoint, StoreNetworkInfo,
} from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import {
    Wifi, WifiOff, Activity, AlertTriangle, RefreshCw, X, Router, Zap, Gauge, Signal,
} from "lucide-react";
import {
    LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, ReferenceLine, CartesianGrid,
} from "recharts";

const classConfig: Record<RouterLineClass, { label: string; color: string; bg: string; border: string; icon: React.ReactNode }> = {
    Terrestrial: { label: "Karasal", color: "text-emerald-400", bg: "bg-emerald-500/10", border: "border-emerald-500/30", icon: <Wifi className="w-4 h-4" /> },
    MobileSuspected: { label: "Mobil Şüphesi", color: "text-amber-400", bg: "bg-amber-500/10", border: "border-amber-500/30", icon: <Signal className="w-4 h-4" /> },
    MobileLikely: { label: "Mobil (4.5G)", color: "text-rose-400", bg: "bg-rose-500/10", border: "border-rose-500/30", icon: <Signal className="w-4 h-4" /> },
    Unstable: { label: "Kararsız", color: "text-fuchsia-400", bg: "bg-fuchsia-500/10", border: "border-fuchsia-500/30", icon: <Zap className="w-4 h-4" /> },
    Unknown: { label: "Veri Yok", color: "text-slate-400", bg: "bg-slate-700/30", border: "border-slate-600/30", icon: <Router className="w-4 h-4" /> },
};

const fmtTime = (iso: string) => new Date(iso).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });

const RouterLineDiagnosticsTab: React.FC<{ initialStoreCode?: number | null }> = ({ initialStoreCode }) => {
    const [routers, setRouters] = useState<RouterClassification[]>([]);
    const [summary, setSummary] = useState<RouterDiagnosticsSummary | null>(null);
    const [networkInfos, setNetworkInfos] = useState<Map<number, StoreNetworkInfo>>(new Map());
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [filter, setFilter] = useState<'all' | RouterLineClass | 'mobile-any'>('mobile-any');
    const [search, setSearch] = useState("");
    const [selected, setSelected] = useState<RouterClassification | null>(null);
    const [history, setHistory] = useState<RouterLatencyPoint[] | null>(null);
    const [historyLoading, setHistoryLoading] = useState(false);

    const load = useCallback(async (silent = false) => {
        try {
            if (!silent) setLoading(true);
            setError(null);
            const [diag, infos] = await Promise.all([
                apiClient.getRouterDiagnostics(10),
                apiClient.getStoreNetworkInfo(),
            ]);
            setRouters(diag.routers);
            setSummary(diag.summary);
            setNetworkInfos(new Map(infos.map(i => [i.storeCode, i])));
        } catch (err) {
            setError(err instanceof Error ? err.message : "Veri alinamadi");
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => {
        load();
        const t = setInterval(() => load(true), 30000);
        return () => clearInterval(t);
    }, [load]);

    // initial store select
    useEffect(() => {
        if (initialStoreCode != null && routers.length > 0) {
            const r = routers.find(x => x.storeCode === initialStoreCode);
            if (r) openHistory(r);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [initialStoreCode, routers]);

    const openHistory = async (r: RouterClassification) => {
        setSelected(r);
        setHistoryLoading(true);
        try {
            const hist = await apiClient.getRouterLatencyHistory(r.storeCode, 24);
            setHistory(hist.points);
        } catch { setHistory([]); }
        finally { setHistoryLoading(false); }
    };

    const filtered = useMemo(() => {
        let list = routers;
        if (filter === 'mobile-any') {
            list = list.filter(r => r.class === 'MobileSuspected' || r.class === 'MobileLikely' || r.switchoverDetected);
        } else if (filter !== 'all') {
            list = list.filter(r => r.class === filter);
        }
        if (search.trim()) {
            const q = search.toLowerCase();
            list = list.filter(r => r.storeName.toLowerCase().includes(q) || String(r.storeCode).includes(q) || r.ip.includes(q));
        }
        // mobil > unstable > suphesi > karasal > bilinmiyor siralamasi
        const order: Record<RouterLineClass, number> = { MobileLikely: 1, Unstable: 2, MobileSuspected: 3, Terrestrial: 4, Unknown: 5 };
        return [...list].sort((a, b) => order[a.class] - order[b.class] || (b.avgRttMs ?? 0) - (a.avgRttMs ?? 0));
    }, [routers, filter, search]);

    if (loading && routers.length === 0) return <div className="flex h-[60vh] items-center justify-center"><Spinner size="lg" /></div>;

    return (
        <div className="flex flex-col gap-4">
            {error && <div className="bg-rose-500/10 border border-rose-500/30 rounded-xl p-3 text-sm text-rose-400">{error}</div>}

            {/* Summary */}
            <div className="grid grid-cols-6 gap-3">
                <SummaryBox label="Toplam" value={summary?.total ?? 0} color="sky" />
                <SummaryBox label="Karasal" value={summary?.terrestrial ?? 0} color="emerald" />
                <SummaryBox label="Mobil Şüphesi" value={summary?.mobileSuspected ?? 0} color="amber" />
                <SummaryBox label="Mobil (4.5G)" value={summary?.mobileLikely ?? 0} color="rose" />
                <SummaryBox label="Kararsız" value={summary?.unstable ?? 0} color="fuchsia" />
                <SummaryBox label="Switchover" value={summary?.switchovers ?? 0} color="amber" />
            </div>

            {/* Filter bar */}
            <div className="flex flex-wrap gap-2 items-center">
                <FilterBtn active={filter === 'mobile-any'} onClick={() => setFilter('mobile-any')} label="Mobil Şüphe/Kesin" />
                <FilterBtn active={filter === 'all'} onClick={() => setFilter('all')} label={`Hepsi (${routers.length})`} />
                <FilterBtn active={filter === 'Terrestrial'} onClick={() => setFilter('Terrestrial')} label="Karasal" />
                <FilterBtn active={filter === 'MobileSuspected'} onClick={() => setFilter('MobileSuspected')} label="Mobil Şüphesi" />
                <FilterBtn active={filter === 'MobileLikely'} onClick={() => setFilter('MobileLikely')} label="Mobil (4.5G)" />
                <FilterBtn active={filter === 'Unstable'} onClick={() => setFilter('Unstable')} label="Kararsız" />
                <FilterBtn active={filter === 'Unknown'} onClick={() => setFilter('Unknown')} label="Veri Yok" />
                <input
                    value={search}
                    onChange={e => setSearch(e.target.value)}
                    placeholder="Mağaza ara..."
                    className="ml-auto px-3 py-1.5 rounded-lg text-xs bg-slate-900/60 border border-slate-700/50 text-white placeholder:text-slate-500 w-48"
                />
                <button onClick={() => load()} className="p-1.5 bg-slate-800 hover:bg-slate-700 rounded-lg border border-slate-700/50" title="Yenile">
                    <RefreshCw className={`w-4 h-4 text-amber-500 ${loading ? 'animate-spin' : ''}`} />
                </button>
            </div>

            {/* Main layout */}
            <div className="flex gap-4">
                {/* Table */}
                <div className="flex-1 min-w-0 rounded-2xl border border-slate-700/50 bg-slate-900/40 overflow-hidden">
                    <div className="overflow-auto max-h-[calc(100vh-380px)] scrollbar-thin scrollbar-thumb-slate-700">
                        <table className="w-full text-xs">
                            <thead className="sticky top-0 bg-slate-900/90 backdrop-blur text-slate-400 uppercase text-[10px]">
                                <tr>
                                    <th className="text-left px-3 py-2">Mağaza</th>
                                    <th className="text-left px-3 py-2">Sınıf</th>
                                    <th className="text-right px-3 py-2">Mbps</th>
                                    <th className="text-right px-3 py-2">Ort. RTT</th>
                                    <th className="text-right px-3 py-2">p50</th>
                                    <th className="text-right px-3 py-2">p95</th>
                                    <th className="text-right px-3 py-2">Jitter</th>
                                    <th className="text-right px-3 py-2">Başarı</th>
                                    <th className="text-right px-3 py-2">Örnek</th>
                                    <th className="text-left px-3 py-2">Son</th>
                                </tr>
                            </thead>
                            <tbody>
                                {filtered.length === 0 ? (
                                    <tr><td colSpan={10} className="text-center py-10 text-slate-500">Filtreye uyan router yok</td></tr>
                                ) : filtered.map(r => {
                                    const cfg = classConfig[r.class];
                                    const info = networkInfos.get(r.storeCode);
                                    const isSel = selected?.deviceId === r.deviceId;
                                    return (
                                        <tr key={r.deviceId}
                                            onClick={() => openHistory(r)}
                                            className={`border-t border-slate-800/50 cursor-pointer hover:bg-slate-800/40 ${isSel ? 'bg-slate-800/60' : ''}`}>
                                            <td className="px-3 py-2">
                                                <div className="font-bold text-white">[{r.storeCode}] {r.storeName}</div>
                                                <div className="text-[10px] text-slate-500 font-mono">{r.ip}</div>
                                            </td>
                                            <td className="px-3 py-2">
                                                <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold ${cfg.bg} ${cfg.color} border ${cfg.border}`}>
                                                    {cfg.icon} {cfg.label}
                                                </span>
                                                {r.switchoverDetected && (
                                                    <span className="ml-1 inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[9px] font-bold bg-amber-500/20 text-amber-400 border border-amber-500/30">
                                                        SWITCH
                                                    </span>
                                                )}
                                            </td>
                                            <td className="px-3 py-2 text-right font-mono text-slate-300">
                                                {info?.terrestrialMbps ? `${info.terrestrialMbps}` : <span className="text-slate-600">—</span>}
                                            </td>
                                            <td className="px-3 py-2 text-right font-mono">{rttCell(r.avgRttMs)}</td>
                                            <td className="px-3 py-2 text-right font-mono text-slate-400">{r.p50Ms > 0 ? r.p50Ms.toFixed(0) : '—'}</td>
                                            <td className="px-3 py-2 text-right font-mono text-slate-400">{r.p95Ms > 0 ? r.p95Ms.toFixed(0) : '—'}</td>
                                            <td className="px-3 py-2 text-right font-mono text-slate-400">{r.stdDevMs > 0 ? r.stdDevMs.toFixed(0) : '—'}</td>
                                            <td className="px-3 py-2 text-right font-mono">{(r.successRate * 100).toFixed(0)}%</td>
                                            <td className="px-3 py-2 text-right font-mono text-slate-500">{r.sampleCount}</td>
                                            <td className="px-3 py-2 text-slate-500 text-[10px]">{r.lastSampleAt ? fmtTime(r.lastSampleAt) : '—'}</td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>
                </div>

                {/* Detail panel */}
                {selected && (
                    <div className="w-[480px] shrink-0 bg-slate-900/60 border border-slate-700/50 rounded-2xl p-4 max-h-[calc(100vh-380px)] overflow-auto scrollbar-thin scrollbar-thumb-slate-700">
                        <div className="flex items-start justify-between mb-3">
                            <div>
                                <h3 className="text-sm font-bold text-white">[{selected.storeCode}] {selected.storeName}</h3>
                                <p className="text-[11px] text-slate-500 font-mono">{selected.ip}</p>
                            </div>
                            <button onClick={() => { setSelected(null); setHistory(null); }} className="p-1 text-slate-400 hover:text-white rounded hover:bg-slate-800">
                                <X className="w-4 h-4" />
                            </button>
                        </div>

                        {/* Class + reason */}
                        {(() => {
                            const sc = classConfig[selected.class] ?? classConfig.Unknown;
                            return (
                        <div className={`rounded-xl border p-3 mb-3 ${sc.bg} ${sc.border}`}>
                            <div className={`flex items-center gap-2 text-sm font-bold ${sc.color}`}>
                                {sc.icon}
                                {sc.label}
                            </div>
                            <p className="text-xs text-slate-300 mt-1">{selected.reason}</p>
                            {selected.switchoverDetected && selected.prevAvgRttMs != null && selected.avgRttMs != null && (
                                <div className="mt-2 text-[11px] text-amber-400 flex items-center gap-1">
                                    <AlertTriangle className="w-3 h-3" />
                                    Önceki 10dk ort: {selected.prevAvgRttMs}ms → şimdi: {selected.avgRttMs}ms
                                </div>
                            )}
                        </div>
                            );
                        })()}

                        {/* Metrics */}
                        <div className="grid grid-cols-2 gap-2 mb-3">
                            <Metric label="Ortalama RTT" value={selected.avgRttMs != null ? `${selected.avgRttMs}ms` : '—'} />
                            <Metric label="p50 / p95" value={`${selected.p50Ms.toFixed(0)} / ${selected.p95Ms.toFixed(0)}ms`} />
                            <Metric label="Jitter (std)" value={`${selected.stdDevMs.toFixed(0)}ms`} />
                            <Metric label="Başarı Oranı" value={`${(selected.successRate * 100).toFixed(0)}%`} />
                            <Metric label="Örnek Sayısı" value={`${selected.sampleCount}`} />
                            <Metric label="Karasal Hat" value={networkInfos.get(selected.storeCode)?.terrestrialMbps ? `${networkInfos.get(selected.storeCode)!.terrestrialMbps} Mbps` : '—'} />
                        </div>

                        {/* Chart */}
                        <div className="text-[11px] font-bold text-slate-400 uppercase mb-2">Son 24 Saat RTT</div>
                        {historyLoading ? (
                            <div className="flex items-center justify-center h-40"><Spinner size="md" /></div>
                        ) : history && history.length > 0 ? (
                            <div className="h-56 bg-slate-950/40 rounded-xl p-2">
                                <ResponsiveContainer width="100%" height="100%">
                                    <LineChart data={history.map(p => ({
                                        t: new Date(p.sampledAt).getTime(),
                                        rtt: p.success ? p.rttMs : null,
                                    }))}>
                                        <CartesianGrid stroke="#334155" strokeDasharray="3 3" opacity={0.3} />
                                        <XAxis dataKey="t" type="number" domain={['dataMin', 'dataMax']}
                                               tickFormatter={(ts) => new Date(ts).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}
                                               stroke="#64748b" fontSize={10} />
                                        <YAxis stroke="#64748b" fontSize={10} width={40} />
                                        <Tooltip
                                            contentStyle={{ background: '#0f172a', border: '1px solid #334155', borderRadius: 8, fontSize: 11 }}
                                            labelFormatter={(ts) => new Date(ts as number).toLocaleString('tr-TR')}
                                            formatter={(v: number | null) => v == null ? ['timeout', 'RTT'] : [`${v}ms`, 'RTT']}
                                        />
                                        <ReferenceLine y={120} stroke="#34d399" strokeDasharray="4 4" label={{ value: 'karasal', position: 'right', fill: '#34d399', fontSize: 9 }} />
                                        <ReferenceLine y={250} stroke="#f43f5e" strokeDasharray="4 4" label={{ value: 'mobil', position: 'right', fill: '#f43f5e', fontSize: 9 }} />
                                        <Line type="monotone" dataKey="rtt" stroke="#fbbf24" strokeWidth={1.5} dot={false} connectNulls={false} />
                                    </LineChart>
                                </ResponsiveContainer>
                            </div>
                        ) : (
                            <p className="text-xs text-slate-500 text-center py-6">Geçmiş veri yok</p>
                        )}
                    </div>
                )}
            </div>
        </div>
    );
};

const rttCell = (ms: number | null) => {
    if (ms == null) return <span className="text-slate-600">—</span>;
    let cls = 'text-emerald-400';
    if (ms >= 250) cls = 'text-rose-400';
    else if (ms >= 150) cls = 'text-amber-400';
    return <span className={`font-bold ${cls}`}>{ms}ms</span>;
};

const SummaryBox: React.FC<{ label: string; value: number; color: string }> = ({ label, value, color }) => {
    const colors: Record<string, string> = {
        sky: 'text-sky-400', emerald: 'text-emerald-400', amber: 'text-amber-400',
        rose: 'text-rose-400', fuchsia: 'text-fuchsia-400',
    };
    return (
        <div className="bg-slate-900/40 border border-slate-700/50 rounded-xl p-3">
            <div className="text-[10px] font-bold uppercase tracking-widest text-slate-500">{label}</div>
            <div className={`text-2xl font-black mt-1 ${colors[color] ?? 'text-white'}`}>{value}</div>
        </div>
    );
};

const FilterBtn: React.FC<{ active: boolean; onClick: () => void; label: string }> = ({ active, onClick, label }) => (
    <button onClick={onClick}
        className={`px-3 py-1.5 rounded-lg text-xs font-bold transition-colors border ${active ? 'bg-amber-500/20 text-amber-400 border-amber-500/30' : 'text-slate-400 hover:text-white border-transparent hover:bg-slate-800'}`}>
        {label}
    </button>
);

const Metric: React.FC<{ label: string; value: string }> = ({ label, value }) => (
    <div className="bg-slate-800/40 border border-slate-700/50 rounded-lg p-2">
        <div className="text-[9px] uppercase text-slate-500 font-bold">{label}</div>
        <div className="text-sm font-bold text-white mt-0.5 font-mono">{value}</div>
    </div>
);

export default RouterLineDiagnosticsTab;
