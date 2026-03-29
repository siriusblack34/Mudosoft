import React, { useEffect, useState } from "react";
import { apiClient } from "../lib/apiClient";
import { AlertTriangle, Clock, RefreshCw, Search, TrendingUp, WifiOff, ChevronDown, ChevronUp } from "lucide-react";

interface OfflineLog {
    id: number;
    storeCode: number;
    storeName: string;
    offlineKasaCount: number;
    offlineAt: string;
    onlineAt: string | null;
    durationMinutes: number | null;
    isStillOffline: boolean;
}

interface OfflineStat {
    storeCode: number;
    storeName: string;
    totalIncidents: number;
    totalOfflineMinutes: number;
    lastOfflineAt: string;
    isCurrentlyOffline: boolean;
}

function formatDuration(mins: number | null): string {
    if (mins === null) return "—";
    if (mins < 60) return `${mins} dk`;
    const hours = Math.floor(mins / 60);
    const remaining = mins % 60;
    if (hours < 24) return `${hours}sa ${remaining}dk`;
    const days = Math.floor(hours / 24);
    return `${days}g ${hours % 24}sa`;
}

function formatDate(iso: string): string {
    return new Date(iso).toLocaleString("tr-TR", {
        day: "2-digit", month: "2-digit", year: "numeric",
        hour: "2-digit", minute: "2-digit"
    });
}

function liveDuration(offlineAt: string): string {
    const mins = Math.floor((Date.now() - new Date(offlineAt).getTime()) / 60000);
    return formatDuration(mins);
}

