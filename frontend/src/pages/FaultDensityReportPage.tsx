import React, { useEffect, useMemo, useState, useCallback } from "react";
import {
    Activity, AlertTriangle, ChevronDown, ChevronUp, Download, Filter, RefreshCw, Search, ShieldAlert, TrendingUp, WifiOff,
} from "lucide-react";
import { apiClient, FaultDensityDevice, FaultDensityReport, FaultDensityStore } from "../lib/apiClient";

function formatDuration(minutes: number) {
    if (minutes <= 0) return "-";
    if (minutes < 60) return `${minutes} dk`;
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    if (hours < 24) return `${hours} sa ${mins} dk`;
    const days = Math.floor(hours / 24);
    return `${days} g ${hours % 24} sa`;
}

type SortKey = "faultScore" | "storeCode" | "storeName" | "currentOfflineDevices" | "criticalDevices" | "incidentCount" | "totalOfflineMinutes";
type SortDir = "asc" | "desc";

function exportCsv(rows: FaultDensityStore[]) {
    const header = "Skor;Kod;Mağaza;Cihaz;Sorunlu;Offline;Kritik;Uyarı;Kesinti;Toplam Süre (dk)";
    const lines = rows.map(r =>
        [r.faultScore, r.storeCode, r.storeName, r.deviceCount, r.devicesWithIssues, r.currentOfflineDevices, r.criticalDevices, r.warningDevices, r.incidentCount, r.totalOfflineMinutes].join(";")
    );
    const blob = new Blob(["\uFEFF" + header + "\n" + lines.join("\n")], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a"); a.href = url; a.download = `ariza-yogunlugu-${new Date().toISOString().slice(0, 10)}.csv`; a.click();
    URL.revokeObjectURL(url);
}

const FaultDensityReportPage: React.FC = () => {
    const [report, setReport] = useState<FaultDensityReport | null>(null);
    const [loading, setLoading] = useState(true);
    const [days, setDays] = useState(30);
    const [search, setSearch] = useState("");
    const [showFilters, setShowFilters] = useState(false);
    const [minScore, setMinScore] = useState(0);
    const [filterHasOffline, setFilterHasOffline] = useState(false);
    const [sortKey, setSortKey] = useState<SortKey>("faultScore");
    const [sortDir, setSortDir] = useState<SortDir>("desc");
    const [selectedStore, setSelectedStore] = useState<number | null>(null);

    const load = useCallback(async () => {
        setLoading(true);
        try { setReport(await apiClient.getFaultDensityReport(days)); }
        catch (e) { console.error(e); }
        finally { setLoading(false); }
    }, [days]);

    useEffect(() => { void load(); }, [load]);

    const activeFilterCount = (minScore > 0 ? 1 : 0) + (filterHasOffline ? 1 : 0);

    const filteredStores = useMemo(() => {
        if (!report) return [];
        const term = search.trim().toLowerCase();
        return report.stores.filter(row => {
            if (term && !row.storeName.toLowerCase().includes(term) && !String(row.storeCode).includes(term)) return false;
            if (minScore > 0 && row.faultScore < minScore) return false;
            if (filterHasOffline && row.currentOfflineDevices === 0) return false;
            return true;
        });
    }, [report, search, minScore, filterHasOffline]);

    const sortedStores = useMemo(() => {
        return [...filteredStores].sort((a, b) => {
            const av = (a as any)[sortKey]; const bv = (b as any)[sortKey];
            const aVal = typeof av === "string" ? av.toLowerCase() : av;
            const bVal = typeof bv === "string" ? bv.toLowerCase() : bv;
            const c = aVal < bVal ? -1 : aVal > bVal ? 1 : 0;
            return sortDir === "asc" ? c : -c;
        });
    }, [filteredStores, sortKey, sortDir]);

    const filteredDevices = useMemo(() => {
        if (!report) return [];
        if (selectedStore !== null) return report.devices.filter(d => d.storeCode === selectedStore);
        const allowed = new Set(filteredStores.map(r => r.storeCode));
        return report.devices.filter(d => allowed.has(d.storeCode));
    }, [report, filteredStores, selectedStore]);

    const toggleSort = (key: SortKey) => {
        if (sortKey === key) setSortDir(d => d === "asc" ? "desc" : "asc");
        else { setSortKey(key); setSortDir("desc"); }
    };

    const totalCritical = filteredStores.reduce((s, x) => s + x.criticalDevices, 0);
    const totalOffline = filteredStores.reduce((s, x) => s + x.currentOfflineDevices, 0);
    const maxScore = filteredStores.length ? Math.max(...filteredStores.map(s => s.faultScore)) : 0;

    return (
        <div className="mx-auto max-w-[1600px] space-y-5 p-6">
            {/* Header */}
            <div className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end">
                <div>
                    <h1 className="flex items-center gap-3 text-2xl font-bold text-ms-text">
                        <TrendingUp className="h-6 w-6 text-amber-400" />
                        Arıza Yoğunluğu Raporu
                    </h1>
                    <p className="mt-1 text-sm text-ms-text-muted">Mağaza ve cihaz bazında sorun yoğunluğu, kritik sağlık ve offline baskısı.</p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                    <div className="flex rounded-lg border border-ms-border bg-ms-panel p-0.5">
                        {[7, 14, 30, 60, 90].map(v => (
                            <button key={v} onClick={() => setDays(v)}
                                className={`rounded-md px-3 py-1.5 text-xs font-semibold transition-all ${days === v ? "bg-violet-600/20 text-violet-300" : "text-ms-text-muted hover:text-ms-text"}`}>
                                {v}g
                            </button>
                        ))}
                    </div>
                    <div className="relative">
                        <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ms-text-muted" />
                        <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Mağaza ara…" className="w-48 !pl-9" />
                    </div>
                    <button onClick={() => setShowFilters(!showFilters)} className={`btn-secondary !px-3 relative ${showFilters ? "ring-2 ring-violet-500/50" : ""}`}>
                        <Filter className="h-4 w-4" /> Filtre
                        {activeFilterCount > 0 && <span className="absolute -top-1.5 -right-1.5 h-4 w-4 rounded-full bg-violet-500 text-[10px] font-bold text-white flex items-center justify-center">{activeFilterCount}</span>}
                    </button>
                    <button onClick={() => exportCsv(sortedStores)} className="btn-secondary !px-3"><Download className="h-4 w-4" /> CSV</button>
                    <button onClick={load} disabled={loading} className="btn-primary !px-3">
                        <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} /> Yenile
                    </button>
                </div>
            </div>

            {/* Filters */}
            {showFilters && (
                <div className="card animate-fade-in">
                    <div className="flex items-center justify-between mb-3">
                        <span className="text-xs font-semibold uppercase tracking-wider text-ms-text-muted">Filtreler</span>
                        {activeFilterCount > 0 && <button onClick={() => { setMinScore(0); setFilterHasOffline(false); }} className="text-[11px] text-violet-400">Temizle</button>}
                    </div>
                    <div className="flex flex-wrap gap-6">
                        <div>
                            <div className="text-[11px] font-medium text-ms-text-muted mb-1.5">Min. Skor</div>
                            <div className="flex gap-1.5">
                                {[0, 5, 10, 20].map(v => (
                                    <button key={v} onClick={() => setMinScore(v)}
                                        className={`px-2.5 py-1 rounded-lg text-xs font-medium border transition-colors ${minScore === v ? "bg-violet-500/20 text-violet-300 border-violet-500/40" : "text-ms-text-muted border-transparent hover:border-ms-border"}`}>
                                        {v === 0 ? "Tümü" : `${v}+`}
                                    </button>
                                ))}
                            </div>
                        </div>
                        <div>
                            <div className="text-[11px] font-medium text-ms-text-muted mb-1.5">Bağlantı</div>
                            <button onClick={() => setFilterHasOffline(!filterHasOffline)}
                                className={`px-2.5 py-1 rounded-lg text-xs font-medium border transition-colors ${filterHasOffline ? "bg-rose-500/20 text-rose-300 border-rose-500/40" : "text-ms-text-muted border-transparent hover:border-ms-border"}`}>
                                Offline cihazı olan
                            </button>
                        </div>
                    </div>
                </div>
            )}

            {/* Stats */}
            <div className="grid gap-3 grid-cols-2 md:grid-cols-4">
                <div className="card !p-3 border-amber-500/20">
                    <div className="flex items-center gap-2 mb-1"><AlertTriangle className="h-4 w-4 text-amber-400" /><span className="text-[10px] font-semibold uppercase tracking-wider text-ms-text-muted">Sorunlu Mağaza</span></div>
                    <div className="text-xl font-bold text-ms-text">{filteredStores.filter(x => x.faultScore > 0).length}</div>
                </div>
                <div className="card !p-3 border-rose-500/20">
                    <div className="flex items-center gap-2 mb-1"><ShieldAlert className="h-4 w-4 text-rose-400" /><span className="text-[10px] font-semibold uppercase tracking-wider text-ms-text-muted">Kritik Cihaz</span></div>
                    <div className="text-xl font-bold text-ms-text">{totalCritical}</div>
                </div>
                <div className="card !p-3 border-sky-500/20">
                    <div className="flex items-center gap-2 mb-1"><WifiOff className="h-4 w-4 text-sky-400" /><span className="text-[10px] font-semibold uppercase tracking-wider text-ms-text-muted">Offline Cihaz</span></div>
                    <div className="text-xl font-bold text-ms-text">{totalOffline}</div>
                </div>
                <div className="card !p-3 border-violet-500/20">
                    <div className="flex items-center gap-2 mb-1"><TrendingUp className="h-4 w-4 text-violet-400" /><span className="text-[10px] font-semibold uppercase tracking-wider text-ms-text-muted">Maks. Skor</span></div>
                    <div className="text-xl font-bold text-ms-text">{maxScore}</div>
                </div>
            </div>

            {/* Dual-panel */}
            <div className="grid gap-5 xl:grid-cols-[1.2fr_0.8fr]">
                {/* Stores table */}
                <div className="card !p-0 overflow-hidden">
                    <div className="border-b border-ms-border px-4 py-3 flex items-center justify-between">
                        <h2 className="text-sm font-semibold text-ms-text">Mağaza Skorları</h2>
                        <span className="text-[11px] text-ms-text-muted">{sortedStores.length} mağaza</span>
                    </div>
                    <div className="overflow-x-auto">
                        <table className="w-full text-left text-sm">
                            <thead>
                                <tr className="border-b border-ms-border">
                                    {([["faultScore", "Skor", "w-16"], ["storeCode", "Kod", "w-14"], ["storeName", "Mağaza", ""], ["currentOfflineDevices", "Offline", "w-16"], ["criticalDevices", "Kritik", "w-16"], ["incidentCount", "Kesinti", "w-16"], ["totalOfflineMinutes", "Toplam Süre", "w-24"]] as const).map(([key, label, w]) => (
                                        <th key={key} onClick={() => toggleSort(key)}
                                            className={`px-4 py-3 text-[11px] font-semibold uppercase tracking-wider text-ms-text-muted cursor-pointer hover:text-ms-text select-none ${w}`}>
                                            <div className="flex items-center gap-1">
                                                {label}
                                                {sortKey === key && (sortDir === "asc" ? <ChevronUp className="w-3 h-3 text-violet-400" /> : <ChevronDown className="w-3 h-3 text-violet-400" />)}
                                            </div>
                                        </th>
                                    ))}
                                </tr>
                            </thead>
                            <tbody>
                                {sortedStores.map(row => (
                                    <tr key={row.storeCode}
                                        onClick={() => setSelectedStore(selectedStore === row.storeCode ? null : row.storeCode)}
                                        className={`border-t border-ms-border text-ms-text cursor-pointer transition-colors ${selectedStore === row.storeCode ? "bg-violet-600/10" : "hover:bg-ms-hover-bg"}`}>
                                        <td className="px-4 py-2.5">
                                            <ScoreBadge score={row.faultScore} />
                                        </td>
                                        <td className="px-4 py-2.5 font-mono text-xs text-ms-text-muted">{row.storeCode}</td>
                                        <td className="px-4 py-2.5">
                                            <div className="font-medium">{row.storeName}</div>
                                            <div className="text-[11px] text-ms-text-muted">{row.deviceCount} cihaz, {row.devicesWithIssues} sorunlu</div>
                                        </td>
                                        <td className="px-4 py-2.5 font-semibold text-rose-400">{row.currentOfflineDevices || <span className="text-ms-text-muted">0</span>}</td>
                                        <td className="px-4 py-2.5 font-semibold text-rose-400">{row.criticalDevices || <span className="text-ms-text-muted">0</span>}</td>
                                        <td className="px-4 py-2.5">{row.incidentCount}</td>
                                        <td className="px-4 py-2.5 text-ms-text-muted">{formatDuration(row.totalOfflineMinutes)}</td>
                                    </tr>
                                ))}
                                {!loading && sortedStores.length === 0 && (
                                    <tr><td colSpan={7} className="px-4 py-12 text-center text-ms-text-muted">Kayıt bulunamadı.</td></tr>
                                )}
                            </tbody>
                        </table>
                    </div>
                </div>

                {/* Devices */}
                <div className="card !p-0 overflow-hidden">
                    <div className="border-b border-ms-border px-4 py-3 flex items-center justify-between">
                        <h2 className="text-sm font-semibold text-ms-text">
                            Sorunlu Cihazlar
                            {selectedStore !== null && (
                                <button onClick={() => setSelectedStore(null)} className="ml-2 text-[11px] text-violet-400 hover:text-violet-300">
                                    Filtreyi kaldır
                                </button>
                            )}
                        </h2>
                        <span className="text-[11px] text-ms-text-muted">{filteredDevices.length} cihaz</span>
                    </div>
                    <div className="max-h-[720px] overflow-auto px-3 py-3 space-y-2">
                        {filteredDevices.map(device => (
                            <div key={device.deviceId} className="rounded-xl border border-ms-border bg-ms-panel p-3">
                                <div className="flex items-start justify-between gap-2">
                                    <div>
                                        <div className="text-sm font-semibold text-ms-text">{device.hostname}</div>
                                        <div className="text-[11px] text-ms-text-muted">{device.storeCode} — {device.storeName} / {device.type}</div>
                                    </div>
                                    <ScoreBadge score={device.issueScore} />
                                </div>
                                <div className="mt-2 flex flex-wrap gap-1">
                                    {device.issueReasons.map(reason => (
                                        <span key={reason} className="rounded-md border border-ms-border bg-ms-bg px-1.5 py-0.5 text-[10px] text-ms-text-muted">{reason}</span>
                                    ))}
                                </div>
                                <div className="mt-2 grid grid-cols-3 gap-2 text-[11px] text-ms-text-muted">
                                    <div>CPU %{device.cpuUsagePercent}</div>
                                    <div>RAM %{device.ramUsagePercent}</div>
                                    <div>Disk %{device.diskUsagePercent}</div>
                                    <div className={device.online ? "text-emerald-400" : "text-rose-400"}>{device.online ? "Online" : "Offline"}</div>
                                    <div>{device.healthStatus || "-"}</div>
                                    <div>{device.lastSeen ? new Date(device.lastSeen).toLocaleString("tr-TR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" }) : "-"}</div>
                                </div>
                            </div>
                        ))}
                        {!loading && filteredDevices.length === 0 && (
                            <div className="rounded-xl border border-ms-border bg-ms-panel px-4 py-8 text-center text-sm text-ms-text-muted">Sorunlu cihaz bulunamadı.</div>
                        )}
                    </div>
                </div>
            </div>
        </div>
    );
};

const ScoreBadge: React.FC<{ score: number }> = ({ score }) => {
    const cls = score >= 20 ? "bg-rose-500/15 text-rose-400 border-rose-500/30"
        : score >= 10 ? "bg-amber-500/15 text-amber-400 border-amber-500/30"
        : score > 0 ? "bg-sky-500/15 text-sky-400 border-sky-500/30"
        : "bg-ms-border text-ms-text-muted border-transparent";
    return <span className={`inline-flex items-center justify-center min-w-[28px] px-1.5 py-0.5 rounded-md text-[11px] font-bold border ${cls}`}>{score}</span>;
};

export default FaultDensityReportPage;
