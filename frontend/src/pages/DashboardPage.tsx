import React, { useEffect, useState } from "react";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import {
    Monitor, Wifi, WifiOff, RefreshCw, Activity,
    CheckCircle, Clock, Server, MonitorSmartphone,
    AlertTriangle, XCircle
} from "lucide-react";

interface DashboardData {
    totalDevices: number;
    online: number;
    offline: number;
    healthy: number;
    warning: number;
    critical: number;
}

const SQL_CACHE_KEY = 'ms_sql_devices_cache_v2'; // v2: lastSeen alanı eklendi

function formatOfflineDuration(isoStr: string): string {
    const diff = Date.now() - new Date(isoStr).getTime();
    const mins  = Math.floor(diff / 60000);
    const hours = Math.floor(mins / 60);
    const days  = Math.floor(hours / 24);
    if (days > 0)  return `${days} gündür`;
    if (hours > 0) return `${hours} saattir`;
    if (mins > 0)  return `${mins} dakikadır`;
    return 'az önce';
}

const DashboardPage: React.FC = () => {
    const [data, setData] = useState<DashboardData | null>(null);
    const [sqlDevices, setSqlDevices] = useState<SqlDeviceWithStatus[]>(() => {
        // Sayfa açılınca hemen önbellekten yükle
        try {
            const cached = localStorage.getItem(SQL_CACHE_KEY);
            return cached ? JSON.parse(cached) : [];
        } catch { return []; }
    });
    const [loading, setLoading] = useState(true);
    const [sqlLoading, setSqlLoading] = useState(true);
    const [lastUpdated, setLastUpdated] = useState<Date>(new Date());

    const loadDashboard = async () => {
        try {
            const res = await apiClient.getDashboard();
            setData({
                totalDevices: res.totalDevices,
                online: res.online,
                offline: res.offline,
                healthy: res.healthy,
                warning: res.warning,
                critical: res.critical
            });
            setLastUpdated(new Date());
        } catch (err) {
            console.error("Dashboard load failed:", err);
        } finally {
            setLoading(false);
        }
    };

    const loadSqlDevices = async () => {
        setSqlLoading(true);
        try {
            const devices = await apiClient.getSqlDevicesWithStatus({ timeoutMs: 3000 });
            setSqlDevices(devices);
            // Güncel veriyi önbelleğe kaydet
            localStorage.setItem(SQL_CACHE_KEY, JSON.stringify(devices));
        } catch (err) {
            console.error("SQL devices load failed:", err);
        } finally {
            setSqlLoading(false);
        }
    };

    useEffect(() => {
        loadDashboard();
        loadSqlDevices();
        const interval = setInterval(() => {
            loadDashboard();
            loadSqlDevices();
        }, 30000);
        return () => clearInterval(interval);
    }, []);

    // SQL Device counts
    const pcDevices = sqlDevices.filter(d => d.deviceType?.toUpperCase() === 'PC');
    const posDevices = sqlDevices.filter(d => d.deviceType?.toUpperCase() !== 'PC');
    const pcOnline = pcDevices.filter(d => d.isOnline).length;
    const pcOffline = pcDevices.length - pcOnline;
    const posOnline = posDevices.filter(d => d.isOnline).length;
    const posOffline = posDevices.length - posOnline;
    const totalSqlOnline = pcOnline + posOnline;

    // Tüm kasaları offline olan mağazalar
    const offlineStoreAlerts = (() => {
        const storeMap = new Map<number, { storeName: string; kasalar: SqlDeviceWithStatus[] }>();
        for (const d of sqlDevices) {
            if (d.deviceType?.toUpperCase() === 'PC') continue;
            if (!storeMap.has(d.storeCode)) {
                storeMap.set(d.storeCode, { storeName: d.storeName, kasalar: [] });
            }
            storeMap.get(d.storeCode)!.kasalar.push(d);
        }
        return Array.from(storeMap.entries())
            .filter(([, { kasalar }]) => kasalar.length > 0 && kasalar.every(k => !k.isOnline))
            .map(([storeCode, { storeName, kasalar }]) => {
                // En eski lastSeen değerini al (en uzun süredir offline olan kasa)
                const timestamps = kasalar.map(k => k.lastSeen).filter(Boolean) as string[];
                const earliestTs = timestamps.length > 0
                    ? timestamps.reduce((a, b) => (new Date(a) < new Date(b) ? a : b))
                    : null;
                return { storeCode, storeName, count: kasalar.length, offlineSince: earliestTs };
            })
            .sort((a, b) => a.storeCode - b.storeCode);
    })();

    if (loading && !data) {
        return (
            <div className="flex h-screen items-center justify-center">
                <div className="w-6 h-6 border-2 border-sky-500 border-t-transparent rounded-full animate-spin" />
            </div>
        );
    }

    return (
        <div className="space-y-4 max-w-[1400px] mx-auto">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div>
                    <h1 className="text-2xl font-bold text-white flex items-center gap-3 tracking-tight">
                        <div className="p-2 rounded-lg bg-sky-500/20 text-sky-400 border border-sky-500/20 shadow-inner">
                            <Activity className="w-5 h-5" />
                        </div>
                        <span className="text-transparent bg-clip-text bg-gradient-to-r from-white to-slate-400">Dashboard</span>
                    </h1>
                    <p className="text-xs text-ms-text-muted mt-1 font-medium tracking-wide">Sistem Durumu Özeti</p>
                </div>
                <div className="flex items-center gap-3 glass-card px-3 py-1.5 rounded-lg border-white/5">
                    <span className="text-[11px] text-slate-500">
                        {lastUpdated.toLocaleTimeString('tr-TR')}
                    </span>
                    <button
                        onClick={() => { loadDashboard(); loadSqlDevices(); }}
                        className="p-1.5 hover:bg-white/10 rounded-md transition-all duration-200 active:scale-95"
                    >
                        <RefreshCw className={`w-4 h-4 text-sky-400 ${(loading || sqlLoading) ? 'animate-spin' : ''}`} />
                    </button>
                </div>
            </div>

            {/* ⚠️ Tüm Kasaları Offline Mağaza Alertleri */}
            {sqlLoading && sqlDevices.length === 0 ? (
                <div className="glass-card rounded-xl border-zinc-700/40 p-4 flex items-center gap-2 text-sm text-zinc-500">
                    <RefreshCw className="w-3.5 h-3.5 animate-spin shrink-0" />
                    Kasa durumları kontrol ediliyor...
                </div>
            ) : offlineStoreAlerts.length > 0 ? (
                <div className="glass-card rounded-xl border-red-500/30 bg-red-500/5 p-4 space-y-2">
                    <div className="flex items-center gap-2 mb-3">
                        <AlertTriangle className="w-4 h-4 text-red-400 shrink-0" />
                        <span className="text-sm font-semibold text-red-400">
                            {offlineStoreAlerts.length} mağazanın tüm kasaları offline
                        </span>
                        {sqlLoading && <RefreshCw className="w-3 h-3 text-zinc-500 animate-spin ml-auto" />}
                    </div>
                    <div className="flex flex-wrap gap-2">
                        {offlineStoreAlerts.map(({ storeCode, storeName, count, offlineSince }) => (
                            <div
                                key={storeCode}
                                className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-red-500/10 border border-red-500/20 text-xs"
                            >
                                <span className="w-1.5 h-1.5 rounded-full bg-red-500 shrink-0 animate-pulse" />
                                <span className="font-semibold text-red-300">
                                    {storeCode} — {storeName}
                                </span>
                                <span className="text-red-500/70">{count} kasa</span>
                                {offlineSince && (
                                    <span className="text-red-400/60 font-medium">
                                        · {formatOfflineDuration(offlineSince)}
                                    </span>
                                )}
                            </div>
                        ))}
                    </div>
                </div>
            ) : null}

            {/* Quick Stats Row */}
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                <StatCard
                    icon={<Server className="w-4 h-4" />}
                    label="Toplam Cihaz"
                    value={data?.totalDevices || 0}
                    color="indigo"
                />
                <StatCard
                    icon={<Wifi className="w-4 h-4" />}
                    label="Online"
                    value={data?.online || 0}
                    color="emerald"
                />
                <StatCard
                    icon={<CheckCircle className="w-4 h-4" />}
                    label="Sağlıklı"
                    value={data?.healthy || 0}
                    color="sky"
                />
                <StatCard
                    icon={<WifiOff className="w-4 h-4" />}
                    label="Offline"
                    value={data?.offline || 0}
                    color="rose"
                />
            </div>

            {/* Main Grid: Agent + SQL */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">

                {/* Agent Status - compact */}
                <div className="glass-card rounded-2xl p-5 relative overflow-hidden group">
                    <h2 className="text-sm font-semibold text-white mb-4 flex items-center gap-2 relative z-10">
                        <Server className="w-4 h-4 text-sky-400" />
                        Agent Durumu
                    </h2>

                    {/* Ring Chart area */}
                    <div className="flex items-center justify-center py-2">
                        <div className="relative w-28 h-28">
                            <svg className="w-full h-full -rotate-90" viewBox="0 0 36 36">
                                <circle cx="18" cy="18" r="15.5" fill="none" stroke="#1e293b" strokeWidth="3" />
                                <circle
                                    cx="18" cy="18" r="15.5" fill="none"
                                    stroke="#10b981" strokeWidth="3"
                                    strokeDasharray={`${data && data.totalDevices > 0 ? (data.online / data.totalDevices * 100) : 0} 100`}
                                    strokeLinecap="round"
                                />
                            </svg>
                            <div className="absolute inset-0 flex flex-col items-center justify-center">
                                <span className="text-xl font-bold text-white">{data?.online || 0}</span>
                                <span className="text-[10px] text-slate-500">/ {data?.totalDevices || 0}</span>
                            </div>
                        </div>
                    </div>

                    <div className="grid grid-cols-3 gap-2 mt-2">
                        <MiniStat label="Online" value={data?.online || 0} color="emerald" />
                        <MiniStat label="Offline" value={data?.offline || 0} color="rose" />
                        <MiniStat label="Sağlıklı" value={data?.healthy || 0} color="sky" />
                    </div>
                </div>

                {/* SQL Devices - spans 2 cols */}
                <div className="lg:col-span-2 glass-card rounded-2xl p-5 relative overflow-hidden group">
                    <h2 className="text-sm font-semibold text-white mb-4 flex items-center gap-2 relative z-10">
                        <MonitorSmartphone className="w-5 h-5 text-violet-400" />
                        SQL Cihazları
                        <span className="text-[11px] text-slate-400 font-medium ml-2 px-2 py-0.5 bg-white/5 rounded-full border border-white/5">
                            {sqlDevices.length} cihaz
                        </span>
                        {sqlLoading && <RefreshCw className="w-3 h-3 text-slate-500 animate-spin ml-auto" />}
                    </h2>

                    <div className="grid grid-cols-2 gap-4 relative z-10">
                        {/* PC */}
                        <div className="glass-panel rounded-xl p-4 border-white/5 hover-lift">
                            <div className="flex items-center gap-2 mb-4 pb-3 border-b border-white/5">
                                <div className="p-1.5 bg-blue-500/20 rounded-lg text-blue-400">
                                    <Monitor className="w-4 h-4" />
                                </div>
                                <span className="text-sm font-semibold text-white tracking-wide">PC</span>
                                <span className="ml-auto text-xs font-bold text-slate-400 bg-white/5 px-2 py-0.5 rounded-md">{pcDevices.length}</span>
                            </div>
                            <div className="space-y-1.5">
                                <StatusRow label="Online" value={pcOnline} color="emerald" />
                                <StatusRow label="Offline" value={pcOffline} color="rose" />
                            </div>
                        </div>

                        {/* POS */}
                        <div className="glass-panel rounded-xl p-4 border-white/5 hover-lift">
                            <div className="flex items-center gap-2 mb-4 pb-3 border-b border-white/5">
                                <div className="p-1.5 bg-amber-500/20 rounded-lg text-amber-400">
                                    <MonitorSmartphone className="w-4 h-4" />
                                </div>
                                <span className="text-sm font-semibold text-white tracking-wide">POS</span>
                                <span className="ml-auto text-xs font-bold text-slate-400 bg-white/5 px-2 py-0.5 rounded-md">{posDevices.length}</span>
                            </div>
                            <div className="space-y-1.5">
                                <StatusRow label="Online" value={posOnline} color="emerald" />
                                <StatusRow label="Offline" value={posOffline} color="rose" />
                            </div>
                        </div>
                    </div>

                    {/* Summary Bar */}
                    <div className="mt-3 pt-3 border-t border-slate-700/30">
                        <div className="flex justify-between text-xs mb-1.5">
                            <span className="text-slate-400">SQL Erişilebilirlik</span>
                            <span className="text-slate-300 font-medium">
                                {totalSqlOnline}/{sqlDevices.length}
                            </span>
                        </div>
                        <div className="h-1.5 bg-slate-700/60 rounded-full overflow-hidden">
                            <div
                                className="h-full bg-gradient-to-r from-emerald-500 to-emerald-400 transition-all duration-500 rounded-full"
                                style={{ width: `${sqlDevices.length > 0 ? (totalSqlOnline / sqlDevices.length * 100) : 0}%` }}
                            />
                        </div>
                    </div>
                </div>
            </div>

            {/* Bottom Quick Info Row */}
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
                <QuickCard
                    icon={<Clock className="w-4 h-4 text-slate-400" />}
                    label="Son Kontrol"
                    value={lastUpdated.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}
                />
                <QuickCard
                    icon={<Wifi className="w-4 h-4 text-emerald-400" />}
                    label="Agent Online"
                    value={`${data?.online || 0}/${data?.totalDevices || 0}`}
                />
                <QuickCard
                    icon={<Monitor className="w-4 h-4 text-sky-400" />}
                    label="SQL Online"
                    value={`${totalSqlOnline}/${sqlDevices.length}`}
                />
                <QuickCard
                    icon={<WifiOff className="w-4 h-4 text-rose-400" />}
                    label="Toplam Offline"
                    value={(data?.offline || 0) + (pcOffline + posOffline)}
                />
            </div>
        </div>
    );
};

// Compact stat card for top row
const StatCard = ({ icon, label, value, color }: { icon: React.ReactNode; label: string; value: number; color: string }) => {
    const bgColors: Record<string, string> = {
        indigo: "from-indigo-500/20 to-indigo-500/5 border-indigo-500/30 shadow-[0_0_15px_rgba(99,102,241,0.1)]",
        emerald: "from-emerald-500/20 to-emerald-500/5 border-emerald-500/30 shadow-[0_0_15px_rgba(16,185,129,0.1)]",
        sky: "from-sky-500/20 to-sky-500/5 border-sky-500/30 shadow-[0_0_15px_rgba(14,165,233,0.1)]",
        rose: "from-rose-500/20 to-rose-500/5 border-rose-500/30 shadow-[0_0_15px_rgba(244,63,94,0.1)]",
    };
    const textColors: Record<string, string> = {
        indigo: "text-indigo-400",
        emerald: "text-emerald-400",
        sky: "text-sky-400",
        rose: "text-rose-400",
    };

    return (
        <div className={`glass-card-hover bg-gradient-to-br ${bgColors[color]} border rounded-2xl p-4 flex items-center gap-4 relative overflow-hidden group`}>
            <div className="absolute -right-4 -top-4 w-16 h-16 bg-white/5 rounded-full blur-xl group-hover:bg-white/10 transition-colors"></div>
            <div className={`p-3 rounded-xl bg-white/5 border border-white/5 shadow-inner ${textColors[color]}`}>{icon}</div>
            <div className="relative z-10">
                <div className="text-[11px] text-slate-400 uppercase tracking-widest font-semibold mb-0.5">{label}</div>
                <div className={`text-2xl font-bold ${textColors[color]} drop-shadow-sm`}>{value}</div>
            </div>
        </div>
    );
};

// Mini stat inside agent ring card
const MiniStat = ({ label, value, color }: { label: string; value: number; color: string }) => {
    const textColors: Record<string, string> = {
        emerald: "text-emerald-400",
        rose: "text-rose-400",
        sky: "text-sky-400",
    };
    return (
        <div className="text-center">
            <div className={`text-base font-bold ${textColors[color]}`}>{value}</div>
            <div className="text-[10px] text-slate-500">{label}</div>
        </div>
    );
};

// Status row for SQL panel
const StatusRow = ({ label, value, color }: { label: string; value: number; color: string }) => {
    const dotColors: Record<string, string> = { emerald: "bg-emerald-500", rose: "bg-rose-500" };
    const textColors: Record<string, string> = { emerald: "text-emerald-400", rose: "text-rose-400" };
    return (
        <div className="flex items-center justify-between">
            <span className="text-xs text-slate-400 flex items-center gap-1.5">
                <span className={`w-1.5 h-1.5 rounded-full ${dotColors[color]}`}></span>
                {label}
            </span>
            <span className={`text-sm font-semibold ${textColors[color]}`}>{value}</span>
        </div>
    );
};

// Bottom quick info card
const QuickCard = ({ icon, label, value }: { icon: React.ReactNode; label: string; value: any }) => (
    <div className="glass-card px-4 py-3 rounded-xl flex items-center gap-3 hover-lift border-white/5">
        <div className="p-2 bg-white/5 rounded-lg border border-white/5">{icon}</div>
        <div>
            <div className="text-[10px] text-slate-400 uppercase tracking-widest font-semibold">{label}</div>
            <div className="text-sm font-bold text-white mt-0.5">{value}</div>
        </div>
    </div>
);

export default DashboardPage;
