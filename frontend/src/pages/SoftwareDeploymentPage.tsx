import React, { useState, useEffect, useRef } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { ArrowLeft, Package, Search, RefreshCcw, AlertCircle } from "lucide-react";
import { apiClient } from "../lib/apiClient";

interface InstalledSoftware {
    displayName: string;
    displayVersion: string;
    publisher: string;
    installDate?: string;
}

const SoftwareDeploymentPage: React.FC = () => {
    const { deviceId } = useParams<{ deviceId: string }>();
    const navigate = useNavigate();
    const [software, setSoftware] = useState<InstalledSoftware[]>([]);
    const [loading, setLoading] = useState(false);
    const [searchQuery, setSearchQuery] = useState("");
    const [error, setError] = useState<string | null>(null);
    const pollingRef = useRef<NodeJS.Timeout | null>(null);
    const commandIdRef = useRef<string | null>(null);

    const loadInstalledSoftware = async () => {
        if (!deviceId) return;
        setLoading(true);
        setError(null);
        setSoftware([]);

        try {
            // PowerShell script to get installed programs
            const script = `
$apps = @()
$paths = @(
    'HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*',
    'HKLM:\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'
)
foreach ($path in $paths) {
    $apps += Get-ItemProperty $path -ErrorAction SilentlyContinue | 
        Where-Object { $_.DisplayName } |
        Select-Object DisplayName, DisplayVersion, Publisher, InstallDate
}
$apps | Sort-Object DisplayName -Unique | ConvertTo-Json -Compress
`;

            const result = await apiClient.runScript(deviceId, script);
            commandIdRef.current = result.commandId;

            // Start polling for result
            let attempts = 0;
            pollingRef.current = setInterval(async () => {
                attempts++;
                try {
                    const res = await apiClient.get<any>(
                        `/api/agent/command-results/latest?deviceId=${deviceId}`
                    );

                    const resultId = res.commandId || res.CommandId;
                    if (res && resultId === commandIdRef.current && res.output) {
                        if (pollingRef.current) clearInterval(pollingRef.current);
                        setLoading(false);

                        // Parse JSON output
                        try {
                            const parsed = JSON.parse(res.output);
                            // Handle both array and single object
                            const apps = Array.isArray(parsed) ? parsed : [parsed];
                            setSoftware(apps.filter((a: any) => a.displayName || a.DisplayName).map((a: any) => ({
                                displayName: a.displayName || a.DisplayName || '',
                                displayVersion: a.displayVersion || a.DisplayVersion || '-',
                                publisher: a.publisher || a.Publisher || '-',
                                installDate: a.installDate || a.InstallDate
                            })));
                        } catch {
                            setError("Failed to parse software list");
                        }
                    }
                } catch (err) {
                    console.error("Polling error:", err);
                }

                if (attempts >= 200) { // Increased to 200 (60s total)
                    if (pollingRef.current) clearInterval(pollingRef.current);
                    setLoading(false);
                    setError("Timeout waiting for software list");
                }
            }, 300); // Reduced from 2000ms

        } catch (err) {
            console.error("Failed to get software:", err);
            setError("Failed to send command. Make sure the agent is online.");
            setLoading(false);
        }
    };

    useEffect(() => {
        loadInstalledSoftware();
        return () => {
            if (pollingRef.current) clearInterval(pollingRef.current);
        };
    }, [deviceId]);

    const filteredSoftware = software.filter(s =>
        s.displayName?.toLowerCase().includes(searchQuery.toLowerCase()) ||
        s.publisher?.toLowerCase().includes(searchQuery.toLowerCase())
    );

    if (!deviceId) {
        return <div className="p-4 text-red-500">Device ID not found</div>;
    }

    return (
        <div className="space-y-6 p-4">
            {/* Header */}
            <div className="flex items-center gap-4">
                <button
                    onClick={() => navigate(`/devices/${deviceId}`)}
                    className="p-2 hover:bg-slate-700 rounded-lg transition-colors"
                >
                    <ArrowLeft className="w-5 h-5" />
                </button>
                <div className="flex items-center gap-2">
                    <div className="p-2 rounded-xl bg-fuchsia-500/20 shadow-inner border border-fuchsia-500/30">
                        <Package className="w-6 h-6 text-fuchsia-400" />
                    </div>
                    <h1 className="text-2xl font-bold text-white tracking-tight">Installed Software</h1>
                    {software.length > 0 && (
                        <span className="text-sm text-slate-400">({software.length} programs)</span>
                    )}
                </div>
            </div>

            {/* Toolbar */}
            <div className="flex flex-wrap items-center gap-4 glass-panel p-4 rounded-2xl border-white/5 shadow-lg">
                {/* Search */}
                <div className="flex-1 relative min-w-[200px]">
                    <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 w-4 h-4 text-slate-400" />
                    <input
                        type="text"
                        placeholder="Search installed software..."
                        value={searchQuery}
                        onChange={(e) => setSearchQuery(e.target.value)}
                        className="w-full glass-card border border-white/10 rounded-xl pl-10 pr-4 py-3 text-sm focus-ring placeholder-slate-500 text-white"
                    />
                </div>

                <button
                    onClick={loadInstalledSoftware}
                    disabled={loading}
                    className="flex items-center gap-2 px-6 py-3 glass-button rounded-xl text-sm transition-all hover-lift disabled:opacity-50 font-medium"
                >
                    <RefreshCcw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
                    {loading ? 'Loading...' : 'Refresh'}
                </button>
            </div>

            {/* Software List */}
            <div className="glass-card rounded-2xl border-white/5 overflow-hidden shadow-xl">
                {/* Header Row */}
                <div className="grid grid-cols-12 gap-4 px-4 py-4 bg-white/5 border-b border-white/5 text-xs text-slate-300 font-semibold uppercase tracking-widest">
                    <div className="col-span-5">Software Name</div>
                    <div className="col-span-2">Version</div>
                    <div className="col-span-5">Publisher</div>
                </div>

                {/* Loading State */}
                {loading && (
                    <div className="p-8 text-center text-slate-400">
                        <RefreshCcw className="w-6 h-6 animate-spin mx-auto mb-2" />
                        Fetching installed software from device...
                    </div>
                )}

                {/* Error State */}
                {error && !loading && (
                    <div className="p-8 text-center text-red-400">
                        <AlertCircle className="w-8 h-8 mx-auto mb-2" />
                        {error}
                    </div>
                )}

                {/* Empty State */}
                {!loading && !error && filteredSoftware.length === 0 && software.length === 0 && (
                    <div className="p-8 text-center text-slate-500">
                        <Package className="w-12 h-12 mx-auto mb-3 opacity-50" />
                        <p>No installed software found or still loading...</p>
                    </div>
                )}

                {/* No Search Results */}
                {!loading && !error && filteredSoftware.length === 0 && software.length > 0 && (
                    <div className="p-8 text-center text-slate-500">
                        No software matching "{searchQuery}"
                    </div>
                )}

                {/* Software Items */}
                <div className="max-h-[60vh] overflow-y-auto">
                    {filteredSoftware.map((item, index) => (
                        <div
                            key={index}
                            className="grid grid-cols-12 gap-4 px-4 py-3 hover:bg-slate-800/30 border-b border-slate-700/50"
                        >
                            <div className="col-span-5 flex items-center gap-2 truncate">
                                <Package className="w-4 h-4 text-fuchsia-400 shrink-0" />
                                <span className="truncate" title={item.displayName}>{item.displayName}</span>
                            </div>
                            <div className="col-span-2 text-slate-400 text-sm truncate">
                                {item.displayVersion || '-'}
                            </div>
                            <div className="col-span-5 text-slate-400 text-sm truncate" title={item.publisher}>
                                {item.publisher || '-'}
                            </div>
                        </div>
                    ))}
                </div>
            </div>
        </div>
    );
};

export default SoftwareDeploymentPage;
