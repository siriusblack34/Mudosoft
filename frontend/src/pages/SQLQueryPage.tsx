import React, { useEffect, useMemo, useState } from "react";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import * as Icons from "../components/icons/Icons";
import { Check, Terminal, Play, RotateCcw, Monitor, Server, XCircle, AlertCircle } from "lucide-react";

type FilterType = "ALL" | "PC" | "POS" | "GECICI";

const isRouter = (d: SqlDeviceWithStatus) => (d.deviceType ?? "").toLowerCase() === "router";
const isPc = (d: SqlDeviceWithStatus) => (d.deviceType ?? "").toLowerCase() === "pc";
const isPos = (d: SqlDeviceWithStatus) => (d.deviceType ?? "").toLowerCase().includes("kasa");
const isGecici = (d: SqlDeviceWithStatus) => (d.deviceType ?? "").toLowerCase() === "gecici";

const DeviceTypeBadge: React.FC<{ type: string }> = ({ type }) => {
    const t = type.toLowerCase();
    if (t === 'pc')
        return <span className="px-1.5 py-px rounded text-[10px] font-bold bg-sky-500/15 text-sky-400 border border-sky-500/25">PC</span>;
    if (t === 'gecici')
        return <span className="px-1.5 py-px rounded text-[10px] font-bold bg-orange-500/15 text-orange-400 border border-orange-500/25">GEÇİCİ</span>;
    if (t.includes('kasa'))
        return <span className="px-1.5 py-px rounded text-[10px] font-bold bg-amber-500/15 text-amber-400 border border-amber-500/25">{type}</span>;
    return <span className="px-1.5 py-px rounded text-[10px] font-bold bg-ms-border text-ms-text-muted">{type}</span>;
};

interface QueryResult {
    deviceId: string;
    storeName: string;
    status: "success" | "error" | "running";
    data?: any;
    error?: string;
}

