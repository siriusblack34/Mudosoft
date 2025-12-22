import React, { useEffect, useState } from "react";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import {
    Monitor, Wifi, WifiOff, RefreshCw, Server, Activity,
    AlertTriangle, CheckCircle, Clock, TrendingUp, Zap,
    ChevronRight, Database
} from "lucide-react";

interface RecentOfflineDevice {
    hostname: string;
    ip: string;
    os: string;
    store: number;
    lastSeen: string;
}

interface DashboardData {
    totalDevices: number;
    online: number;
    offline: number;
    healthy: number;
    warning: number;
    critical: number;
    recentOffline: RecentOfflineDevice[];
}

const DashboardPage: React.FC = () => {
    const [data, setData] = useState<DashboardData | null>(null);
    const [sqlDevices, setSqlDevices] = useState<SqlDeviceWithStatus[]>([]);
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
                critical: res.critical,
                recentOffline: res.recentOffline?.map((d: any) => ({
                    hostname: d.hostname,
                    ip: d.ip ?? d.ipAddress,
                    os: d.os,
                    store: d.store ?? d.storeCode,
                    lastSeen: d.lastSeen
                })) || []
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

    const sqlOnline = sqlDevices.filter(d => d.isOnline).length;
    const sqlOffline = sqlDevices.filter(d => !d.isOnline).length;
    const sqlTotal = sqlDevices.length;
    const sqlOnlinePercent = sqlTotal > 0 ? ((sqlOnline / sqlTotal) * 100).toFixed(0) : "0";

    const formatTime = (date: Date) => {
        return date.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
    };

    const formatLastSeen = (dateStr: string) => {
        if (!dateStr) return "-";
        const date = new Date(dateStr);
        const now = new Date();
        const diffMs = now.getTime() - date.getTime();
        const diffMins = Math.floor(diffMs / 60000);
        if (diffMins < 60) return `${diffMins}d önce`;
        const diffHours = Math.floor(diffMins / 60);
        if (diffHours < 24) return `${diffHours}s önce`;
        return `${Math.floor(diffHours / 24)}g önce`;
    };

    if (loading && !data) {
        return (
            <div className="flex h-screen items-center justify-center">
                <div className="w-8 h-8 border-2 border-sky-500 border-t-transparent rounded-full animate-spin" />
            </div>
        );
    }

    return (
        <div className="p-6 space-y-5">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div>
                    <h1 className="text-xl font-semibold text-white">Dashboard</h1>
                    <p className="text-sm text-slate-400">Son güncelleme: {formatTime(lastUpdated)}</p>
                </div>
                <button
                    onClick={() => { loadDashboard(); loadSqlDevices(); }}
                    className="flex items-center gap-2 px-4 py-2 text-sm bg-slate-800 hover:bg-slate-700 border border-slate-600 rounded-xl transition-colors"
                >
                    <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
                    Yenile
                </button>
            </div>

            {/* Stats Row */}
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
                {/* Total Devices */}
                <div className="bg-gradient-to-br from-slate-800 to-slate-800/50 rounded-2xl p-5 border border-slate-700/50">
                    <div className="flex items-center justify-between mb-3">
                        <div className="p-2.5 bg-indigo-500/20 rounded-xl">
                            <Monitor className="w-5 h-5 text-indigo-400" />
                        </div>
                        <TrendingUp className="w-4 h-4 text-emerald-400" />
                    </div>
                    <div className="text-3xl font-bold text-white mb-1">{data?.totalDevices || 0}</div>
                    <div className="text-xs text-slate-400 uppercase tracking-wide">Toplam Cihaz</div>
                </div>

                {/* Online */}
                <div className="bg-gradient-to-br from-slate-800 to-slate-800/50 rounded-2xl p-5 border border-slate-700/50">
                    <div className="flex items-center justify-between mb-3">
                        <div className="p-2.5 bg-emerald-500/20 rounded-xl">
                            <Wifi className="w-5 h-5 text-emerald-400" />
                        </div>
                        <span className="text-xs px-2 py-1 bg-emerald-500/20 text-emerald-400 rounded-lg font-medium">
                            {data?.totalDevices ? ((data.online / data.totalDevices) * 100).toFixed(0) : 0}%
                        </span>
                    </div>
                    <div className="text-3xl font-bold text-emerald-400 mb-1">{data?.online || 0}</div>
                    <div className="text-xs text-slate-400 uppercase tracking-wide">Çevrimiçi</div>
                </div>

                {/* Critical */}
                <div className="bg-gradient-to-br from-slate-800 to-slate-800/50 rounded-2xl p-5 border border-slate-700/50">
                    <div className="flex items-center justify-between mb-3">
                        <div className="p-2.5 bg-amber-500/20 rounded-xl">
                            <AlertTriangle className="w-5 h-5 text-amber-400" />
                        </div>
                        <Zap className="w-4 h-4 text-amber-400" />
                    </div>
                    <div className="text-3xl font-bold text-amber-400 mb-1">{data?.critical || 0}</div>
                    <div className="text-xs text-slate-400 uppercase tracking-wide">Kritik Uyarı</div>
                </div>

                {/* Healthy */}
                <div className="bg-gradient-to-br from-slate-800 to-slate-800/50 rounded-2xl p-5 border border-slate-700/50">
                    <div className="flex items-center justify-between mb-3">
                        <div className="p-2.5 bg-sky-500/20 rounded-xl">
                            <CheckCircle className="w-5 h-5 text-sky-400" />
                        </div>
                        <Activity className="w-4 h-4 text-sky-400" />
                    </div>
                    <div className="text-3xl font-bold text-sky-400 mb-1">{data?.healthy || 0}</div>
                    <div className="text-xs text-slate-400 uppercase tracking-wide">Sağlıklı</div>
                </div>
            </div>

            {/* SQL Envanter Widget */}
            <div className="bg-gradient-to-br from-slate-800 to-slate-900/80 rounded-2xl p-5 border border-slate-700/50">
                <div className="flex items-center justify-between mb-4">
                    <div className="flex items-center gap-3">
                        <div className="p-2.5 bg-violet-500/20 rounded-xl">
                            <Database className="w-5 h-5 text-violet-400" />
                        </div>
                        <div>
                            <h3 className="text-base font-semibold text-white">SQL Envanter</h3>
                            <p className="text-xs text-slate-400">{sqlTotal} kayıtlı cihaz</p>
                        </div>
                    </div>
                    {sqlLoading && <div className="w-4 h-4 border-2 border-violet-500 border-t-transparent rounded-full animate-spin" />}
                </div>

                <div className="grid grid-cols-2 gap-4 mb-4">
                    {/* Online */}
                    <div className="bg-emerald-500/10 border border-emerald-500/30 rounded-xl p-4">
                        <div className="flex items-center gap-2 mb-2">
                            <div className="relative">
                                <Wifi className="w-5 h-5 text-emerald-400" />
                                <span className="absolute -top-0.5 -right-0.5 w-2 h-2 bg-emerald-400 rounded-full animate-ping" />
                            </div>
                            <span className="text-sm text-emerald-300 font-medium">Online</span>
                        </div>
                        <div className="text-4xl font-bold text-emerald-400">{sqlOnline}</div>
                    </div>

                    {/* Offline */}
                    <div className="bg-rose-500/10 border border-rose-500/30 rounded-xl p-4">
                        <div className="flex items-center gap-2 mb-2">
                            <WifiOff className="w-5 h-5 text-rose-400" />
                            <span className="text-sm text-rose-300 font-medium">Offline</span>
                        </div>
                        <div className="text-4xl font-bold text-rose-400">{sqlOffline}</div>
                    </div>
                </div>

                {/* Progress Bar */}
                <div>
                    <div className="flex justify-between text-xs mb-2">
                        <span className="text-slate-400">Çevrimiçi Oranı</span>
                        <span className="text-white font-semibold">{sqlOnlinePercent}%</span>
                    </div>
                    <div className="h-2.5 bg-slate-700/50 rounded-full overflow-hidden">
                        <div
                            className="h-full bg-gradient-to-r from-emerald-500 to-emerald-400 rounded-full transition-all duration-700"
                            style={{ width: `${sqlOnlinePercent}%` }}
                        />
                    </div>
                </div>
            </div>

            {/* Bottom Row */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
                {/* Health Donut */}
                <div className="bg-gradient-to-br from-slate-800 to-slate-900/80 rounded-2xl p-5 border border-slate-700/50">
                    <div className="flex items-center gap-3 mb-5">
                        <div className="p-2.5 bg-sky-500/20 rounded-xl">
                            <Activity className="w-5 h-5 text-sky-400" />
                        </div>
                        <h3 className="text-base font-semibold text-white">Sistem Sağlığı</h3>
                    </div>

                    <div className="flex items-center gap-8">
                        {/* Donut */}
                        <div className="relative w-32 h-32 flex-shrink-0">
                            <svg className="w-full h-full -rotate-90" viewBox="0 0 100 100">
                                <circle cx="50" cy="50" r="38" fill="none" stroke="#1e293b" strokeWidth="10" />
                                <circle
                                    cx="50" cy="50" r="38" fill="none"
                                    stroke="#10b981" strokeWidth="10"
                                    strokeLinecap="round"
                                    strokeDasharray={`${(data?.healthy || 0) / Math.max(data?.totalDevices || 1, 1) * 238.76} 238.76`}
                                    className="transition-all duration-1000"
                                />
                            </svg>
                            <div className="absolute inset-0 flex flex-col items-center justify-center">
                                <span className="text-2xl font-bold text-white">{data?.healthy || 0}</span>
                                <span className="text-xs text-slate-400">Sağlıklı</span>
                            </div>
                        </div>

                        {/* Legend */}
                        <div className="flex-1 space-y-3">
                            <div className="flex items-center justify-between">
                                <div className="flex items-center gap-2">
                                    <div className="w-3 h-3 bg-emerald-500 rounded-full" />
                                    <span className="text-sm text-slate-300">Sağlıklı</span>
                                </div>
                                <span className="text-sm font-semibold text-white">{data?.healthy || 0}</span>
                            </div>
                            <div className="flex items-center justify-between">
                                <div className="flex items-center gap-2">
                                    <div className="w-3 h-3 bg-amber-500 rounded-full" />
                                    <span className="text-sm text-slate-300">Uyarı</span>
                                </div>
                                <span className="text-sm font-semibold text-white">{data?.warning || 0}</span>
                            </div>
                            <div className="flex items-center justify-between">
                                <div className="flex items-center gap-2">
                                    <div className="w-3 h-3 bg-rose-500 rounded-full" />
                                    <span className="text-sm text-slate-300">Kritik</span>
                                </div>
                                <span className="text-sm font-semibold text-white">{data?.critical || 0}</span>
                            </div>
                        </div>
                    </div>
                </div>

                {/* Recent Offline */}
                <div className="bg-gradient-to-br from-slate-800 to-slate-900/80 rounded-2xl p-5 border border-slate-700/50">
                    <div className="flex items-center justify-between mb-4">
                        <div className="flex items-center gap-3">
                            <div className="p-2.5 bg-rose-500/20 rounded-xl">
                                <Clock className="w-5 h-5 text-rose-400" />
                            </div>
                            <h3 className="text-base font-semibold text-white">Son Çevrimdışı</h3>
                        </div>
                        <span className="text-xs px-2 py-1 bg-slate-700 text-slate-300 rounded-lg">
                            {data?.recentOffline?.length || 0} cihaz
                        </span>
                    </div>

                    <div className="space-y-2 max-h-[200px] overflow-y-auto pr-1">
                        {data?.recentOffline?.length === 0 ? (
                            <div className="flex flex-col items-center justify-center py-10 text-slate-500">
                                <CheckCircle className="w-10 h-10 mb-2 text-emerald-500/40" />
                                <span className="text-sm">Tüm cihazlar çevrimiçi</span>
                            </div>
                        ) : (
                            data?.recentOffline?.slice(0, 5).map((device, i) => (
                                <div key={i} className="flex items-center justify-between p-3 bg-slate-700/30 rounded-xl hover:bg-slate-700/50 transition-colors group cursor-pointer">
                                    <div className="flex items-center gap-3 min-w-0">
                                        <div className="w-2 h-2 bg-rose-500 rounded-full flex-shrink-0" />
                                        <div className="min-w-0">
                                            <div className="text-sm font-medium text-white truncate">{device.hostname}</div>
                                            <div className="text-xs text-slate-400">{device.ip}</div>
                                        </div>
                                    </div>
                                    <div className="flex items-center gap-3">
                                        <span className="text-xs text-slate-400">{formatLastSeen(device.lastSeen)}</span>
                                        <ChevronRight className="w-4 h-4 text-slate-600 group-hover:text-slate-400 transition-colors" />
                                    </div>
                                </div>
                            ))
                        )}
                    </div>
                </div>
            </div>
        </div>
    );
};

export default DashboardPage;
