import React, { useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import MetricChart from "../components/ui/MetricChart";
import { apiClient } from "../lib/apiClient";
import type { Device, DeviceMetric, OsInfo } from "../types";
import {
    Cpu, HardDrive, MemoryStick, Monitor,
    Power, Settings, FolderOpen, Package, ArrowLeft, User, Clock, Terminal, Trash2, Activity
} from "lucide-react";

// UTC to Local Time formatter
const formatTimeLocal = (utcString: string | null | undefined) => {
    if (!utcString) return "N/A";
    const cleanString = utcString.endsWith('Z') ? utcString : utcString + 'Z';
    const date = new Date(cleanString);
    if (isNaN(date.getTime())) return "Invalid Date";

    const options: Intl.DateTimeFormatOptions = {
        year: 'numeric', month: 'numeric', day: 'numeric',
        hour: '2-digit', minute: '2-digit', second: '2-digit',
        hour12: false,
    };
    return date.toLocaleString(undefined, options);
};

// Format uptime from boot time
const formatUptime = (bootTimeStr: string | null | undefined) => {
    if (!bootTimeStr) return "N/A";
    const cleanString = bootTimeStr.endsWith('Z') ? bootTimeStr : bootTimeStr + 'Z';
    const bootTime = new Date(cleanString);
    if (isNaN(bootTime.getTime())) return "N/A";

    const now = new Date();
    const diffMs = now.getTime() - bootTime.getTime();

    const days = Math.floor(diffMs / (1000 * 60 * 60 * 24));
    const hours = Math.floor((diffMs % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
    const minutes = Math.floor((diffMs % (1000 * 60 * 60)) / (1000 * 60));

    if (days > 0) return `${days}d ${hours}h ${minutes}m`;
    if (hours > 0) return `${hours}h ${minutes}m`;
    return `${minutes}m`;
};

// OS name formatter
const formatOsName = (osInfo?: OsInfo) => {
    if (!osInfo || !osInfo.name) return "N/A";
    const name = osInfo.name.trim();
    if (!name) return "N/A";
    if (name.includes('NT 6.1')) return 'Windows 7';
    if (name.includes('NT 6.2')) return 'Windows 8';
    if (name.includes('NT 6.3')) return 'Windows 8.1';
    if (name.includes('NT 10.0')) return 'Windows 10/11';
    return name;
};

// Format RAM size
const formatRam = (mb?: number) => {
    if (!mb) return "N/A";
    const gb = Math.round(mb / 1024);
    return `${gb} GB`;
};

const DeviceDetailsPage: React.FC = () => {
    const { deviceId } = useParams<{ deviceId: string }>();
    const navigate = useNavigate();
    const [deviceData, setDeviceData] = useState<Device | null>(null);
    const [loading, setLoading] = useState(true);
    const [restarting, setRestarting] = useState(false);
    const [cleanupPaths, setCleanupPaths] = useState<string[]>([]);
    const [customPath, setCustomPath] = useState('');
    const [cleaning, setCleaning] = useState(false);
    const [cleanupResult, setCleanupResult] = useState<string | null>(null);

    useEffect(() => {
        if (!deviceId) {
            setLoading(false);
            return;
        }

        let cancelled = false;

        const loadData = async () => {
            try {
                const fullDeviceData = await apiClient.getDevice(deviceId);
                if (!cancelled) setDeviceData(fullDeviceData);
            } catch (err) {
                console.error("❌ Failed to load device:", err);
                if (!cancelled) setDeviceData(null);
            } finally {
                if (!cancelled) setLoading(false);
            }
        };

        loadData();
        const dataIntervalId = setInterval(loadData, 10000);

        return () => {
            cancelled = true;
            clearInterval(dataIntervalId);
        };
    }, [deviceId]);

    const handleRestart = async () => {
        if (!deviceId) return;
        const confirmed = window.confirm('Are you sure you want to restart this computer? This will close all open applications.');
        if (!confirmed) return;

        setRestarting(true);
        try {
            await apiClient.runScript(deviceId, 'shutdown /r /t 0');
            alert('Restart komutu gönderildi! Bilgisayar şimdi yeniden başlatılıyor.');
        } catch (err) {
            console.error('Restart failed:', err);
            alert('Failed to send restart command.');
        } finally {
            setRestarting(false);
        }
    };

    const predefinedPaths = [
        { label: 'Windows Temp', path: 'C:\\Windows\\Temp' },
        { label: 'User Temp', path: '%USERTEMP%' },
        { label: 'Prefetch', path: 'C:\\Windows\\Prefetch' },
    ];

    const togglePath = (path: string) => {
        setCleanupPaths(prev =>
            prev.includes(path)
                ? prev.filter(p => p !== path)
                : [...prev, path]
        );
    };

    const handleCleanup = async () => {
        if (!deviceId) return;

        const pathsToClean = [...cleanupPaths];
        if (customPath.trim()) {
            pathsToClean.push(customPath.trim());
        }

        if (pathsToClean.length === 0) {
            alert('Lütfen en az bir klasör seçin veya path girin.');
            return;
        }

        const confirmed = window.confirm(`${pathsToClean.length} klasör temizlenecek. Onaylıyor musunuz?`);
        if (!confirmed) return;

        setCleaning(true);
        setCleanupResult(null);

        try {
            const results: string[] = [];
            for (const path of pathsToClean) {
                const { commandId } = await apiClient.folderCleanup(deviceId, path);
                results.push(`✓ ${path} (Command: ${commandId.substring(0, 8)}...)`);
            }
            setCleanupResult(results.join('\n'));
            setCleanupPaths([]);
            setCustomPath('');
        } catch (err) {
            console.error('Cleanup failed:', err);
            setCleanupResult('❌ Temizlik komutu gönderilemedi!');
        } finally {
            setCleaning(false);
        }
    };

    if (loading || !deviceId) {
        return (
            <div className="flex items-center justify-center min-h-[400px]">
                <div className="w-8 h-8 border-2 border-emerald-500 border-t-transparent rounded-full animate-spin" />
            </div>
        );
    }

    if (!deviceData) {
        return (
            <div className="flex flex-col items-center justify-center min-h-[400px] text-gray-400">
                <Monitor className="w-16 h-16 mb-4 opacity-50" />
                <p>Could not load device details for {deviceId}.</p>
                <button
                    onClick={() => navigate('/devices')}
                    className="mt-4 px-4 py-2 bg-gray-700 hover:bg-gray-600 rounded-lg"
                >
                    Back to Devices
                </button>
            </div>
        );
    }

    const metrics: DeviceMetric[] = deviceData.metrics || [];
    const cpuData = metrics.map(m => ({ name: formatTimeLocal(m.timestampUtc), value: m.cpuUsagePercent }));
    const ramData = metrics.map(m => ({ name: formatTimeLocal(m.timestampUtc), value: m.ramUsagePercent }));
    const diskData = metrics.map(m => ({ name: formatTimeLocal(m.timestampUtc), value: m.diskUsagePercent }));

    const latestCpu = deviceData.cpuUsage ?? 0;
    const latestRam = deviceData.ramUsage ?? 0;
    const latestDisk = deviceData.diskUsage ?? 0;

    return (
        <div className="space-y-6">
            {/* Header */}
            <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-4">
                <div className="flex items-center gap-4">
                    <button
                        onClick={() => navigate('/devices')}
                        className="p-2 hover:bg-gray-700/50 rounded-xl transition-colors"
                    >
                        <ArrowLeft className="w-5 h-5 text-gray-400" />
                    </button>
                    <div>
                        <div className="flex items-center gap-3">
                            <h1 className="text-2xl font-bold text-white">{deviceData.hostname}</h1>
                            <span className={`px-3 py-1 rounded-full text-xs font-semibold ${deviceData.online
                                ? 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/30'
                                : 'bg-red-500/20 text-red-400 border border-red-500/30'
                                }`}>
                                {deviceData.online ? '● Online' : '○ Offline'}
                            </span>
                        </div>
                        <p className="text-sm text-gray-500 mt-1">{deviceData.ipAddress} • {deviceData.storeCode || 'No Store'}</p>
                    </div>
                </div>

                {/* Action Buttons */}
                <div className="flex flex-wrap gap-2">
                    <ActionButton icon={Activity} label="Health" color="emerald" onClick={() => navigate(`/devices/${deviceId}/health`)} />
                    <ActionButton icon={Settings} label="Services" color="indigo" onClick={() => navigate(`/devices/${deviceId}/services`)} />
                    <ActionButton icon={FolderOpen} label="Files" color="violet" onClick={() => navigate(`/devices/${deviceId}/files`)} />
                    <ActionButton icon={Package} label="Software" color="fuchsia" onClick={() => navigate(`/devices/${deviceId}/software`)} />
                    <ActionButton icon={Terminal} label="Script" color="amber" onClick={() => navigate(`/devices/${deviceId}/script`)} />
                    <ActionButton
                        icon={Power}
                        label={restarting ? 'Sending...' : 'Restart'}
                        color="red"
                        onClick={handleRestart}
                        disabled={restarting || !deviceData.online}
                    />
                </div>
            </div>

            {/* Main Grid */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                {/* Device Information */}
                <div className="p-6 rounded-2xl glass-card relative overflow-hidden group">
                    <div className="absolute top-0 right-0 w-32 h-32 bg-emerald-500/10 rounded-full blur-3xl group-hover:bg-emerald-500/20 transition-all duration-500"></div>
                    <h2 className="text-lg font-semibold mb-5 flex items-center gap-2 text-white relative z-10">
                        <div className="p-2 rounded-xl bg-emerald-500/10 shadow-inner border border-white/5">
                            <Monitor className="w-5 h-5 text-emerald-400" />
                        </div>
                        Device Information
                    </h2>
                    <div className="space-y-4">
                        <InfoRow label="Hostname" value={deviceData.hostname} />
                        <InfoRow label="IP Address" value={deviceData.ipAddress} />
                        <InfoRow label="OS" value={deviceData.os?.version && deviceData.os.version !== '-' ? deviceData.os.version : formatOsName(deviceData.os)} />
                        <InfoRow label="Store Code" value={deviceData.storeCode ? String(deviceData.storeCode) : 'N/A'} />
                        <InfoRow label="Agent Version" value={deviceData.agentVersion ? String(deviceData.agentVersion) : 'N/A'} highlight />
                        <div className="h-px bg-white/5 my-4" />
                        <InfoRow label="Uptime" value={formatUptime(deviceData.systemBootTime)} icon={Clock} valueClass="text-emerald-400" />
                        <InfoRow label="Logged In User" value={deviceData.lastLoggedInUser ?? 'N/A'} icon={User} />
                        <InfoRow label="Last Seen" value={formatTimeLocal(deviceData.lastSeen)} small />
                    </div>
                </div>

                {/* Hardware Inventory */}
                <div className="p-6 rounded-2xl glass-card relative overflow-hidden group">
                    <div className="absolute -bottom-10 right-10 w-40 h-40 bg-blue-500/10 rounded-full blur-3xl group-hover:bg-blue-500/20 transition-all duration-500"></div>
                    <h2 className="text-lg font-semibold mb-5 flex items-center gap-2 text-white relative z-10">
                        <div className="p-2 rounded-xl bg-blue-500/10 shadow-inner border border-white/5">
                            <Cpu className="w-5 h-5 text-blue-400" />
                        </div>
                        Hardware Inventory
                    </h2>
                    <div className="grid grid-cols-2 gap-4">
                        <HardwareCard icon={Cpu} label="PROCESSOR" value={deviceData.cpuModel ?? 'N/A'} color="blue" />
                        <HardwareCard icon={MemoryStick} label="MEMORY" value={formatRam(deviceData.totalRamMB)} color="purple" />
                        <HardwareCard
                            icon={HardDrive}
                            label="STORAGE"
                            value={deviceData.totalDiskGB ? `${Math.round((deviceData.totalDiskGB * (deviceData.diskUsage || 0)) / 100)} / ${deviceData.totalDiskGB} GB` : 'N/A'}
                            color="amber"
                        />
                        <HardwareCard icon={Monitor} label="GRAPHICS" value={deviceData.gpuModel ?? 'N/A'} color="green" />
                    </div>
                </div>
            </div>

            {/* Quick Cleanup */}
            <div className="p-6 rounded-2xl glass-card relative overflow-hidden group">
                <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-64 h-64 bg-rose-500/5 rounded-full blur-3xl group-hover:bg-rose-500/10 transition-all duration-500"></div>
                <h2 className="text-lg font-semibold mb-5 flex items-center gap-2 text-white relative z-10">
                    <div className="p-2 rounded-xl bg-rose-500/10 shadow-inner border border-white/5">
                        <Trash2 className="w-5 h-5 text-rose-400" />
                    </div>
                    Quick Cleanup
                </h2>
                <div className="space-y-4">
                    <div className="flex flex-wrap gap-2">
                        {predefinedPaths.map(({ label, path }) => (
                            <button
                                key={path}
                                onClick={() => togglePath(path)}
                                className={`px-4 py-2 rounded-xl text-sm font-medium transition-all duration-200 ${cleanupPaths.includes(path)
                                    ? 'bg-rose-500 text-white shadow-lg shadow-rose-500/25'
                                    : 'bg-gray-700/50 text-gray-300 hover:bg-gray-600/50 border border-gray-600/50'
                                    }`}
                            >
                                {cleanupPaths.includes(path) ? '✓ ' : ''}{label}
                            </button>
                        ))}
                    </div>

                    <div className="flex gap-3">
                        <input
                            type="text"
                            value={customPath}
                            onChange={(e) => setCustomPath(e.target.value)}
                            placeholder="Özel path girin (örn: C:\Users\...\Downloads)"
                            className="flex-1 px-4 py-3 rounded-xl glass-panel text-sm text-white placeholder-slate-400 focus-ring transition-all relative z-10"
                        />
                        <button
                            onClick={handleCleanup}
                            disabled={cleaning || !deviceData.online || (cleanupPaths.length === 0 && !customPath.trim())}
                            className="px-6 py-3 bg-gradient-to-r from-rose-600 to-rose-500 hover:from-rose-500 hover:to-rose-400 text-white rounded-xl flex items-center gap-2 transition-all duration-300 hover:shadow-[0_0_20px_rgba(244,63,94,0.4)] disabled:opacity-50 disabled:cursor-not-allowed hover:-translate-y-0.5 relative z-10"
                        >
                            <Trash2 className="w-4 h-4" />
                            {cleaning ? 'Temizleniyor...' : 'Temizle'}
                        </button>
                    </div>

                    {cleanupResult && (
                        <div className="p-4 rounded-xl bg-gray-800/50 border border-gray-600/50 text-sm whitespace-pre-line text-gray-300">
                            {cleanupResult}
                        </div>
                    )}

                    {!deviceData.online && (
                        <p className="text-sm text-amber-400 flex items-center gap-2">
                            <Activity className="w-4 h-4" />
                            Cihaz offline - temizlik komutu gönderilemez.
                        </p>
                    )}
                </div>
            </div>

            {/* Performance Metrics */}
            <div className="space-y-4">
                <h2 className="text-lg font-semibold text-white flex items-center gap-2">
                    <Activity className="w-5 h-5 text-emerald-400" />
                    Performance Metrics
                    <span className="text-xs text-gray-500 font-normal ml-2">Last 24 Hours</span>
                </h2>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                    <MetricCard title="CPU Usage" data={cpuData} value={latestCpu} color="#f87171" />
                    <MetricCard title="RAM Usage" data={ramData} value={latestRam} color="#60a5fa" />
                    <MetricCard title="Disk Usage" data={diskData} value={latestDisk} color="#34d399" />
                </div>
            </div>
        </div>
    );
};

// Helper Components
const ActionButton: React.FC<{
    icon: React.FC<{ className?: string }>;
    label: string;
    color: string;
    onClick: () => void;
    disabled?: boolean;
}> = ({ icon: Icon, label, color, onClick, disabled }) => {
    const colorMap: Record<string, string> = {
        emerald: 'bg-emerald-600 hover:bg-emerald-500 shadow-emerald-900/20',
        indigo: 'bg-indigo-600 hover:bg-indigo-500 shadow-indigo-900/20',
        violet: 'bg-violet-600 hover:bg-violet-500 shadow-violet-900/20',
        fuchsia: 'bg-fuchsia-600 hover:bg-fuchsia-500 shadow-fuchsia-900/20',
        amber: 'bg-amber-600 hover:bg-amber-500 shadow-amber-900/20',
        red: 'bg-red-600 hover:bg-red-500 shadow-red-900/20',
    };

    return (
        <button
            onClick={onClick}
            disabled={disabled}
            className={`${colorMap[color]} text-white px-4 py-2 rounded-xl flex items-center gap-2 transition-all text-sm font-medium shadow-lg disabled:opacity-50 disabled:cursor-not-allowed`}
        >
            <Icon className="w-4 h-4" />
            {label}
        </button>
    );
};

const InfoRow: React.FC<{
    label: string;
    value: string | null | undefined;
    icon?: React.FC<{ className?: string }>;
    valueClass?: string;
    highlight?: boolean;
    small?: boolean;
}> = ({ label, value, icon: Icon, valueClass, highlight, small }) => (
    <div className="flex justify-between items-center">
        <span className="text-gray-500 flex items-center gap-2 text-sm">
            {Icon && <Icon className="w-4 h-4" />}
            {label}
        </span>
        <span className={`${valueClass || 'text-white'} ${highlight ? 'px-2 py-1 bg-emerald-500/10 rounded-md text-emerald-400' : ''} ${small ? 'text-xs text-gray-400' : 'font-medium'}`}>
            {value || 'N/A'}
        </span>
    </div>
);

const HardwareCard: React.FC<{
    icon: React.FC<{ className?: string }>;
    label: string;
    value: string;
    color: string;
}> = ({ icon: Icon, label, value, color }) => {
    const colorMap: Record<string, string> = {
        blue: 'bg-blue-500/10 text-blue-400',
        purple: 'bg-purple-500/10 text-purple-400',
        amber: 'bg-amber-500/10 text-amber-400',
        green: 'bg-green-500/10 text-green-400',
    };

    return (
        <div className="p-4 rounded-xl glass-panel relative z-10 hover-lift">
            <div className={`inline-flex p-2 rounded-lg ${colorMap[color]} mb-3 shadow-inner border border-white/5`}>
                <Icon className="w-4 h-4" />
            </div>
            <p className="text-[11px] font-semibold text-slate-400 mb-1 tracking-wider">{label}</p>
            <p className="text-sm font-bold text-white truncate" title={value}>{value}</p>
        </div>
    );
};

const MetricCard: React.FC<{
    title: string;
    data: { name: string; value: number }[];
    value: number;
    color: string;
}> = ({ title, data, value, color }) => (
    <div className="p-4 rounded-2xl glass-card hover-lift">
        <MetricChart title={title} data={data} value={value} color={color} height={150} />
    </div>
);

export default DeviceDetailsPage;