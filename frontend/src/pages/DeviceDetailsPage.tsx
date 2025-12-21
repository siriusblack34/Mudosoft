import React, { useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import MetricChart from "../components/ui/MetricChart";
import { apiClient } from "../lib/apiClient";
import type { Device, DeviceMetric, OsInfo } from "../types";
import {
    MonitorPlay, Cpu, HardDrive, MemoryStick, Monitor,
    Power, Settings, FolderOpen, Package, ArrowLeft, User, Clock, Terminal
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
            await apiClient.runScript(deviceId, 'shutdown /r /t 5 /c "Restart initiated by MudoSoft Admin"');
            alert('Restart command sent! The computer will restart in 5 seconds.');
        } catch (err) {
            console.error('Restart failed:', err);
            alert('Failed to send restart command.');
        } finally {
            setRestarting(false);
        }
    };

    if (loading || !deviceId) {
        return <div className="p-4 text-ms-text">Loading device details…</div>;
    }

    if (!deviceData) {
        return <div className="p-4 text-red-500">Could not load device details for {deviceId}.</div>;
    }

    const metrics: DeviceMetric[] = deviceData.metrics || [];
    const cpuData = metrics.map(m => ({ name: formatTimeLocal(m.timestampUtc), value: m.cpuUsagePercent }));
    const ramData = metrics.map(m => ({ name: formatTimeLocal(m.timestampUtc), value: m.ramUsagePercent }));
    const diskData = metrics.map(m => ({ name: formatTimeLocal(m.timestampUtc), value: m.diskUsagePercent }));

    const latestCpu = deviceData.cpuUsage ?? 0;
    const latestRam = deviceData.ramUsage ?? 0;
    const latestDisk = deviceData.diskUsage ?? 0;

    return (
        <div className="space-y-6 p-4">
            {/* Header with Back Button and Actions */}
            <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
                <div className="flex items-center gap-3">
                    <button
                        onClick={() => navigate('/devices')}
                        className="p-2 hover:bg-slate-700 rounded-lg transition-colors"
                    >
                        <ArrowLeft className="w-5 h-5" />
                    </button>
                    <h1 className="text-2xl font-semibold">{deviceData.hostname}</h1>
                    <span className={`px-2 py-1 rounded-full text-xs font-medium ${deviceData.online ? 'bg-emerald-500/20 text-emerald-400' : 'bg-red-500/20 text-red-400'}`}>
                        {deviceData.online ? 'Online' : 'Offline'}
                    </span>
                </div>

                <div className="flex flex-wrap gap-2">
                    <button
                        onClick={() => window.open(`/remote/${deviceId}`, '_blank')}
                        className="bg-emerald-600 hover:bg-emerald-500 text-white px-4 py-2 rounded-lg flex items-center gap-2 transition-colors shadow-lg shadow-emerald-900/20 text-sm"
                    >
                        <MonitorPlay className="w-4 h-4" />
                        Remote View
                    </button>
                    <button
                        onClick={() => navigate(`/devices/${deviceId}/services`)}
                        className="bg-indigo-600 hover:bg-indigo-500 text-white px-4 py-2 rounded-lg flex items-center gap-2 transition-colors text-sm"
                    >
                        <Settings className="w-4 h-4" />
                        Services
                    </button>
                    <button
                        onClick={() => navigate(`/devices/${deviceId}/files`)}
                        className="bg-violet-600 hover:bg-violet-500 text-white px-4 py-2 rounded-lg flex items-center gap-2 transition-colors text-sm"
                    >
                        <FolderOpen className="w-4 h-4" />
                        Files
                    </button>
                    <button
                        onClick={() => navigate(`/devices/${deviceId}/software`)}
                        className="bg-fuchsia-600 hover:bg-fuchsia-500 text-white px-4 py-2 rounded-lg flex items-center gap-2 transition-colors text-sm"
                    >
                        <Package className="w-4 h-4" />
                        Software
                    </button>
                    <button
                        onClick={() => navigate(`/devices/${deviceId}/script`)}
                        className="bg-amber-600 hover:bg-amber-500 text-white px-4 py-2 rounded-lg flex items-center gap-2 transition-colors text-sm"
                    >
                        <Terminal className="w-4 h-4" />
                        Script
                    </button>
                    <button
                        onClick={handleRestart}
                        disabled={restarting || !deviceData.online}
                        className="bg-orange-600 hover:bg-orange-500 text-white px-4 py-2 rounded-lg flex items-center gap-2 transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                        <Power className="w-4 h-4" />
                        {restarting ? 'Sending...' : 'Restart'}
                    </button>
                </div>
            </div>

            {/* Main Grid: Device Info + Hardware Inventory */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">

                {/* Device Information */}
                <section className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-lg">
                    <h2 className="text-lg font-medium mb-4 flex items-center gap-2">
                        <Monitor className="w-5 h-5 text-slate-400" />
                        Device Information
                    </h2>
                    <div className="space-y-3 text-sm">
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">Hostname:</span>
                            <span className="font-medium">{deviceData.hostname}</span>
                        </div>
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">IP Address:</span>
                            <span>{deviceData.ipAddress}</span>
                        </div>
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">OS:</span>
                            <span className="text-right">
                                {deviceData.os?.version && deviceData.os.version !== '-'
                                    ? deviceData.os.version
                                    : formatOsName(deviceData.os)}
                            </span>
                        </div>
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">Store Code:</span>
                            <span>{deviceData.storeCode ?? 'N/A'}</span>
                        </div>
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">Agent Version:</span>
                            <span>{deviceData.agentVersion ?? 'N/A'}</span>
                        </div>
                        <hr className="border-slate-700" />
                        <div className="flex justify-between items-center">
                            <span className="text-ms-text-muted flex items-center gap-1">
                                <Clock className="w-4 h-4" /> Uptime:
                            </span>
                            <span className="text-emerald-400 font-medium">{formatUptime(deviceData.systemBootTime)}</span>
                        </div>
                        <div className="flex justify-between items-center">
                            <span className="text-ms-text-muted flex items-center gap-1">
                                <User className="w-4 h-4" /> Logged In User:
                            </span>
                            <span>{deviceData.lastLoggedInUser ?? 'N/A'}</span>
                        </div>
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">Last Seen:</span>
                            <span className="text-right text-xs">{formatTimeLocal(deviceData.lastSeen)}</span>
                        </div>
                    </div>
                </section>

                {/* Hardware Inventory */}
                <section className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-lg">
                    <h2 className="text-lg font-medium mb-4 flex items-center gap-2">
                        <Cpu className="w-5 h-5 text-slate-400" />
                        Hardware Inventory
                    </h2>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                        <div className="bg-slate-800/50 p-4 rounded-xl border border-slate-700">
                            <div className="flex items-center gap-2 text-slate-400 text-xs mb-1">
                                <Cpu className="w-4 h-4" /> PROCESSOR
                            </div>
                            <div className="text-sm font-medium truncate" title={deviceData.cpuModel}>
                                {deviceData.cpuModel ?? 'N/A'}
                            </div>
                        </div>
                        <div className="bg-slate-800/50 p-4 rounded-xl border border-slate-700">
                            <div className="flex items-center gap-2 text-slate-400 text-xs mb-1">
                                <MemoryStick className="w-4 h-4" /> MEMORY
                            </div>
                            <div className="text-sm font-medium">
                                {formatRam(deviceData.totalRamMB)}
                            </div>
                        </div>
                        <div className="bg-slate-800/50 p-4 rounded-xl border border-slate-700">
                            <div className="flex items-center gap-2 text-slate-400 text-xs mb-1">
                                <HardDrive className="w-4 h-4" /> STORAGE
                            </div>
                            <div className="text-sm font-medium">
                                {deviceData.totalDiskGB ? (
                                    <>
                                        <span className="text-amber-400">
                                            {Math.round((deviceData.totalDiskGB * (deviceData.diskUsage || 0)) / 100)} GB
                                        </span>
                                        <span className="text-slate-500"> / {deviceData.totalDiskGB} GB</span>
                                    </>
                                ) : 'N/A'}
                            </div>
                        </div>
                        <div className="bg-slate-800/50 p-4 rounded-xl border border-slate-700">
                            <div className="flex items-center gap-2 text-slate-400 text-xs mb-1">
                                <Monitor className="w-4 h-4" /> GRAPHICS
                            </div>
                            <div className="text-sm font-medium truncate" title={deviceData.gpuModel ?? undefined}>
                                {deviceData.gpuModel ?? 'N/A'}
                            </div>
                        </div>
                    </div>
                </section>
            </div>

            {/* Performance Metrics */}
            <section>
                <h2 className="text-lg font-medium mb-4">Performance Metrics (Last 24 Hours)</h2>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                    <div className="p-3 bg-ms-panel rounded-2xl border border-ms-border shadow-sm">
                        <MetricChart title="CPU Usage" data={cpuData} value={latestCpu} color="#f87171" height={150} />
                    </div>
                    <div className="p-3 bg-ms-panel rounded-2xl border border-ms-border shadow-sm">
                        <MetricChart title="RAM Usage" data={ramData} value={latestRam} color="#60a5fa" height={150} />
                    </div>
                    <div className="p-3 bg-ms-panel rounded-2xl border border-ms-border shadow-sm">
                        <MetricChart title="Disk Usage" data={diskData} value={latestDisk} color="#34d399" height={150} />
                    </div>
                </div>
            </section>

        </div>
    );
};

export default DeviceDetailsPage;