const OfflineLogsPage: React.FC = () => {
    const [logs, setLogs] = useState<OfflineLog[]>([]);
    const [stats, setStats] = useState<OfflineStat[]>([]);
    const [loading, setLoading] = useState(true);
    const [days, setDays] = useState(7);
    const [search, setSearch] = useState("");
    const [tab, setTab] = useState<"logs" | "stats">("stats");
    const [expandedStore, setExpandedStore] = useState<number | null>(null);
    const [tick, setTick] = useState(0);

    // Tick for live duration
    useEffect(() => {
        const t = setInterval(() => setTick(p => p + 1), 30000);
        return () => clearInterval(t);
    }, []);

    const load = async () => {
        setLoading(true);
        try {
            const [logsData, statsData] = await Promise.all([
                apiClient.getOfflineLogs(days),
                apiClient.getOfflineStats(days)
            ]);
            setLogs(logsData);
            setStats(statsData);
        } catch (err) {
            console.error("Offline logs load failed:", err);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => { load(); }, [days]);

    // Auto-refresh every 60 seconds
    useEffect(() => {
        const interval = setInterval(load, 60000);
        return () => clearInterval(interval);
    }, [days]);

    const filteredLogs = search.trim()
        ? logs.filter(l => l.storeName.toLowerCase().includes(search.toLowerCase()) || String(l.storeCode).includes(search))
        : logs;

    const filteredStats = search.trim()
        ? stats.filter(s => s.storeName.toLowerCase().includes(search.toLowerCase()) || String(s.storeCode).includes(search))
        : stats;

    void tick;

    return (
        <div className="p-6 max-w-[1400px] mx-auto space-y-5">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-4">
                    <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-red-500 to-rose-600 flex items-center justify-center shadow-lg shadow-red-500/30">
                        <WifiOff className="w-5 h-5 text-white" />
                    </div>
                    <div>
                        <h1 className="text-xl font-bold text-white tracking-tight">Offline Geçmişi</h1>
                        <p className="text-[11px] text-slate-500 mt-0.5">Mağaza erişim kesintisi kayıtları</p>
                    </div>
                </div>
                <div className="flex items-center gap-3">
                    {/* Period selector */}
                    <div className="flex bg-slate-800/60 rounded-lg border border-slate-700/50 p-0.5">
                        {[7, 14, 30, 90].map(d => (
                            <button
                                key={d}
                                onClick={() => setDays(d)}
                                className={`px-3 py-1.5 text-xs font-semibold rounded-md transition-all ${days === d
                                    ? "bg-violet-500/20 text-violet-400 border border-violet-500/30"
                                    : "text-slate-500 hover:text-slate-300"
                                }`}
                            >
                                {d}g
                            </button>
                        ))}
                    </div>
                    <button
                        onClick={load}
                        className="p-2 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-all active:scale-95"
                    >
                        <RefreshCw className={`w-4 h-4 text-violet-400 ${loading ? 'animate-spin' : ''}`} />
                    </button>
                </div>
            </div>

            {/* Search + Tabs */}
            <div className="flex items-center gap-4">
                <div className="relative flex-1 max-w-sm">
                    <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                    <input
                        type="text"
                        placeholder="Mağaza ara..."
                        value={search}
                        onChange={e => setSearch(e.target.value)}
                        className="w-full pl-10 pr-4 py-2 bg-slate-800/60 border border-slate-700/50 rounded-lg text-sm text-white placeholder-slate-500 focus:outline-none focus:border-violet-500/50"
                    />
                </div>
                <div className="flex bg-slate-800/60 rounded-lg border border-slate-700/50 p-0.5">
                    <button
                        onClick={() => setTab("stats")}
                        className={`flex items-center gap-1.5 px-4 py-1.5 text-xs font-semibold rounded-md transition-all ${tab === "stats"
                            ? "bg-violet-500/20 text-violet-400 border border-violet-500/30"
                            : "text-slate-500 hover:text-slate-300"
                        }`}
                    >
                        <TrendingUp className="w-3.5 h-3.5" /> Özet
                    </button>
                    <button
                        onClick={() => setTab("logs")}
                        className={`flex items-center gap-1.5 px-4 py-1.5 text-xs font-semibold rounded-md transition-all ${tab === "logs"
                            ? "bg-violet-500/20 text-violet-400 border border-violet-500/30"
                            : "text-slate-500 hover:text-slate-300"
                        }`}
                    >
                        <Clock className="w-3.5 h-3.5" /> Detaylı Log
                    </button>
                </div>
            </div>

            {/* Summary Bar */}
            {!loading && stats.length > 0 && (
                <div className="flex items-center gap-4 text-xs">
                    <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-red-500/10 border border-red-500/20">
                        <span className="w-2 h-2 rounded-full bg-red-500 animate-pulse" />
                        <span className="font-bold text-red-400">{stats.filter(s => s.isCurrentlyOffline).length}</span>
                        <span className="text-red-400/70">simdi offline</span>
                    </div>
                    <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-slate-700/50">
                        <AlertTriangle className="w-3 h-3 text-slate-400" />
                        <span className="font-bold text-slate-300">{logs.length}</span>
                        <span className="text-slate-400">toplam kesinti</span>
                    </div>
                    <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-slate-700/50">
                        <span className="font-bold text-slate-300">{stats.length}</span>
                        <span className="text-slate-400">etkilenen magaza</span>
                    </div>
                </div>
            )}

            {loading ? (
                <div className="flex items-center justify-center h-64 text-slate-600">
                    <RefreshCw className="w-6 h-6 animate-spin" />
                </div>
            ) : tab === "stats" ? (
                /* ═══ STATS VIEW ═══ */
                <div className="space-y-2">
                    {filteredStats.length === 0 ? (
                        <div className="flex flex-col items-center justify-center h-48 text-slate-600">
                            <AlertTriangle className="w-8 h-8 mb-2 opacity-30" />
                            <span className="text-sm">Bu dönemde kesinti kaydı yok</span>
                        </div>
                    ) : (
                        filteredStats.map(stat => {
                            const isExpanded = expandedStore === stat.storeCode;
                            const storeLogs = logs.filter(l => l.storeCode === stat.storeCode);
                            return (
                                <div key={stat.storeCode} className="bg-slate-800/30 border border-slate-700/50 rounded-xl overflow-hidden">
                                    <div
                                        className="flex items-center gap-4 px-5 py-3.5 cursor-pointer hover:bg-slate-800/50 transition-colors"
                                        onClick={() => setExpandedStore(isExpanded ? null : stat.storeCode)}
                                    >
                                        {/* Rank / Status */}
                                        <div className="relative shrink-0">
                                            <span className={`w-3 h-3 rounded-full block ${stat.isCurrentlyOffline
                                                ? 'bg-red-500 animate-pulse shadow-[0_0_8px_rgba(239,68,68,0.6)]'
                                                : 'bg-slate-600'
                                            }`} />
                                        </div>

                                        <span className="font-mono text-sm text-slate-400 font-bold w-10 shrink-0">{stat.storeCode}</span>
                                        <span className="text-sm text-white font-medium flex-1 truncate">{stat.storeName}</span>

                                        {/* Incident count */}
                                        <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-red-500/10 border border-red-500/20 shrink-0">
                                            <AlertTriangle className="w-3 h-3 text-red-400" />
                                            <span className="text-xs font-bold text-red-300">{stat.totalIncidents}</span>
                                            <span className="text-[10px] text-red-400/70">kesinti</span>
                                        </div>

                                        {/* Total duration */}
                                        <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-slate-700/50 shrink-0">
                                            <Clock className="w-3 h-3 text-slate-400" />
                                            <span className="text-xs font-bold text-slate-300">{formatDuration(stat.totalOfflineMinutes)}</span>
                                        </div>

                                        {stat.isCurrentlyOffline && (
                                            <span className="text-[10px] font-bold text-red-400 bg-red-500/10 px-2 py-0.5 rounded-full border border-red-500/20 animate-pulse shrink-0">
                                                OFFLINE
                                            </span>
                                        )}

                                        {isExpanded ? <ChevronUp className="w-4 h-4 text-slate-500 shrink-0" /> : <ChevronDown className="w-4 h-4 text-slate-500 shrink-0" />}
                                    </div>

                                    {/* Expanded: individual logs */}
                                    {isExpanded && storeLogs.length > 0 && (
                                        <div className="border-t border-slate-700/50 bg-slate-900/30 px-5 py-3 space-y-1.5">
                                            {storeLogs.map(log => (
                                                <div key={log.id} className="flex items-center gap-3 text-xs py-1.5">
                                                    <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${log.isStillOffline ? 'bg-red-500 animate-pulse' : 'bg-slate-600'}`} />
                                                    <span className="text-slate-400 w-36 shrink-0">{formatDate(log.offlineAt)}</span>
                                                    <span className="text-slate-600 shrink-0">→</span>
                                                    <span className="text-slate-400 w-36 shrink-0">
                                                        {log.onlineAt ? formatDate(log.onlineAt) : <span className="text-red-400 font-semibold">hâlâ offline</span>}
                                                    </span>
                                                    <span className="text-slate-500 shrink-0">{log.offlineKasaCount} kasa</span>
                                                    <span className={`font-mono font-semibold shrink-0 ${log.isStillOffline ? 'text-red-400' : 'text-slate-300'}`}>
                                                        {log.isStillOffline ? liveDuration(log.offlineAt) : formatDuration(log.durationMinutes)}
                                                    </span>
                                                </div>
                                            ))}
                                        </div>
                                    )}
                                </div>
                            );
                        })
                    )}
                </div>
            ) : (
                /* ═══ LOGS VIEW ═══ */
                <div className="bg-slate-800/30 border border-slate-700/50 rounded-2xl overflow-hidden">
                    <table className="w-full text-left">
                        <thead className="bg-slate-800/80 border-b border-slate-700/50">
                            <tr>
                                <th className="px-5 py-3 text-[10px] font-bold text-slate-400 uppercase tracking-widest w-10"></th>
                                <th className="px-5 py-3 text-[10px] font-bold text-slate-400 uppercase tracking-widest w-16">Kod</th>
                                <th className="px-5 py-3 text-[10px] font-bold text-slate-400 uppercase tracking-widest">Mağaza</th>
                                <th className="px-5 py-3 text-[10px] font-bold text-slate-400 uppercase tracking-widest w-16">Kasa</th>
                                <th className="px-5 py-3 text-[10px] font-bold text-slate-400 uppercase tracking-widest">Offline Oldu</th>
                                <th className="px-5 py-3 text-[10px] font-bold text-slate-400 uppercase tracking-widest">Online Oldu</th>
                                <th className="px-5 py-3 text-[10px] font-bold text-slate-400 uppercase tracking-widest w-24">Süre</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-800/50">
                            {filteredLogs.length === 0 ? (
                                <tr>
                                    <td colSpan={7} className="text-center py-16 text-slate-600 text-sm">
                                        Bu dönemde kesinti kaydı yok
                                    </td>
                                </tr>
                            ) : (
                                filteredLogs.map(log => (
                                    <tr key={log.id} className={`hover:bg-slate-800/30 transition-colors ${log.isStillOffline ? 'bg-red-500/5' : ''}`}>
                                        <td className="px-5 py-3">
                                            <span className={`w-2 h-2 rounded-full block ${log.isStillOffline ? 'bg-red-500 animate-pulse' : 'bg-slate-600'}`} />
                                        </td>
                                        <td className="px-5 py-3 font-mono text-xs text-slate-400 font-bold">{log.storeCode}</td>
                                        <td className="px-5 py-3 text-sm text-slate-200 font-medium">{log.storeName}</td>
                                        <td className="px-5 py-3 text-xs text-slate-400">{log.offlineKasaCount}</td>
                                        <td className="px-5 py-3 text-xs text-slate-400">{formatDate(log.offlineAt)}</td>
                                        <td className="px-5 py-3 text-xs">
                                            {log.onlineAt
                                                ? <span className="text-slate-400">{formatDate(log.onlineAt)}</span>
                                                : <span className="text-red-400 font-semibold">hâlâ offline</span>
                                            }
                                        </td>
                                        <td className="px-5 py-3">
                                            <span className={`text-xs font-mono font-semibold ${log.isStillOffline ? 'text-red-400' : 'text-slate-300'}`}>
                                                {log.isStillOffline ? liveDuration(log.offlineAt) : formatDuration(log.durationMinutes)}
                                            </span>
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>
            )}
        </div>
    );
};

export default OfflineLogsPage;