const SQLQueryPage: React.FC = () => {
    const [devices, setDevices] = useState<SqlDeviceWithStatus[]>([]);
    // Multi-select state
    const [selectedDeviceIds, setSelectedDeviceIds] = useState<Set<string>>(new Set());

    const [filterType, setFilterType] = useState<FilterType>("ALL");
    const [search, setSearch] = useState("");

    const [sqlQuery, setSqlQuery] = useState("SELECT * FROM STORE_USER");

    // Bulk Results
    const [results, setResults] = useState<QueryResult[]>([]);

    const [isLoading, setIsLoading] = useState(true);
    const [isExecuting, setIsExecuting] = useState(false);
    const [pageError, setPageError] = useState<string | null>(null);

    // =====================================================
    // LOAD
    // =====================================================
    useEffect(() => {
        let isMounted = true;

        const load = async (silent = false) => {
            try {
                if (!silent) setIsLoading(true);
                // Don't reset error on silent refresh to avoid flickering if it persists, 
                // but here we want to retry.
                if (!silent) setPageError(null);

                const data = await apiClient.getSqlDevicesWithStatus({
                    timeoutMs: 500,
                    maxConcurrency: 40,
                });

                if (isMounted) {
                    const runnableDevices = (data ?? []).filter(d => !isRouter(d));
                    setDevices(runnableDevices);
                    setSelectedDeviceIds(prev => {
                        const runnableIds = new Set(runnableDevices.map(d => d.deviceId));
                        return new Set([...prev].filter(id => runnableIds.has(id)));
                    });
                    // If previously there was an error and now it succeeds, clear it
                    setPageError(null);
                }
            } catch {
                if (isMounted) setPageError("Cihaz listesi yüklenemedi.");
            } finally {
                if (isMounted && !silent) setIsLoading(false);
            }
        };

        load(); // Initial load
        const intervalId = setInterval(() => load(true), 30000); // Background refresh every 30s

        return () => {
            isMounted = false;
            clearInterval(intervalId);
        };
    }, []);

    // =====================================================
    // FILTERING
    // =====================================================
    const matchesSegment = (d: SqlDeviceWithStatus) => {
        if (filterType === "PC") return isPc(d);
        if (filterType === "POS") return isPos(d);
        if (filterType === "GECICI") return isGecici(d);
        return true;
    };

    const matchesSearch = (d: SqlDeviceWithStatus) => {
        const s = search.trim().toLowerCase();
        if (!s) return true;
        return (
            d.storeName.toLowerCase().includes(s) ||
            String(d.storeCode).includes(s) ||
            d.calculatedIpAddress.includes(s) ||
            d.deviceType.toLowerCase().includes(s)
        );
    };

    const displayedDevices = useMemo(() => {
        return devices
            .filter(matchesSegment)
            .filter(matchesSearch)
            .sort((a, b) => {
                if (a.isOnline !== b.isOnline) return a.isOnline ? -1 : 1;
                return a.storeCode - b.storeCode;
            });
    }, [devices, filterType, search]);

    // =====================================================
    // SELECTION HELPERS
    // =====================================================
    const toggleSelection = (id: string) => {
        const next = new Set(selectedDeviceIds);
        if (next.has(id)) next.delete(id);
        else next.add(id);
        setSelectedDeviceIds(next);
    };

    const selectAllDisplayed = () => {
        const next = new Set(selectedDeviceIds);
        displayedDevices.forEach(d => next.add(d.deviceId));
        setSelectedDeviceIds(next);
    };

    const deselectAll = () => {
        setSelectedDeviceIds(new Set());
    };

    // =====================================================
    // BULK EXECUTION (concurrency-limited)
    // =====================================================
    const MAX_CONCURRENCY = 10;

    const handleExecute = async () => {
        if (selectedDeviceIds.size === 0) return;
        setIsExecuting(true);
        setResults([]); // Clear previous results

        const targets = devices.filter(d => selectedDeviceIds.has(d.deviceId));

        // Initialize results as "pending"
        const initialResults: QueryResult[] = targets.map(d => ({
            deviceId: d.deviceId,
            storeName: `[${d.storeCode}] ${d.storeName}`,
            status: "running"
        }));
        setResults(initialResults);

        // Concurrency-limited execution to avoid browser connection saturation
        let idx = 0;
        const runNext = async (): Promise<void> => {
            while (idx < targets.length) {
                const current = idx++;
                const device = targets[current];
                try {
                    const res = await apiClient.runSqlQuery(device.deviceId, sqlQuery);
                    setResults(prev => prev.map(r =>
                        r.deviceId === device.deviceId
                            ? { ...r, status: "success", data: res }
                            : r
                    ));
                } catch (err: any) {
                    setResults(prev => prev.map(r =>
                        r.deviceId === device.deviceId
                            ? { ...r, status: "error", error: err.message || "Query failed" }
                            : r
                    ));
                }
            }
        };

        const workers = Array.from({ length: Math.min(MAX_CONCURRENCY, targets.length) }, () => runNext());
        await Promise.all(workers);
        setIsExecuting(false);
    };

    // Select only devices that returned errors
    const selectFailed = () => {
        const failedIds = new Set(results.filter(r => r.status === "error").map(r => r.deviceId));
        setSelectedDeviceIds(failedIds);
    };

    if (isLoading) {
        return (
            <div className="flex h-[80vh] items-center justify-center">
                <Spinner size="lg" />
            </div>
        );
    }

    // Counts
    const pcDevices = devices.filter(isPc);
    const posDevices = devices.filter(isPos);

    const pcOnline = pcDevices.filter(d => d.isOnline).length;
    const pcOffline = pcDevices.length - pcOnline;

    const posOnline = posDevices.filter(d => d.isOnline).length;
    const posOffline = posDevices.length - posOnline;

    const geciciDevices = devices.filter(isGecici);
    const geciciOnline = geciciDevices.filter(d => d.isOnline).length;
    const geciciOffline = geciciDevices.length - geciciOnline;

    const selectedCount = selectedDeviceIds.size;

    return (
        <div className="p-6 h-[calc(100vh-2rem)] flex flex-col gap-6">
            <div className="flex items-center justify-between">
                <h1 className="text-2xl font-bold text-white flex items-center gap-3">
                    <div className="p-2 rounded-xl bg-emerald-500/20 shadow-inner border border-emerald-500/30">
                        <Terminal className="w-6 h-6 text-emerald-400" />
                    </div>
                    SQL Command Center
                </h1>

                <div className="flex items-center gap-6 glass-panel px-5 py-3 rounded-2xl border-white/5 shadow-lg">
                    {/* PC Stats */}
                    <div className="flex flex-col items-end">
                        <span className="text-xs font-bold text-slate-500 uppercase tracking-wider mb-0.5">PC Devices</span>
                        <div className="flex items-center gap-3 text-sm">
                            <span className="flex items-center gap-1.5 text-emerald-400">
                                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
                                {pcOnline}
                            </span>
                            <span className="flex items-center gap-1.5 text-slate-500">
                                <span className="w-1.5 h-1.5 rounded-full bg-slate-600"></span>
                                {pcOffline}
                            </span>
                        </div>
                    </div>

                    <div className="w-px h-8 bg-slate-800"></div>

                    {/* POS Stats */}
                    <div className="flex flex-col items-end">
                        <span className="text-xs font-bold text-slate-500 uppercase tracking-wider mb-0.5">POS (Kasa)</span>
                        <div className="flex items-center gap-3 text-sm">
                            <span className="flex items-center gap-1.5 text-emerald-400">
                                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
                                {posOnline}
                            </span>
                            <span className="flex items-center gap-1.5 text-slate-500">
                                <span className="w-1.5 h-1.5 rounded-full bg-slate-600"></span>
                                {posOffline}
                            </span>
                        </div>
                    </div>

                    <div className="w-px h-8 bg-slate-800"></div>

                    {/* GECICI Stats */}
                    <div className="flex flex-col items-end">
                        <span className="text-xs font-bold text-slate-500 uppercase tracking-wider mb-0.5">Geçici PC</span>
                        <div className="flex items-center gap-3 text-sm">
                            <span className="flex items-center gap-1.5 text-emerald-400">
                                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
                                {geciciOnline}
                            </span>
                            <span className="flex items-center gap-1.5 text-slate-500">
                                <span className="w-1.5 h-1.5 rounded-full bg-slate-600"></span>
                                {geciciOffline}
                            </span>
                        </div>
                    </div>
                </div>
            </div>

            {pageError && (
                <div className="bg-rose-900/40 border border-rose-600 text-rose-300 p-4 rounded-xl flex items-center gap-3">
                    <AlertCircle className="w-5 h-5" />
                    {pageError}
                </div>
            )}

            <div className="flex flex-1 gap-6 overflow-hidden">
                {/* LEFT PANEL: DEVICE LIST */}
                <div className="w-1/3 glass-panel rounded-2xl flex flex-col overflow-hidden border-white/5">
                    <div className="p-4 border-b border-white/5 bg-slate-900/40 space-y-3">
                        {/* Filters */}
                        <div className="flex gap-2">
                            {(["ALL", "PC", "POS", "GECICI"] as FilterType[]).map(t => (
                                <button
                                    key={t}
                                    onClick={() => setFilterType(t)}
                                    className={`px-3 py-1.5 text-xs font-medium rounded-xl transition-all duration-300 ${filterType === t
                                        ? "bg-indigo-500 text-white shadow-[0_0_10px_rgba(99,102,241,0.5)] border-none"
                                        : "glass-button hover:-translate-y-0.5 border-white/5"
                                        }`}
                                >
                                    {t === "GECICI" ? "GEÇİCİ" : t}
                                </button>
                            ))}
                        </div>
                        {/* Search */}
                        <div className="relative">
                            <input
                                className="w-full px-4 py-2.5 bg-slate-950/50 border border-white/10 rounded-xl text-sm text-white placeholder-slate-400 focus-ring transition-all"
                                placeholder="Search store, ip, type..."
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                            />
                        </div>
                        {/* Selection Controls */}
                        <div className="flex justify-between items-center text-xs text-slate-400">
                            <span>{selectedCount} selected</span>
                            <div className="flex gap-2">
                                <button onClick={selectAllDisplayed} className="hover:text-white">Select Visible</button>
                                {results.some(r => r.status === "error") && (
                                    <button onClick={selectFailed} className="hover:text-rose-400 text-rose-500 font-medium">
                                        Select Failed ({results.filter(r => r.status === "error").length})
                                    </button>
                                )}
                                <button onClick={deselectAll} className="hover:text-white">Clear</button>
                            </div>
                        </div>
                    </div>

                    <div className="flex-1 overflow-auto p-2 space-y-1">
                        {displayedDevices.map(d => {
                            const isSelected = selectedDeviceIds.has(d.deviceId);
                            return (
                                <div
                                    key={d.deviceId}
                                    onClick={() => toggleSelection(d.deviceId)}
                                    className={`flex items-center gap-3 p-3 rounded-xl cursor-pointer transition-all border ${isSelected
                                        ? "bg-indigo-500/20 border-indigo-400/50 shadow-[0_0_10px_rgba(99,102,241,0.1)]"
                                        : "hover:bg-white/5 border-transparent"
                                        }`}
                                >
                                    <div className={`w-4 h-4 rounded border flex items-center justify-center transition-all ${isSelected ? "bg-indigo-500 border-indigo-500" : "border-slate-500 bg-slate-900"
                                        }`}>
                                        {isSelected && <Check className="w-3 h-3 text-white" />}
                                    </div>

                                    <div className="flex-1 min-w-0">
                                        <div className="flex items-center justify-between mb-0.5">
                                            <span className={`font-medium text-sm truncate ${d.isOnline ? "text-slate-200" : "text-slate-500"
                                                }`}>
                                                [{d.storeCode}] {d.storeName}
                                            </span>
                                            {d.isOnline && <span className="w-2 h-2 rounded-full bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.4)]"></span>}
                                        </div>
                                        <div className="flex items-center gap-2 text-xs text-ms-text-muted">
                                            {d.deviceType.toLowerCase().includes('kasa') ? <Monitor className="w-3 h-3" /> : <Server className="w-3 h-3" />}
                                            <span className="font-mono">{d.calculatedIpAddress}</span>
                                            <DeviceTypeBadge type={d.deviceType} />
                                        </div>
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                </div>

                {/* RIGHT PANEL: EDITOR & RESULTS */}
                <div className="flex-1 flex flex-col gap-6 min-w-0">
                    {/* Editor */}
                    <div className="glass-panel border-white/5 rounded-2xl p-5 flex flex-col h-1/3 shrink-0">
                        <div className="flex justify-between items-center mb-3">
                            <label className="text-sm font-semibold text-slate-300 tracking-wide">SQL Command</label>
                            <span className="text-xs text-indigo-400 font-medium bg-indigo-500/10 px-2 py-1 rounded-md">Targeting {selectedCount} devices</span>
                        </div>
                        <textarea
                            className="flex-1 w-full bg-slate-950/80 font-mono text-sm text-emerald-400 p-4 rounded-xl border border-white/10 focus-ring shadow-inner resize-none"
                            value={sqlQuery}
                            onChange={e => setSqlQuery(e.target.value)}
                            spellCheck={false}
                        />
                        <div className="flex justify-end mt-4">
                            <button
                                onClick={handleExecute}
                                disabled={isExecuting || selectedCount === 0}
                                className="flex items-center gap-2 px-6 py-2.5 bg-gradient-to-r from-emerald-500 to-teal-500 hover:from-emerald-400 hover:to-teal-400 text-white rounded-xl hover:shadow-[0_0_20px_rgba(16,185,129,0.3)] disabled:opacity-50 disabled:cursor-not-allowed transition-all duration-300 hover:-translate-y-0.5"
                            >
                                {isExecuting ? <RotateCcw className="w-4 h-4 animate-spin" /> : <Play className="w-4 h-4 fill-current" />}
                                <span className="font-medium">Execute on {selectedCount} Devices</span>
                            </button>
                        </div>
                    </div>

                    {/* Results Console */}
                    <div className="flex-1 glass-panel border-white/5 rounded-2xl overflow-hidden flex flex-col min-h-0 bg-slate-900/80">
                        <div className="px-5 py-3 border-b border-white/5 flex items-center justify-between shrink-0 bg-white/5">
                            <span className="text-xs font-mono uppercase tracking-widest text-slate-400 font-semibold">Execution Output</span>
                            {results.length > 0 && (
                                <div className="flex items-center gap-3">
                                    <span className="text-xs text-slate-400">
                                        {results.filter(r => r.status === 'success').length} Success, {results.filter(r => r.status === 'error').length} Failed
                                    </span>
                                    {results.some(r => r.status === "error") && (
                                        <button
                                            onClick={selectFailed}
                                            className="text-xs px-2.5 py-1 rounded-lg bg-rose-500/15 text-rose-400 hover:bg-rose-500/25 border border-rose-500/25 transition-all font-medium"
                                        >
                                            Hatalıları Seç & Tekrar Çalıştır
                                        </button>
                                    )}
                                </div>
                            )}
                        </div>

                        <div className="flex-1 overflow-auto p-4 space-y-3 text-sm">
                            {results.length === 0 ? (
                                <div className="h-full flex flex-col items-center justify-center text-slate-600 opacity-50">
                                    <Terminal className="w-12 h-12 mb-3" />
                                    <p>Ready for output...</p>
                                </div>
                            ) : (
                                results.map((res, i) => (
                                    <div key={i} className="border-b border-slate-800/50 pb-3 last:border-0 last:pb-0">
                                        <div className="flex items-center gap-3 mb-2">
                                            {res.status === "running" && <div className="w-3 h-3 border-2 border-indigo-500 border-t-transparent rounded-full animate-spin"></div>}
                                            {res.status === "success" && <Check className="w-4 h-4 text-emerald-500" />}
                                            {res.status === "error" && <XCircle className="w-4 h-4 text-rose-500" />}

                                            <span className={`font-semibold ${res.status === "success" ? "text-emerald-400" :
                                                res.status === "error" ? "text-rose-400" : "text-indigo-400"
                                                }`}>
                                                {res.storeName}
                                            </span>
                                            <span className="text-slate-600 text-xs">{res.deviceId}</span>
                                        </div>

                                        {res.status === "success" && res.data && (
                                            <div className="mt-2 overflow-hidden">
                                                {Array.isArray(res.data) && res.data.length > 0 ? (
                                                    <div className="overflow-auto max-h-[400px] rounded-lg border border-slate-700 bg-slate-900 border-collapse">
                                                        <table className="w-full text-xs text-left border-collapse font-sans">
                                                            <thead className="bg-slate-800 text-slate-300 sticky top-0 z-10">
                                                                <tr>
                                                                    {Object.keys(res.data[0]).map((key) => (
                                                                        <th key={key} className="px-3 py-2 border-b border-slate-700 font-medium whitespace-nowrap">
                                                                            {key}
                                                                        </th>
                                                                    ))}
                                                                </tr>
                                                            </thead>
                                                            <tbody className="divide-y divide-slate-800 text-slate-300">
                                                                {res.data.map((row: any, idx: number) => (
                                                                    <tr key={idx} className="hover:bg-slate-800/50 transition-colors">
                                                                        {Object.values(row).map((val: any, vIdx: number) => (
                                                                            <td key={vIdx} className="px-3 py-2 whitespace-nowrap max-w-[200px] truncate" title={String(val)}>
                                                                                {val === null ? <span className="text-slate-600 italic">null</span> : String(val)}
                                                                            </td>
                                                                        ))}
                                                                    </tr>
                                                                ))}
                                                            </tbody>
                                                        </table>
                                                    </div>
                                                ) : (
                                                    <div className="bg-slate-900/50 p-2 rounded text-slate-500 text-xs italic border border-slate-800">
                                                        {Array.isArray(res.data)
                                                            ? "Query executed successfully. Result is empty (0 rows)."
                                                            : "Operation completed successfully."}
                                                    </div>
                                                )}
                                            </div>
                                        )}

                                        {res.status === "error" && (
                                            <div className="ml-7 text-rose-400/80 text-xs">
                                                Error: {res.error}
                                            </div>
                                        )}
                                    </div>
                                ))
                            )}
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default SQLQueryPage;
