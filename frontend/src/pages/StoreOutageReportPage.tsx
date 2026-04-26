import React, { useEffect, useMemo, useState, useCallback } from "react";
import {
    AlertTriangle, ChevronDown, ChevronUp, Clock3, Download, Filter, RefreshCw, Search, Store, WifiOff, X,
} from "lucide-react";
import { apiClient, StoreOutageIncident, StoreOutageReport, StoreOutageSummary } from "../lib/apiClient";

function formatDuration(minutes: number | null) {
    if (minutes === null || minutes <= 0) return "-";
    if (minutes < 60) return `${minutes} dk`;
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    if (hours < 24) return `${hours} sa ${mins} dk`;
    const days = Math.floor(hours / 24);
    return `${days} g ${hours % 24} sa`;
}

function formatDate(value: string | null) {
    if (!value) return "-";
    return new Date(value).toLocaleString("tr-TR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

type SortKey = "storeCode" | "storeName" | "incidentCount" | "totalOfflineMinutes" | "averageOfflineMinutes" | "lastOfflineAt";
type SortDir = "asc" | "desc";

function cmp(a: any, b: any, dir: SortDir) { const c = a < b ? -1 : a > b ? 1 : 0; return dir === "asc" ? c : -c; }

function exportCsv(rows: StoreOutageSummary[]) {
    const header = "Kod;Mağaza;Kesinti Sayısı;Toplam Süre (dk);Ortalama (dk);Son Kesinti;Durum";
    const lines = rows.map(r =>
        [r.storeCode, r.storeName, r.incidentCount, r.totalOfflineMinutes, Math.round(r.averageOfflineMinutes), formatDate(r.lastOfflineAt), r.isCurrentlyOffline ? "Offline" : "Online"].join(";")
    );
    const blob = new Blob(["\uFEFF" + header + "\n" + lines.join("\n")], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a"); a.href = url; a.download = `magaza-kesinti-${new Date().toISOString().slice(0, 10)}.csv`; a.click();
    URL.revokeObjectURL(url);
}

const StoreOutageReportPage: React.FC = () => {
    const [report, setReport] = useState<StoreOutageReport | null>(null);
    const [loading, setLoading] = useState(true);
    const [days, setDays] = useState(30);
    const [search, setSearch] = useState("");
    const [showFilters, setShowFilters] = useState(false);
    const [filterStatus, setFilterStatus] = useState<"all" | "offline" | "recovered">("all");
    const [minIncidents, setMinIncidents] = useState(0);
    const [sortKey, setSortKey] = useState<SortKey>("totalOfflineMinutes");
    const [sortDir, setSortDir] = useState<SortDir>("desc");
    const [selectedStore, setSelectedStore] = useState<number | null>(null);

    const load = useCallback(async () => {
        setLoading(true);
        try { setReport(await apiClient.getStoreOutageReport(days)); }
        catch (e) { console.error(e); }
        finally { setLoading(false); }
    }, [days]);

    useEffect(() => { void load(); }, [load]);

    const activeFilterCount = (filterStatus !== "all" ? 1 : 0) + (minIncidents > 0 ? 1 : 0);

    const filteredSummary = useMemo(() => {
        if (!report) return [];
        const term = search.trim().toLowerCase();
        return report.summary.filter(row => {
            if (term && !row.storeName.toLowerCase().includes(term) && !String(row.storeCode).includes(term)) return false;
            if (filterStatus === "offline" && !row.isCurrentlyOffline) return false;
            if (filterStatus === "recovered" && row.isCurrentlyOffline) return false;
            if (minIncidents > 0 && row.incidentCount < minIncidents) return false;
            return true;
        });
    }, [report, search, filterStatus, minIncidents]);

    const sortedSummary = useMemo(() => {
        return [...filteredSummary].sort((a, b) => {
            switch (sortKey) {
                case "storeCode": return cmp(a.storeCode, b.storeCode, sortDir);
                case "storeName": return cmp(a.storeName.toLowerCase(), b.storeName.toLowerCase(), sortDir);
                case "incidentCount": return cmp(a.incidentCount, b.incidentCount, sortDir);
                case "totalOfflineMinutes": return cmp(a.totalOfflineMinutes, b.totalOfflineMinutes, sortDir);
                case "averageOfflineMinutes": return cmp(a.averageOfflineMinutes, b.averageOfflineMinutes, sortDir);
                case "lastOfflineAt": return cmp(a.lastOfflineAt || "", b.lastOfflineAt || "", sortDir);
                default: return 0;
            }
        });
    }, [filteredSummary, sortKey, sortDir]);

    const filteredIncidents = useMemo(() => {
        if (!report) return [];
        if (selectedStore !== null) return report.incidents.filter(i => i.storeCode === selectedStore);
        const allowed = new Set(filteredSummary.map(r => r.storeCode));
        return report.incidents.filter(i => allowed.has(i.storeCode));
    }, [report, filteredSummary, selectedStore]);

    const toggleSort = (key: SortKey) => {
        if (sortKey === key) setSortDir(d => d === "asc" ? "desc" : "asc");
        else { setSortKey(key); setSortDir("desc"); }
    };

    const totalMinutes = filteredSummary.reduce((s, r) => s + r.totalOfflineMinutes, 0);

    return (
        <div className="mx-auto max-w-[1600px] space-y-5 p-6">
            {/* Header */}
            <div className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end">
                <div>
                    <h1 className="flex items-center gap-3 text-2xl font-bold text-ms-text">
                        <Store className="h-6 w-6 text-rose-400" />
                        Mağaza Kesintisi Raporu
                    </h1>
                    <p className="mt-1 text-sm text-ms-text-muted">
                        Mağaza bazlı kesinti sayısı, toplam süre ve olay detayları.
                    </p>
                </div>

                <div className="flex flex-wrap items-center gap-2">
                    {/* Period selector */}
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

                    <button onClick={() => setShowFilters(!showFilters)} className={`btn-secondary !px-3 relative ${showFilters ? 'ring-2 ring-violet-500/50' : ''}`}>
                        <Filter className="h-4 w-4" /> Filtre
                        {activeFilterCount > 0 && <span className="absolute -top-1.5 -right-1.5 h-4 w-4 rounded-full bg-violet-500 text-[10px] font-bold text-white flex items-center justify-center">{activeFilterCount}</span>}
                    </button>

                    <button onClick={() => exportCsv(sortedSummary)} className="btn-secondary !px-3"><Download className="h-4 w-4" /> CSV</button>
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
                        {activeFilterCount > 0 && <button onClick={() => { setFilterStatus("all"); setMinIncidents(0); }} className="text-[11px] text-violet-400">Temizle</button>}
                    </div>
                    <div className="flex flex-wrap gap-6">
                        <div>
                            <div className="text-[11px] font-medium text-ms-text-muted mb-1.5">Durum</div>
                            <div className="flex gap-1.5">
                                {([["all", "Tümü"], ["offline", "Hâlâ Offline"], ["recovered", "Toparlandı"]] as const).map(([v, l]) => (
                                    <button key={v} onClick={() => setFilterStatus(v)}
                                        className={`px-2.5 py-1 rounded-lg text-xs font-medium border transition-colors ${filterStatus === v ? "bg-violet-500/20 text-violet-300 border-violet-500/40" : "text-ms-text-muted border-transparent hover:border-ms-border"}`}>
                                        {l}
                                    </button>
                                ))}
                            </div>
                        </div>
                        <div>
                            <div className="text-[11px] font-medium text-ms-text-muted mb-1.5">Min. Kesinti Sayısı</div>
                            <input type="number" min={0} value={minIncidents} onChange={e => setMinIncidents(parseInt(e.target.value) || 0)} className="w-20 !py-1 text-xs" />
                        </div>
                    </div>
                </div>
            )}

            {/* Stats */}
            <div className="grid gap-3 grid-cols-2 md:grid-cols-4">
                <div className="card !p-3 border-rose-500/20">
                    <div className="flex items-center gap-2 mb-1"><AlertTriangle className="h-4 w-4 text-rose-400" /><span className="text-[10px] font-semibold uppercase tracking-wider text-ms-text-muted">Toplam Kesinti</span></div>
                    <div className="text-xl font-bold text-ms-text">{report?.totalIncidents ?? 0}</div>
                </div>
                <div className="card !p-3 border-amber-500/20">
                    <div className="flex items-center gap-2 mb-1"><WifiOff className="h-4 w-4 text-amber-400" /><span className="text-[10px] font-semibold uppercase tracking-wider text-ms-text-muted">Şu An Offline</span></div>
                    <div className="text-xl font-bold text-ms-text">{report?.currentlyOfflineStoreCount ?? 0}</div>
                </div>
                <div className="card !p-3 border-sky-500/20">
                    <div className="flex items-center gap-2 mb-1"><Clock3 className="h-4 w-4 text-sky-400" /><span className="text-[10px] font-semibold uppercase tracking-wider text-ms-text-muted">Toplam Süre</span></div>
                    <div className="text-xl font-bold text-ms-text">{formatDuration(totalMinutes)}</div>
                </div>
                <div className="card !p-3 border-violet-500/20">
                    <div className="flex items-center gap-2 mb-1"><Store className="h-4 w-4 text-violet-400" /><span className="text-[10px] font-semibold uppercase tracking-wider text-ms-text-muted">Etkilenen Mağaza</span></div>
                    <div className="text-xl font-bold text-ms-text">{filteredSummary.length}</div>
                </div>
            </div>

            {/* Dual-panel */}
            <div className="grid gap-5 xl:grid-cols-[1.3fr_1fr]">
                {/* Summary table */}
                <div className="card !p-0 overflow-hidden">
                    <div className="border-b border-ms-border px-4 py-3 flex items-center justify-between">
                        <h2 className="text-sm font-semibold text-ms-text">Mağaza Özeti</h2>
                        <span className="text-[11px] text-ms-text-muted">{sortedSummary.length} mağaza</span>
                    </div>
                    <div className="overflow-x-auto">
                        <table className="w-full text-left text-sm">
                            <thead>
                                <tr className="border-b border-ms-border">
                                    {([["storeCode", "Kod", "w-14"], ["storeName", "Mağaza", ""], ["incidentCount", "Kesinti", "w-20"], ["totalOfflineMinutes", "Toplam Süre", "w-28"], ["averageOfflineMinutes", "Ort.", "w-20"], ["lastOfflineAt", "Son Kesinti", ""]] as const).map(([key, label, w]) => (
                                        <th key={key} onClick={() => toggleSort(key)}
                                            className={`px-4 py-3 text-[11px] font-semibold uppercase tracking-wider text-ms-text-muted cursor-pointer hover:text-ms-text select-none ${w}`}>
                                            <div className="flex items-center gap-1">
                                                {label}
                                                {sortKey === key && (sortDir === "asc" ? <ChevronUp className="w-3 h-3 text-violet-400" /> : <ChevronDown className="w-3 h-3 text-violet-400" />)}
                                            </div>
                                        </th>
                                    ))}
                                    <th className="px-4 py-3 text-[11px] font-semibold uppercase tracking-wider text-ms-text-muted w-24">Durum</th>
                                </tr>
                            </thead>
                            <tbody>
                                {sortedSummary.map(row => (
                                    <tr key={row.storeCode}
                                        onClick={() => setSelectedStore(selectedStore === row.storeCode ? null : row.storeCode)}
                                        className={`border-t border-ms-border text-ms-text cursor-pointer transition-colors ${selectedStore === row.storeCode ? "bg-violet-600/10" : "hover:bg-ms-hover-bg"}`}>
                                        <td className="px-4 py-2.5 font-mono text-xs text-ms-text-muted">{row.storeCode}</td>
                                        <td className="px-4 py-2.5">
                                            <div className="font-medium">{row.storeName}</div>
                                        </td>
                                        <td className="px-4 py-2.5 font-semibold text-rose-400">{row.incidentCount}</td>
                                        <td className="px-4 py-2.5">{formatDuration(row.totalOfflineMinutes)}</td>
                                        <td className="px-4 py-2.5 text-ms-text-muted">{formatDuration(Math.round(row.averageOfflineMinutes))}</td>
                                        <td className="px-4 py-2.5 text-[12px] text-ms-text-muted">{formatDate(row.lastOfflineAt)}</td>
                                        <td className="px-4 py-2.5">
                                            {row.isCurrentlyOffline
                                                ? <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] font-semibold bg-rose-500/15 text-rose-400 border border-rose-500/30"><WifiOff className="w-3 h-3" /> Offline</span>
                                                : <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] font-semibold bg-emerald-500/15 text-emerald-400 border border-emerald-500/30">Toparlandı</span>
                                            }
                                        </td>
                                    </tr>
                                ))}
                                {!loading && sortedSummary.length === 0 && (
                                    <tr><td colSpan={7} className="px-4 py-12 text-center text-ms-text-muted">Kayıt bulunamadı.</td></tr>
                                )}
                            </tbody>
                        </table>
                    </div>
                </div>

                {/* Incidents */}
                <div className="card !p-0 overflow-hidden">
                    <div className="border-b border-ms-border px-4 py-3 flex items-center justify-between">
                        <h2 className="text-sm font-semibold text-ms-text">
                            Kesinti Olayları
                            {selectedStore !== null && (
                                <button onClick={() => setSelectedStore(null)} className="ml-2 text-[11px] text-violet-400 hover:text-violet-300">
                                    <X className="w-3 h-3 inline" /> Filtreyi kaldır
                                </button>
                            )}
                        </h2>
                        <span className="text-[11px] text-ms-text-muted">{filteredIncidents.length} olay</span>
                    </div>
                    <div className="max-h-[720px] overflow-auto px-3 py-3 space-y-2">
                        {filteredIncidents.map(incident => (
                            <div key={incident.id} className="rounded-xl border border-ms-border bg-ms-panel p-3">
                                <div className="flex items-start justify-between gap-3">
                                    <div>
                                        <div className="text-sm font-semibold text-ms-text">{incident.storeCode} — {incident.storeName}</div>
                                        <div className="mt-1 text-[11px] text-ms-text-muted">{incident.offlineKasaCount} kasa etkilendi</div>
                                    </div>
                                    <span className={`shrink-0 rounded-md px-2 py-1 text-[11px] font-semibold ${incident.isStillOffline ? "bg-rose-500/15 text-rose-400 border border-rose-500/30" : "bg-ms-border text-ms-text-muted"}`}>
                                        {incident.isStillOffline ? "Aktif" : formatDuration(incident.durationMinutes)}
                                    </span>
                                </div>
                                <div className="mt-2 grid grid-cols-2 gap-1 text-[11px] text-ms-text-muted">
                                    <div>Offline: {formatDate(incident.offlineAt)}</div>
                                    <div>Online: {formatDate(incident.onlineAt)}</div>
                                </div>
                            </div>
                        ))}
                        {!loading && filteredIncidents.length === 0 && (
                            <div className="rounded-xl border border-ms-border bg-ms-panel px-4 py-8 text-center text-sm text-ms-text-muted">Olay bulunamadı.</div>
                        )}
                    </div>
                </div>
            </div>
        </div>
    );
};

export default StoreOutageReportPage;
