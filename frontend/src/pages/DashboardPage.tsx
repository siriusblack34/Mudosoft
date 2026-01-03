import React, { useEffect, useState } from "react";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import {
    Monitor, Wifi, WifiOff, RefreshCw, Activity,
    CheckCircle, Clock, Server, MonitorSmartphone
} from "lucide-react";

interface DashboardData {
    totalDevices: number;
    online: number;
    offline: number;
    healthy: number;
    warning: number;
    critical: number;
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
        } catch (err) {
            console.error("SQL devices load failed:", err);
        } finally {
            setSqlLoading(false);
        }
    };

    useEffect(() => {
        loadDashboard();
        loadSqlDevices();
        // Refresh every 30 seconds (no live SignalR needed)
        const interval = setInterval(() => {
            loadDashboard();
            loadSqlDevices();
        }, 30000);

        return () => clearInterval(interval);
    }, []);

    // SQL Device counts by type
    const pcDevices = sqlDevices.filter(d => d.deviceType?.toUpperCase() === 'PC');
    const posDevices = sqlDevices.filter(d => d.deviceType?.toUpperCase() !== 'PC');
    const pcOnline = pcDevices.filter(d => d.isOnline).length;
    const pcOffline = pcDevices.length - pcOnline;
    const posOnline = posDevices.filter(d => d.isOnline).length;
    const posOffline = posDevices.length - posOnline;

    if (loading && !data) {
        return (
            <div className="flex h-screen items-center justify-center bg-[#0f172a]">
                <div className="w-8 h-8 border-2 border-sky-500 border-t-transparent rounded-full animate-spin" />
            </div>
        );
    }

    return (
        <div className="p-6 space-y-6 bg-[#0f172a] min-h-screen text-slate-200">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div>
                    <h1 className="text-2xl font-bold text-white tracking-tight flex items-center gap-2">
                        <Activity className="text-sky-500" />
                        Dashboard
                    </h1>
                    <p className="text-sm text-slate-400">Sistem Durumu Özeti</p>
                </div>
                <div className="flex items-center gap-3">
                    <span className="text-xs text-slate-500">
                        Son güncelleme: {lastUpdated.toLocaleTimeString('tr-TR')}
                    </span>
                    <button
                        onClick={() => { loadDashboard(); loadSqlDevices(); }}
                        className="p-2 bg-slate-700/50 hover:bg-slate-600/50 rounded-lg transition-colors"
                    >
                        <RefreshCw className="w-4 h-4 text-slate-400" />
                    </button>
                </div>
            </div>

            {/* Main Content Grid */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">

                {/* LEFT: Agent Status - Vertical Layout */}
                <div className="bg-[#1e293b]/50 border border-slate-700/50 rounded-2xl p-6">
                    <h2 className="text-lg font-semibold text-white mb-4 flex items-center gap-2">
                        <Server className="w-5 h-5 text-sky-400" />
                        Agent Durumu
                    </h2>

                    <div className="space-y-3">
                        {/* Toplam */}
                        <div className="flex items-center justify-between p-4 bg-slate-800/50 rounded-xl border border-slate-700/30">
                            <div className="flex items-center gap-3">
                                <div className="p-2 bg-indigo-500/20 rounded-lg">
                                    <Monitor className="w-5 h-5 text-indigo-400" />
                                </div>
                                <span className="text-slate-300">Toplam Cihaz</span>
                            </div>
                            <span className="text-2xl font-bold text-white">{data?.totalDevices || 0}</span>
                        </div>

                        {/* Online */}
                        <div className="flex items-center justify-between p-4 bg-slate-800/50 rounded-xl border border-emerald-500/20">
                            <div className="flex items-center gap-3">
                                <div className="p-2 bg-emerald-500/20 rounded-lg">
                                    <Wifi className="w-5 h-5 text-emerald-400" />
                                </div>
                                <span className="text-slate-300">Online</span>
                            </div>
                            <span className="text-2xl font-bold text-emerald-400">{data?.online || 0}</span>
                        </div>

                        {/* Sağlıklı */}
                        <div className="flex items-center justify-between p-4 bg-slate-800/50 rounded-xl border border-sky-500/20">
                            <div className="flex items-center gap-3">
                                <div className="p-2 bg-sky-500/20 rounded-lg">
                                    <CheckCircle className="w-5 h-5 text-sky-400" />
                                </div>
                                <span className="text-slate-300">Sağlıklı</span>
                            </div>
                            <span className="text-2xl font-bold text-sky-400">{data?.healthy || 0}</span>
                        </div>
                    </div>
                </div>

                {/* RIGHT: SQL Devices - PC vs POS */}
                <div className="bg-[#1e293b]/50 border border-slate-700/50 rounded-2xl p-6">
                    <h2 className="text-lg font-semibold text-white mb-4 flex items-center gap-2">
                        <MonitorSmartphone className="w-5 h-5 text-violet-400" />
                        SQL Cihazları
                        {sqlLoading && <RefreshCw className="w-4 h-4 text-slate-500 animate-spin ml-2" />}
                    </h2>

                    <div className="grid grid-cols-2 gap-4">
                        {/* PC Column */}
                        <div className="bg-slate-800/50 rounded-xl p-4 border border-slate-700/30">
                            <div className="flex items-center gap-2 mb-4">
                                <Monitor className="w-5 h-5 text-blue-400" />
                                <span className="font-medium text-white">PC</span>
                                <span className="ml-auto text-xs text-slate-500">{pcDevices.length} toplam</span>
                            </div>

                            <div className="space-y-2">
                                <div className="flex items-center justify-between">
                                    <span className="text-sm text-slate-400 flex items-center gap-1.5">
                                        <span className="w-2 h-2 rounded-full bg-emerald-500"></span>
                                        Online
                                    </span>
                                    <span className="font-bold text-emerald-400">{pcOnline}</span>
                                </div>
                                <div className="flex items-center justify-between">
                                    <span className="text-sm text-slate-400 flex items-center gap-1.5">
                                        <span className="w-2 h-2 rounded-full bg-rose-500"></span>
                                        Offline
                                    </span>
                                    <span className="font-bold text-rose-400">{pcOffline}</span>
                                </div>
                            </div>
                        </div>

                        {/* POS Column */}
                        <div className="bg-slate-800/50 rounded-xl p-4 border border-slate-700/30">
                            <div className="flex items-center gap-2 mb-4">
                                <MonitorSmartphone className="w-5 h-5 text-amber-400" />
                                <span className="font-medium text-white">POS</span>
                                <span className="ml-auto text-xs text-slate-500">{posDevices.length} toplam</span>
                            </div>

                            <div className="space-y-2">
                                <div className="flex items-center justify-between">
                                    <span className="text-sm text-slate-400 flex items-center gap-1.5">
                                        <span className="w-2 h-2 rounded-full bg-emerald-500"></span>
                                        Online
                                    </span>
                                    <span className="font-bold text-emerald-400">{posOnline}</span>
                                </div>
                                <div className="flex items-center justify-between">
                                    <span className="text-sm text-slate-400 flex items-center gap-1.5">
                                        <span className="w-2 h-2 rounded-full bg-rose-500"></span>
                                        Offline
                                    </span>
                                    <span className="font-bold text-rose-400">{posOffline}</span>
                                </div>
                            </div>
                        </div>
                    </div>

                    {/* Summary Bar */}
                    <div className="mt-4 pt-4 border-t border-slate-700/30">
                        <div className="flex justify-between text-sm">
                            <span className="text-slate-400">Toplam SQL Cihazı</span>
                            <span className="text-white font-medium">{sqlDevices.length}</span>
                        </div>
                        <div className="mt-2 h-2 bg-slate-700 rounded-full overflow-hidden">
                            <div
                                className="h-full bg-gradient-to-r from-emerald-500 to-emerald-400 transition-all duration-500"
                                style={{ width: `${sqlDevices.length > 0 ? ((pcOnline + posOnline) / sqlDevices.length * 100) : 0}%` }}
                            />
                        </div>
                        <div className="flex justify-between text-xs mt-1 text-slate-500">
                            <span>{pcOnline + posOnline} online</span>
                            <span>{pcOffline + posOffline} offline</span>
                        </div>
                    </div>
                </div>
            </div>

            {/* Quick Info Cards */}
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
                <QuickCard
                    icon={Clock}
                    label="Son Kontrol"
                    value={lastUpdated.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}
                    color="slate"
                />
                <QuickCard
                    icon={Wifi}
                    label="Agent Online"
                    value={`${data?.online || 0}/${data?.totalDevices || 0}`}
                    color="emerald"
                />
                <QuickCard
                    icon={Monitor}
                    label="SQL Online"
                    value={`${pcOnline + posOnline}/${sqlDevices.length}`}
                    color="sky"
                />
                <QuickCard
                    icon={WifiOff}
                    label="Toplam Offline"
                    value={(data?.offline || 0) + (pcOffline + posOffline)}
                    color="rose"
                />
            </div>
        </div>
    );
};

const QuickCard = ({ icon: Icon, label, value, color }: { icon: any; label: string; value: any; color: string }) => {
    const colors: Record<string, string> = {
        slate: "text-slate-400",
        emerald: "text-emerald-400",
        sky: "text-sky-400",
        rose: "text-rose-400",
        amber: "text-amber-400"
    };

    return (
        <div className="bg-[#1e293b]/30 border border-slate-700/30 p-4 rounded-xl flex items-center gap-3">
            <Icon className={`w-5 h-5 ${colors[color]}`} />
            <div>
                <div className="text-xs text-slate-500 uppercase">{label}</div>
                <div className={`text-lg font-semibold ${colors[color]}`}>{value}</div>
            </div>
        </div>
    );
};

export default DashboardPage;
