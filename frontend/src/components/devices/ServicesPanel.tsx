import React, { useState, useEffect, useRef } from 'react';
import { apiClient } from '../../lib/apiClient';
import { Play, Square, RotateCcw, RefreshCw, Search, Loader2 } from 'lucide-react';

interface ServiceItem {
    Name: string;
    DisplayName: string;
    Status: number; // 4=Running, 1=Stopped (PowerShell enum values)
    StartType: number; // 2=Auto, 3=Manual, 4=Disabled
}

interface ServicesPanelProps {
    deviceId: string;
}

const ServicesPanel: React.FC<ServicesPanelProps> = ({ deviceId }) => {
    const [services, setServices] = useState<ServiceItem[]>([]);
    const [loading, setLoading] = useState(false);
    const [refreshing, setRefreshing] = useState(false);
    const [search, setSearch] = useState('');
    const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
    const [error, setError] = useState<string | null>(null);

    // Command Polling Refs
    const pollingRef = useRef<NodeJS.Timeout | null>(null);
    const currentCommandId = useRef<string | null>(null);

    const fetchServices = async () => {
        setLoading(true);
        setError(null);
        try {
            // PowerShell command to get services as JSON
            const script = `Get-Service | Select-Object Name, DisplayName, Status, StartType | ConvertTo-Json -Compress`;
            const res = await apiClient.runScript(deviceId, script);
            currentCommandId.current = res.commandId;

            // Start polling for result
            startPolling(res.commandId);
        } catch (err) {
            setError("Failed to start service scan.");
            setLoading(false);
        }
    };

    const startPolling = (commandId: string) => {
        if (pollingRef.current) clearInterval(pollingRef.current);

        pollingRef.current = setInterval(async () => {
            try {
                // IMPORTANT: Use the CORRECT endpoint we fixed earlier
                const res: any = await apiClient.get<any>(`/api/agent/command-results/latest?deviceId=${deviceId}`);

                // Check if this result matches our command
                // Fix: Handle both PascalCase (C# default) and camelCase
                const resultId = res.commandId || res.CommandId;
                if (res && resultId === commandId) {
                    // Stop polling
                    if (pollingRef.current) clearInterval(pollingRef.current);

                    parseServiceData(res.output);
                    setLoading(false);
                    setRefreshing(false);
                    setLastUpdated(new Date(res.completedAtUtc));
                }
            } catch (ignore) {
                // Ignore errors during polling
            }
        }, 1000);
    };

    const parseServiceData = (jsonString: string) => {
        try {
            const data = JSON.parse(jsonString);
            // Handle single object vs array
            const list = Array.isArray(data) ? data : [data];
            setServices(list);
        } catch (err) {
            setError("Failed to parse service data from agent.");
            console.error(err);
        }
    };

    const handleServiceAction = async (serviceName: string, action: 'Start' | 'Stop' | 'Restart') => {
        if (!confirm(`Are you sure you want to ${action} service '${serviceName}'?`)) return;

        try {
            setRefreshing(true); // Show global activity indicator

            // Fix: Start-Service does NOT support -Force
            const forceParam = action === 'Start' ? '' : ' -Force';
            const script = `${action}-Service -Name "${serviceName}"${forceParam}`;

            await apiClient.runScript(deviceId, script);

            // Wait a bit then refresh list
            setTimeout(() => {
                fetchServices();
            }, 3000);
        } catch (err) {
            alert(`Failed to send ${action} command.`);
            setRefreshing(false);
        }
    };

    // Initial load
    useEffect(() => {
        fetchServices();
        return () => {
            if (pollingRef.current) clearInterval(pollingRef.current);
        };
    }, [deviceId]);

    // Filtering
    const filteredServices = services.filter(s =>
        s.Name.toLowerCase().includes(search.toLowerCase()) ||
        s.DisplayName.toLowerCase().includes(search.toLowerCase())
    );

    return (
        <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden shadow-sm">
            {/* Header */}
            <div className="p-4 border-b border-slate-800 flex flex-col sm:flex-row justify-between items-center gap-4">
                <div className="flex items-center gap-2">
                    <h3 className="font-semibold text-white">Windows Services</h3>
                    <span className="bg-slate-800 text-slate-400 text-xs px-2 py-0.5 rounded-full font-mono">
                        {services.length}
                    </span>
                    {loading && <span className="text-xs text-indigo-400 animate-pulse ml-2">Scanning...</span>}
                </div>

                <div className="flex items-center gap-2 w-full sm:w-auto">
                    <div className="relative flex-1 sm:w-64">
                        <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-500" />
                        <input
                            type="text"
                            placeholder="Search services..."
                            className="w-full bg-slate-950 border border-slate-800 rounded-lg pl-9 pr-3 py-1.5 text-sm text-slate-300 focus:outline-none focus:border-indigo-500"
                            value={search}
                            onChange={(e) => setSearch(e.target.value)}
                        />
                    </div>
                    <button
                        onClick={fetchServices}
                        disabled={loading || refreshing}
                        className="p-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-lg transition-colors disabled:opacity-50"
                        title="Refresh List"
                    >
                        <RefreshCw className={`w-4 h-4 ${loading || refreshing ? 'animate-spin' : ''}`} />
                    </button>
                </div>
            </div>

            {/* Content */}
            <div className="overflow-x-auto min-h-[300px] max-h-[600px]">
                {loading && services.length === 0 ? (
                    <div className="flex flex-col items-center justify-center h-64 text-slate-500">
                        <Loader2 className="w-8 h-8 animate-spin mb-2 text-indigo-500" />
                        <p>Fetching service inventory...</p>
                    </div>
                ) : error ? (
                    <div className="p-8 text-center text-rose-400 bg-rose-500/5 m-4 rounded-lg border border-rose-500/20">
                        <p>{error}</p>
                        <button onClick={fetchServices} className="mt-2 text-sm underline hover:text-rose-300">Try Again</button>
                    </div>
                ) : (
                    <table className="w-full text-left text-sm">
                        <thead className="bg-slate-950 text-slate-400 sticky top-0 z-10 shadow-sm">
                            <tr>
                                <th className="p-3 font-medium">Name</th>
                                <th className="p-3 font-medium">Display Name</th>
                                <th className="p-3 font-medium w-24">Status</th>
                                <th className="p-3 font-medium w-32 text-right">Actions</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-800">
                            {filteredServices.map((svc) => (
                                <tr key={svc.Name} className="hover:bg-slate-800/50 transition-colors group">
                                    <td className="p-3 font-medium text-slate-200">{svc.Name}</td>
                                    <td className="p-3 text-slate-400 truncate max-w-xs" title={svc.DisplayName}>{svc.DisplayName}</td>
                                    <td className="p-3">
                                        <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium border ${svc.Status === 4
                                            ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20'
                                            : 'bg-slate-800 text-slate-400 border-slate-700'
                                            }`}>
                                            <span className={`w-1.5 h-1.5 rounded-full ${svc.Status === 4 ? 'bg-emerald-400' : 'bg-slate-500'}`}></span>
                                            {svc.Status === 4 ? 'Running' : 'Stopped'}
                                        </span>
                                    </td>
                                    <td className="p-3 text-right">
                                        <div className="flex items-center justify-end gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                                            {svc.Status !== 4 ? (
                                                <button
                                                    onClick={() => handleServiceAction(svc.Name, 'Start')}
                                                    className="p-1.5 text-emerald-400 hover:bg-emerald-500/10 rounded transition-colors"
                                                    title="Start Service"
                                                >
                                                    <Play className="w-4 h-4" />
                                                </button>
                                            ) : (
                                                <button
                                                    onClick={() => handleServiceAction(svc.Name, 'Stop')}
                                                    className="p-1.5 text-rose-400 hover:bg-rose-500/10 rounded transition-colors"
                                                    title="Stop Service"
                                                >
                                                    <Square className="w-4 h-4" />
                                                </button>
                                            )}
                                            <button
                                                onClick={() => handleServiceAction(svc.Name, 'Restart')}
                                                className="p-1.5 text-indigo-400 hover:bg-indigo-500/10 rounded transition-colors"
                                                title="Restart Service"
                                            >
                                                <RotateCcw className="w-4 h-4" />
                                            </button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                            {filteredServices.length === 0 && (
                                <tr>
                                    <td colSpan={4} className="p-8 text-center text-slate-500">
                                        No services found matching "{search}"
                                    </td>
                                </tr>
                            )}
                        </tbody>
                    </table>
                )}
            </div>

            {lastUpdated && (
                <div className="px-4 py-2 bg-slate-950 text-xs text-slate-500 border-t border-slate-800 flex justify-end">
                    Last updated: {lastUpdated.toLocaleTimeString()}
                </div>
            )}
        </div>
    );
};

export default ServicesPanel;
