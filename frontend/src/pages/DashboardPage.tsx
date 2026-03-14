import React, { useEffect, useState, useMemo } from "react";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import {
    Monitor, Wifi, WifiOff, RefreshCw, Activity,
    Server, MonitorSmartphone, AlertTriangle,
    PauseCircle, ChevronRight, Zap, Shield, Clock
} from "lucide-react";

interface DashboardData {
    totalDevices: number;
    online: number;
    offline: number;
    healthy: number;
    warning: number;
    critical: number;
}

const SQL_CACHE_KEY = 'ms_sql_devices_cache_v2';

function formatOfflineDuration(isoStr: string): string {
    const diff = Date.now() - new Date(isoStr).getTime();
    const mins = Math.floor(diff / 60000);
    const hours = Math.floor(mins / 60);
    const days = Math.floor(hours / 24);
    if (days > 0) return `${days}g`;
    if (hours > 0) return `${hours}sa`;
    if (mins > 0) return `${mins}dk`;
    return 'az önce';
}

// ─── Animated pulse ring ───
const PulseRing = ({ color, delay = 0 }: { color: string; delay?: number }) => (
    <span
        className={`absolute inset-0 rounded-full border ${color} animate-ping opacity-20`}
        style={{ animationDuration: '3s', animationDelay: `${delay}s` }}
    />
);

// ─── Animated count ───
const AnimatedNumber = ({ value, className }: { value: number; className?: string }) => {
    const [display, setDisplay] = useState(0);
    useEffect(() => {
        if (value === display) return;
        const step = value > display ? 1 : -1;
        const timer = setInterval(() => {
            setDisplay(prev => {
                const next = prev + step;
                if ((step > 0 && next >= value) || (step < 0 && next <= value)) {
                    clearInterval(timer);
                    return value;
                }
                return next;
            });
        }, 20);
        return () => clearInterval(timer);
    }, [value]);
    return <span className={className}>{display}</span>;
};

// ─── Progress Arc SVG ───
const ProgressArc = ({ percent, size = 80, stroke = 4, color }: { percent: number; size?: number; stroke?: number; color: string }) => {
    const r = (size - stroke) / 2;
    const circumference = 2 * Math.PI * r;
    const offset = circumference - (percent / 100) * circumference;
    return (
        <svg width={size} height={size} className="-rotate-90">
            <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="#1e293b" strokeWidth={stroke} />
            <circle
                cx={size / 2} cy={size / 2} r={r} fill="none"
                stroke={color} strokeWidth={stroke}
                strokeDasharray={circumference}
                strokeDashoffset={offset}
                strokeLinecap="round"
                className="transition-all duration-1000 ease-out"
            />
        </svg>
    );
};

