import React, { useEffect, useState } from "react";
import { apiClient, SqlDeviceWithStatus } from "../../lib/apiClient";
import { Monitor, Wifi, WifiOff, RefreshCw } from "lucide-react";

interface SqlDeviceStatusWidgetProps {
    onCountChange?: (online: number, offline: number, total: number) => void;
}

const SqlDeviceStatusWidget: React.FC<SqlDeviceStatusWidgetProps> = ({ onCountChange }) => {
    const [devices, setDevices] = useState<SqlDeviceWithStatus[]>([]);
    const [loading, setLoading] = useState(true);
    const [animatedOnline, setAnimatedOnline] = useState(0);
    const [animatedOffline, setAnimatedOffline] = useState(0);

    const fetchDevices = async () => {
        setLoading(true);
        try {
            const data = await apiClient.getSqlDevicesWithStatus({ timeoutMs: 3000 });
            setDevices(data);

            const online = data.filter(d => d.isOnline).length;
            const offline = data.filter(d => !d.isOnline).length;

            onCountChange?.(online, offline, data.length);
        } catch (err) {
            console.error("Failed to fetch SQL devices:", err);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchDevices();
        const interval = setInterval(fetchDevices, 60000); // Refresh every minute
        return () => clearInterval(interval);
    }, []);

    // Animate numbers counting up
    useEffect(() => {
        const online = devices.filter(d => d.isOnline).length;
        const offline = devices.filter(d => !d.isOnline).length;

        const duration = 1000;
        const steps = 30;
        const onlineStep = online / steps;
        const offlineStep = offline / steps;

        let currentStep = 0;
        const timer = setInterval(() => {
            currentStep++;
            setAnimatedOnline(Math.min(Math.round(onlineStep * currentStep), online));
            setAnimatedOffline(Math.min(Math.round(offlineStep * currentStep), offline));

            if (currentStep >= steps) {
                clearInterval(timer);
                setAnimatedOnline(online);
                setAnimatedOffline(offline);
            }
        }, duration / steps);

        return () => clearInterval(timer);
    }, [devices]);

    const onlineCount = devices.filter(d => d.isOnline).length;
    const offlineCount = devices.filter(d => !d.isOnline).length;
    const total = devices.length;
    const onlinePercent = total > 0 ? (onlineCount / total) * 100 : 0;

    return (
        <div className="bg-gradient-to-br from-slate-800/80 to-slate-900/80 backdrop-blur-xl p-6 rounded-2xl border border-slate-700/50 shadow-2xl relative overflow-hidden">
            {/* Background Animation */}
            <div className="absolute inset-0 overflow-hidden">
                <div className="absolute -top-20 -right-20 w-40 h-40 bg-emerald-500/10 rounded-full blur-3xl animate-pulse" />
                <div className="absolute -bottom-20 -left-20 w-40 h-40 bg-rose-500/10 rounded-full blur-3xl animate-pulse" style={{ animationDelay: '1s' }} />
            </div>

            {/* Header */}
            <div className="relative flex items-center justify-between mb-6">
                <div className="flex items-center gap-3">
                    <div className="p-2 bg-indigo-500/20 rounded-xl">
                        <Monitor className="w-5 h-5 text-indigo-400" />
                    </div>
                    <div>
                        <h3 className="text-lg font-semibold text-white">SQL Envanter</h3>
                        <p className="text-xs text-slate-400">{total} cihaz</p>
                    </div>
                </div>
                <button
                    onClick={fetchDevices}
                    disabled={loading}
                    className="p-2 hover:bg-slate-700/50 rounded-lg transition-colors disabled:opacity-50"
                >
                    <RefreshCw className={`w-4 h-4 text-slate-400 ${loading ? 'animate-spin' : ''}`} />
                </button>
            </div>

            {/* Stats Grid */}
            <div className="relative grid grid-cols-2 gap-4 mb-6">
                {/* Online Card */}
                <div className="relative group">
                    <div className="absolute inset-0 bg-gradient-to-r from-emerald-500/20 to-emerald-600/10 rounded-xl blur-sm group-hover:blur-md transition-all" />
                    <div className="relative bg-slate-800/60 p-4 rounded-xl border border-emerald-500/30 hover:border-emerald-500/50 transition-all">
                        <div className="flex items-center gap-2 mb-2">
                            <div className="relative">
                                <Wifi className="w-5 h-5 text-emerald-400" />
                                <span className="absolute -top-0.5 -right-0.5 w-2 h-2 bg-emerald-400 rounded-full animate-ping" />
                            </div>
                            <span className="text-sm text-emerald-300 font-medium">Online</span>
                        </div>
                        <div className="text-4xl font-bold text-emerald-400 tabular-nums">
                            {animatedOnline}
                        </div>
                    </div>
                </div>

                {/* Offline Card */}
                <div className="relative group">
                    <div className="absolute inset-0 bg-gradient-to-r from-rose-500/20 to-rose-600/10 rounded-xl blur-sm group-hover:blur-md transition-all" />
                    <div className="relative bg-slate-800/60 p-4 rounded-xl border border-rose-500/30 hover:border-rose-500/50 transition-all">
                        <div className="flex items-center gap-2 mb-2">
                            <WifiOff className="w-5 h-5 text-rose-400" />
                            <span className="text-sm text-rose-300 font-medium">Offline</span>
                        </div>
                        <div className="text-4xl font-bold text-rose-400 tabular-nums">
                            {animatedOffline}
                        </div>
                    </div>
                </div>
            </div>

            {/* Progress Bar */}
            <div className="relative">
                <div className="flex items-center justify-between text-xs text-slate-400 mb-2">
                    <span>Çevrimiçi Oran</span>
                    <span className="font-medium text-white">{onlinePercent.toFixed(1)}%</span>
                </div>
                <div className="h-3 bg-slate-700/50 rounded-full overflow-hidden">
                    <div
                        className="h-full bg-gradient-to-r from-emerald-500 to-emerald-400 rounded-full transition-all duration-1000 ease-out relative"
                        style={{ width: `${onlinePercent}%` }}
                    >
                        <div className="absolute inset-0 bg-gradient-to-r from-transparent via-white/20 to-transparent animate-shimmer" />
                    </div>
                </div>
            </div>

            {/* Loading Overlay */}
            {loading && devices.length === 0 && (
                <div className="absolute inset-0 bg-slate-900/80 backdrop-blur-sm flex items-center justify-center rounded-2xl">
                    <div className="text-center">
                        <div className="w-10 h-10 border-3 border-indigo-500 border-t-transparent rounded-full animate-spin mx-auto mb-3" />
                        <p className="text-sm text-slate-400">Cihazlar taranıyor...</p>
                    </div>
                </div>
            )}
        </div>
    );
};

export default SqlDeviceStatusWidget;