const DashboardPage: React.FC = () => {
    const [data, setData] = useState<DashboardData | null>(null);
    const [sqlDevices, setSqlDevices] = useState<SqlDeviceWithStatus[]>(() => {
        try {
            const cached = localStorage.getItem(SQL_CACHE_KEY);
            return cached ? JSON.parse(cached) : [];
        } catch { return []; }
    });
    const [loading, setLoading] = useState(true);
    const [sqlLoading, setSqlLoading] = useState(true);
    const [lastUpdated, setLastUpdated] = useState<Date>(new Date());
    const [tick, setTick] = useState(0);

    // Live clock tick every second
    useEffect(() => {
        const t = setInterval(() => setTick(p => p + 1), 1000);
        return () => clearInterval(t);
    }, []);

    const loadDashboard = async () => {
        try {
            const res = await apiClient.getDashboard();
            setData({ totalDevices: res.totalDevices, online: res.online, offline: res.offline, healthy: res.healthy, warning: res.warning, critical: res.critical });
            setLastUpdated(new Date());
        } catch (err) { console.error("Dashboard load failed:", err); }
        finally { setLoading(false); }
    };

    const loadSqlDevices = async () => {
        setSqlLoading(true);
        try {
            const devices = await apiClient.getSqlDevicesWithStatus({ timeoutMs: 3000 });
            setSqlDevices(devices);
            localStorage.setItem(SQL_CACHE_KEY, JSON.stringify(devices));
        } catch (err) { console.error("SQL devices load failed:", err); }
        finally { setSqlLoading(false); }
    };

    useEffect(() => {
        loadDashboard();
        loadSqlDevices();
        const interval = setInterval(() => { loadDashboard(); loadSqlDevices(); }, 30000);
        return () => clearInterval(interval);
    }, []);

    // ─── Computed stats ───
    const pcDevices = sqlDevices.filter(d => d.deviceType?.toUpperCase() === 'PC');
    const posDevices = sqlDevices.filter(d => d.deviceType?.toUpperCase() !== 'PC');
    const posClosed = posDevices.filter(d => d.isTemporarilyClosed).length;
    const pcOnline = pcDevices.filter(d => d.isOnline).length;
    const posOnline = posDevices.filter(d => d.isOnline && !d.isTemporarilyClosed).length;
    const posOffline = posDevices.filter(d => !d.isOnline && !d.isTemporarilyClosed).length;
    const totalSqlOnline = pcOnline + posOnline;
    const sqlPercent = sqlDevices.length > 0 ? Math.round((totalSqlOnline / (sqlDevices.length - posClosed)) * 100) : 0;
    const agentPercent = data && data.totalDevices > 0 ? Math.round((data.online / data.totalDevices) * 100) : 0;

    // Offline stores (geçici kapalılar hariç)
    const offlineStoreAlerts = useMemo(() => {
        const storeMap = new Map<number, { storeName: string; kasalar: SqlDeviceWithStatus[] }>();
        for (const d of sqlDevices) {
            if (d.deviceType?.toUpperCase() === 'PC' || d.isTemporarilyClosed) continue;
            if (!storeMap.has(d.storeCode)) storeMap.set(d.storeCode, { storeName: d.storeName, kasalar: [] });
            storeMap.get(d.storeCode)!.kasalar.push(d);
        }
        return Array.from(storeMap.entries())
            .filter(([, { kasalar }]) => kasalar.length > 0 && kasalar.every(k => !k.isOnline))
            .map(([storeCode, { storeName, kasalar }]) => {
                const ts = kasalar.map(k => k.lastSeen).filter(Boolean) as string[];
                const earliest = ts.length > 0 ? ts.reduce((a, b) => (new Date(a) < new Date(b) ? a : b)) : null;
                return { storeCode, storeName, count: kasalar.length, offlineSince: earliest };
            })
            .sort((a, b) => a.storeCode - b.storeCode);
    }, [sqlDevices]);

    // Geçici kapalı mağazalar
    const closedStoreAlerts = useMemo(() => {
        const storeMap = new Map<number, { storeName: string; kasalar: SqlDeviceWithStatus[] }>();
        for (const d of sqlDevices) {
            if (d.deviceType?.toUpperCase() === 'PC' || !d.isTemporarilyClosed) continue;
            if (!storeMap.has(d.storeCode)) storeMap.set(d.storeCode, { storeName: d.storeName, kasalar: [] });
            storeMap.get(d.storeCode)!.kasalar.push(d);
        }
        return Array.from(storeMap.entries())
            .map(([storeCode, { storeName, kasalar }]) => ({
                storeCode, storeName, count: kasalar.length, reason: kasalar[0]?.temporaryCloseReason
            }))
            .sort((a, b) => a.storeCode - b.storeCode);
    }, [sqlDevices]);

    // Live time string
    const now = new Date();
    const liveTime = now.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    void tick; // keep reactive

    if (loading && !data) {
        return (
            <div className="flex h-screen items-center justify-center">
                <div className="w-8 h-8 border-2 border-violet-500 border-t-transparent rounded-full animate-spin" />
            </div>
        );
    }

    return (
        <div className="p-6 max-w-[1600px] mx-auto space-y-5 h-[calc(100vh-2rem)] flex flex-col">

            {/* ═══ TOP BAR ═══ */}
            <div className="flex items-center justify-between shrink-0">
                <div className="flex items-center gap-4">
                    <div className="relative">
                        <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-violet-500 to-indigo-600 flex items-center justify-center shadow-lg shadow-violet-500/30">
                            <Zap className="w-5 h-5 text-white" />
                        </div>
                        <span className="absolute -top-0.5 -right-0.5 w-2.5 h-2.5 bg-emerald-500 rounded-full border-2 border-slate-900 animate-pulse" />
                    </div>
                    <div>
                        <h1 className="text-xl font-bold text-white tracking-tight">Kontrol Paneli</h1>
                        <div className="flex items-center gap-2 mt-0.5">
                            <span className="w-1 h-1 rounded-full bg-emerald-500" />
                            <span className="text-[11px] text-slate-500 font-mono">{liveTime}</span>
                            <span className="text-[11px] text-slate-600">·</span>
                            <span className="text-[11px] text-slate-500">Son veri {lastUpdated.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}</span>
                        </div>
                    </div>
                </div>
                <button
                    onClick={() => { loadDashboard(); loadSqlDevices(); }}
                    className="flex items-center gap-2 px-3 py-2 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-all active:scale-95"
                >
                    <RefreshCw className={`w-3.5 h-3.5 text-violet-400 ${(loading || sqlLoading) ? 'animate-spin' : ''}`} />
                    <span className="text-xs text-slate-400 font-medium">Yenile</span>
                </button>
            </div>

            {/* ═══ STATS STRIP ═══ */}
            <div className="grid grid-cols-6 gap-3 shrink-0">
                {/* Agent Arc */}
                <div className="col-span-1 bg-slate-800/40 border border-slate-700/50 rounded-xl p-3 flex items-center gap-3">
                    <div className="relative shrink-0">
                        <ProgressArc percent={agentPercent} size={48} stroke={3} color="#8b5cf6" />
                        <div className="absolute inset-0 flex items-center justify-center">
                            <span className="text-xs font-bold text-violet-400">{agentPercent}%</span>
                        </div>
                    </div>
                    <div className="min-w-0">
                        <div className="text-[10px] text-slate-500 font-semibold uppercase tracking-wider">Agent</div>
                        <div className="text-sm font-bold text-white"><AnimatedNumber value={data?.online || 0} />/{data?.totalDevices || 0}</div>
                    </div>
                </div>

                {/* SQL Arc */}
                <div className="col-span-1 bg-slate-800/40 border border-slate-700/50 rounded-xl p-3 flex items-center gap-3">
                    <div className="relative shrink-0">
                        <ProgressArc percent={sqlPercent} size={48} stroke={3} color="#10b981" />
                        <div className="absolute inset-0 flex items-center justify-center">
                            <span className="text-xs font-bold text-emerald-400">{sqlPercent}%</span>
                        </div>
                    </div>
                    <div className="min-w-0">
                        <div className="text-[10px] text-slate-500 font-semibold uppercase tracking-wider">SQL</div>
                        <div className="text-sm font-bold text-white"><AnimatedNumber value={totalSqlOnline} />/{sqlDevices.length - posClosed}</div>
                    </div>
                </div>

                {/* Mini stat pills */}
                <MiniPill icon={<Monitor className="w-3.5 h-3.5" />} label="PC Online" value={pcOnline} total={pcDevices.length} color="sky" />
                <MiniPill icon={<MonitorSmartphone className="w-3.5 h-3.5" />} label="POS Online" value={posOnline} total={posDevices.length - posClosed} color="amber" />
                <MiniPill icon={<Shield className="w-3.5 h-3.5" />} label="Sağlıklı" value={data?.healthy || 0} total={data?.totalDevices || 0} color="emerald" />
                <MiniPill icon={<WifiOff className="w-3.5 h-3.5" />} label="Toplam Offline" value={(data?.offline || 0) + posOffline} color="rose" />
            </div>

            {/* ═══ MAIN CONTENT ═══ */}
            <div className="flex-1 grid grid-cols-12 gap-5 min-h-0 overflow-y-auto scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent">

                {/* ─── LEFT: Offline Alerts ─── */}
                <div className="col-span-7 flex flex-col gap-4">

                    {/* Offline Mağazalar */}
                    <div className="bg-slate-800/30 border border-red-500/20 rounded-2xl flex flex-col overflow-hidden">
                        <div className="flex items-center gap-3 px-5 py-3.5 border-b border-red-500/10 shrink-0">
                            <div className="relative">
                                <AlertTriangle className="w-4.5 h-4.5 text-red-400" />
                                {offlineStoreAlerts.length > 0 && <PulseRing color="border-red-500" />}
                            </div>
                            <span className="text-sm font-bold text-red-400">Offline Mağazalar</span>
                            <span className="ml-auto text-xs font-bold text-red-500/60 bg-red-500/10 px-2 py-0.5 rounded-full">
                                {offlineStoreAlerts.length}
                            </span>
                            {sqlLoading && <RefreshCw className="w-3 h-3 text-slate-600 animate-spin" />}
                        </div>

                        <div className="p-2 space-y-1">
                            {sqlLoading && sqlDevices.length === 0 ? (
                                <div className="flex items-center justify-center h-full text-slate-600 text-sm gap-2">
                                    <RefreshCw className="w-4 h-4 animate-spin" /> Kontrol ediliyor...
                                </div>
                            ) : offlineStoreAlerts.length === 0 ? (
                                <div className="flex flex-col items-center justify-center h-full text-slate-600">
                                    <Wifi className="w-8 h-8 mb-2 opacity-30" />
                                    <span className="text-sm font-medium">Tüm mağazalar erişilebilir</span>
                                </div>
                            ) : (
                                offlineStoreAlerts.map(({ storeCode, storeName, count, offlineSince }, i) => (
                                    <div
                                        key={storeCode}
                                        className="flex items-center gap-3 px-4 py-2.5 rounded-xl bg-red-500/5 hover:bg-red-500/10 border border-transparent hover:border-red-500/20 transition-all group cursor-default"
                                        style={{ animationDelay: `${i * 30}ms` }}
                                    >
                                        <span className="w-2 h-2 rounded-full bg-red-500 shrink-0 animate-pulse shadow-[0_0_6px_rgba(239,68,68,0.6)]" />
                                        <span className="font-mono text-xs text-red-300 font-bold w-8 shrink-0">{storeCode}</span>
                                        <span className="text-sm text-slate-100 font-medium truncate flex-1">{storeName}</span>
                                        <span className="text-[11px] text-red-300 font-semibold shrink-0">{count} kasa</span>
                                        {offlineSince && (
                                            <span className="text-[11px] text-red-300/80 font-mono font-semibold shrink-0 flex items-center gap-1">
                                                <Clock className="w-3 h-3" />
                                                {formatOfflineDuration(offlineSince)}
                                            </span>
                                        )}
                                        <ChevronRight className="w-3.5 h-3.5 text-red-500/20 group-hover:text-red-400/50 transition-colors shrink-0" />
                                    </div>
                                ))
                            )}
                        </div>
                    </div>

                    {/* Geçici Kapalı Mağazalar */}
                    {closedStoreAlerts.length > 0 && (
                        <div className="shrink-0 bg-slate-800/30 border border-amber-500/20 rounded-2xl overflow-hidden">
                            <div className="flex items-center gap-3 px-5 py-3 border-b border-amber-500/10">
                                <PauseCircle className="w-4 h-4 text-amber-400" />
                                <span className="text-sm font-bold text-amber-400">Geçici Kapalı</span>
                                <span className="ml-auto text-xs font-bold text-amber-500/60 bg-amber-500/10 px-2 py-0.5 rounded-full">
                                    {closedStoreAlerts.length}
                                </span>
                            </div>
                            <div className="p-2 space-y-1 max-h-[200px] overflow-y-auto scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent">
                                {closedStoreAlerts.map(({ storeCode, storeName, count, reason }) => (
                                    <div
                                        key={storeCode}
                                        className="flex items-center gap-3 px-4 py-2.5 rounded-xl bg-amber-500/5 hover:bg-amber-500/10 border border-transparent hover:border-amber-500/20 transition-all"
                                    >
                                        <span className="w-2 h-2 rounded-full bg-amber-500 shrink-0" />
                                        <span className="font-mono text-xs text-amber-300 font-bold w-8 shrink-0">{storeCode}</span>
                                        <span className="text-sm text-slate-100 font-medium truncate flex-1">{storeName}</span>
                                        <span className="text-[11px] text-amber-300 font-semibold shrink-0">{count} kasa</span>
                                        {reason && (
                                            <span className="text-[11px] text-amber-300/80 font-medium truncate max-w-[180px]" title={reason}>{reason}</span>
                                        )}
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}
                </div>

                {/* ─── RIGHT: Live Overview ─── */}
                <div className="col-span-5 flex flex-col gap-4">

                    {/* SQL Cihaz Dağılımı */}
                    <div className="bg-slate-800/30 border border-slate-700/50 rounded-2xl p-5 shrink-0">
                        <h3 className="text-xs font-bold text-slate-400 uppercase tracking-widest mb-4">Cihaz Dağılımı</h3>

                        <div className="grid grid-cols-2 gap-3">
                            {/* PC Card */}
                            <div className="bg-slate-900/50 border border-slate-700/40 rounded-xl p-4 hover:border-sky-500/30 transition-colors">
                                <div className="flex items-center gap-2 mb-3">
                                    <div className="p-1.5 bg-sky-500/10 rounded-lg text-sky-400">
                                        <Monitor className="w-4 h-4" />
                                    </div>
                                    <span className="text-sm font-bold text-white">PC</span>
                                    <span className="ml-auto text-xs text-slate-500 font-mono">{pcDevices.length}</span>
                                </div>
                                <div className="h-1.5 bg-slate-700/40 rounded-full overflow-hidden mb-2">
                                    <div
                                        className="h-full bg-gradient-to-r from-sky-500 to-sky-400 rounded-full transition-all duration-700"
                                        style={{ width: `${pcDevices.length > 0 ? (pcOnline / pcDevices.length * 100) : 0}%` }}
                                    />
                                </div>
                                <div className="flex justify-between text-[11px]">
                                    <span className="text-emerald-400">{pcOnline} online</span>
                                    <span className="text-rose-400">{pcDevices.length - pcOnline} offline</span>
                                </div>
                            </div>

                            {/* POS Card */}
                            <div className="bg-slate-900/50 border border-slate-700/40 rounded-xl p-4 hover:border-amber-500/30 transition-colors">
                                <div className="flex items-center gap-2 mb-3">
                                    <div className="p-1.5 bg-amber-500/10 rounded-lg text-amber-400">
                                        <MonitorSmartphone className="w-4 h-4" />
                                    </div>
                                    <span className="text-sm font-bold text-white">POS / Kasa</span>
                                    <span className="ml-auto text-xs text-slate-500 font-mono">{posDevices.length}</span>
                                </div>
                                <div className="h-1.5 bg-slate-700/40 rounded-full overflow-hidden mb-2">
                                    <div
                                        className="h-full bg-gradient-to-r from-amber-500 to-amber-400 rounded-full transition-all duration-700"
                                        style={{ width: `${(posDevices.length - posClosed) > 0 ? (posOnline / (posDevices.length - posClosed) * 100) : 0}%` }}
                                    />
                                </div>
                                <div className="flex justify-between text-[11px]">
                                    <span className="text-emerald-400">{posOnline} online</span>
                                    <span className="text-rose-400">{posOffline} offline</span>
                                    {posClosed > 0 && <span className="text-amber-400">{posClosed} kapalı</span>}
                                </div>
                            </div>
                        </div>

                        {/* Overall bar */}
                        <div className="mt-4 pt-3 border-t border-slate-700/30">
                            <div className="flex justify-between items-center text-xs mb-2">
                                <span className="text-slate-500">Genel SQL Erişim</span>
                                <span className="text-white font-bold">{sqlPercent}%</span>
                            </div>
                            <div className="h-2 bg-slate-700/40 rounded-full overflow-hidden flex">
                                <div
                                    className="h-full bg-gradient-to-r from-emerald-500 to-emerald-400 transition-all duration-700 rounded-l-full"
                                    style={{ width: `${sqlPercent}%` }}
                                />
                                {posClosed > 0 && (
                                    <div
                                        className="h-full bg-amber-500/40 transition-all duration-700"
                                        style={{ width: `${(posClosed / sqlDevices.length) * 100}%` }}
                                    />
                                )}
                            </div>
                            <div className="flex gap-4 mt-2 text-[10px] text-slate-500">
                                <span className="flex items-center gap-1"><span className="w-2 h-2 rounded-full bg-emerald-500" /> Online</span>
                                <span className="flex items-center gap-1"><span className="w-2 h-2 rounded-full bg-slate-600" /> Offline</span>
                                {posClosed > 0 && <span className="flex items-center gap-1"><span className="w-2 h-2 rounded-full bg-amber-500/60" /> Kapalı</span>}
                            </div>
                        </div>
                    </div>

                    {/* Agent Durumu */}
                    <div className="bg-slate-800/30 border border-slate-700/50 rounded-2xl p-5 flex flex-col">
                        <h3 className="text-xs font-bold text-slate-400 uppercase tracking-widest mb-4">Agent Durumu</h3>

                        <div className="flex-1 flex items-center justify-center">
                            <div className="flex items-center gap-8">
                                {/* Big ring */}
                                <div className="relative">
                                    <ProgressArc percent={agentPercent} size={120} stroke={6} color="#8b5cf6" />
                                    <div className="absolute inset-0 flex flex-col items-center justify-center">
                                        <AnimatedNumber value={data?.online || 0} className="text-2xl font-black text-white" />
                                        <span className="text-[10px] text-slate-500 font-medium">/ {data?.totalDevices || 0}</span>
                                    </div>
                                </div>

                                {/* Stats column */}
                                <div className="space-y-3">
                                    <AgentStatRow label="Online" value={data?.online || 0} color="emerald" />
                                    <AgentStatRow label="Offline" value={data?.offline || 0} color="rose" />
                                    <AgentStatRow label="Sağlıklı" value={data?.healthy || 0} color="violet" />
                                    {(data?.warning || 0) > 0 && <AgentStatRow label="Uyarı" value={data?.warning || 0} color="amber" />}
                                    {(data?.critical || 0) > 0 && <AgentStatRow label="Kritik" value={data?.critical || 0} color="red" />}
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};

// ─── Sub-components ───

const MiniPill = ({ icon, label, value, total, color }: { icon: React.ReactNode; label: string; value: number; total?: number; color: string }) => {
    const colors: Record<string, { text: string; bg: string; border: string }> = {
        sky: { text: 'text-sky-400', bg: 'bg-sky-500/10', border: 'border-sky-500/20' },
        amber: { text: 'text-amber-400', bg: 'bg-amber-500/10', border: 'border-amber-500/20' },
        emerald: { text: 'text-emerald-400', bg: 'bg-emerald-500/10', border: 'border-emerald-500/20' },
        rose: { text: 'text-rose-400', bg: 'bg-rose-500/10', border: 'border-rose-500/20' },
    };
    const c = colors[color] || colors.sky;
    return (
        <div className={`col-span-1 ${c.bg} border ${c.border} rounded-xl p-3 flex items-center gap-2.5`}>
            <div className={c.text}>{icon}</div>
            <div className="min-w-0">
                <div className="text-[10px] text-slate-500 font-semibold uppercase tracking-wider truncate">{label}</div>
                <div className="text-sm font-bold text-white">
                    <AnimatedNumber value={value} />
                    {total !== undefined && <span className="text-slate-500 font-normal text-xs">/{total}</span>}
                </div>
            </div>
        </div>
    );
};

const AgentStatRow = ({ label, value, color }: { label: string; value: number; color: string }) => {
    const dotColors: Record<string, string> = {
        emerald: 'bg-emerald-500', rose: 'bg-rose-500', violet: 'bg-violet-500', amber: 'bg-amber-500', red: 'bg-red-500'
    };
    const textColors: Record<string, string> = {
        emerald: 'text-emerald-400', rose: 'text-rose-400', violet: 'text-violet-400', amber: 'text-amber-400', red: 'text-red-400'
    };
    return (
        <div className="flex items-center gap-3">
            <span className={`w-2 h-2 rounded-full ${dotColors[color]}`} />
            <span className="text-xs text-slate-400 w-14">{label}</span>
            <span className={`text-sm font-bold ${textColors[color]}`}>{value}</span>
        </div>
    );
};

export default DashboardPage;
