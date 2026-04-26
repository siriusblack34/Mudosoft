import React, { useEffect, useMemo, useState, useCallback } from "react";
import {
    Cpu, Download, HardDrive, MemoryStick, RefreshCw, Search, Server, ChevronUp, ChevronDown,
    Filter, Wifi, WifiOff,
} from "lucide-react";
import { apiClient, HardwareInventoryReport, HardwareInventoryRow } from "../lib/apiClient";

// ── Helpers ──────────────────────────────────────────────────────────────

function formatRam(mb: number) { return `${Math.round(mb / 1024)} GB`; }
function formatDisk(gb: number | null | undefined) { return (!gb || gb <= 0) ? "-" : `${gb} GB`; }
function ramBucket(mb: number): string {
    const gb = mb / 1024;
    if (gb < 4) return "<4 GB";
    if (gb < 8) return "4-8 GB";
    if (gb < 16) return "8-16 GB";
    return "16+ GB";
}

type SortKey = "storeName" | "storeCode" | "cpuModel" | "totalRamMB" | "totalDiskGB" | "os";
type SortDir = "asc" | "desc";

function compareRows(a: HardwareInventoryRow, b: HardwareInventoryRow, key: SortKey, dir: SortDir): number {
    let av: any, bv: any;
    switch (key) {
        case "storeName": av = (a.storeName || "").toLowerCase(); bv = (b.storeName || "").toLowerCase(); break;
        case "storeCode": av = a.storeCode; bv = b.storeCode; break;
        case "cpuModel": av = (a.cpuModel || "").toLowerCase(); bv = (b.cpuModel || "").toLowerCase(); break;
        case "totalRamMB": av = a.totalRamMB; bv = b.totalRamMB; break;
        case "totalDiskGB": av = a.totalDiskGB; bv = b.totalDiskGB; break;
        case "os": av = (a.os || "").toLowerCase(); bv = (b.os || "").toLowerCase(); break;
        default: av = 0; bv = 0;
    }
    const cmp = av < bv ? -1 : av > bv ? 1 : 0;
    return dir === "asc" ? cmp : -cmp;
}

function exportCsv(rows: HardwareInventoryRow[]) {
    const header = "Mağaza Kodu;Mağaza;Hostname;CPU;RAM (GB);Disk (GB);OS;Online";
    const lines = rows.map(r =>
        [r.storeCode, r.storeName, r.hostname, r.cpuModel, Math.round(r.totalRamMB / 1024), r.totalDiskGB, r.os, r.online ? "Evet" : "Hayır"].join(";")
    );
    const bom = "\uFEFF";
    const blob = new Blob([bom + header + "\n" + lines.join("\n")], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `donanim-envanteri-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
}

// ── Page ─────────────────────────────────────────────────────────────────

const HardwareInventoryReportPage: React.FC = () => {
    const [report, setReport] = useState<HardwareInventoryReport | null>(null);
    const [loading, setLoading] = useState(true);
    const [search, setSearch] = useState("");
    const [showFilters, setShowFilters] = useState(false);

    // Filters
    const [filterOnline, setFilterOnline] = useState<"all" | "online" | "offline">("all");
    const [filterRam, setFilterRam] = useState<Set<string>>(new Set());

    // Sort
    const [sortKey, setSortKey] = useState<SortKey>("storeCode");
    const [sortDir, setSortDir] = useState<SortDir>("asc");

    const load = useCallback(async () => {
        setLoading(true);
        try { setReport(await apiClient.getHardwareInventoryReport()); }
        catch (e) { console.error(e); }
        finally { setLoading(false); }
    }, []);

    useEffect(() => { void load(); }, [load]);

    const uniqueRam = useMemo(() => {
        if (!report) return [];
        return [...new Set(report.rows.map(r => ramBucket(r.totalRamMB)))].sort();
    }, [report]);

    const activeFilterCount = (filterOnline !== "all" ? 1 : 0) + (filterRam.size > 0 ? 1 : 0);

    const filteredRows = useMemo(() => {
        if (!report) return [];
        const term = search.trim().toLowerCase();
        return report.rows.filter(row => {
            if (term && !(
                row.hostname.toLowerCase().includes(term) ||
                (row.storeName || "").toLowerCase().includes(term) ||
                String(row.storeCode).includes(term) ||
                (row.cpuModel || "").toLowerCase().includes(term) ||
                (row.os || "").toLowerCase().includes(term)
            )) return false;
            if (filterOnline === "online" && !row.online) return false;
            if (filterOnline === "offline" && row.online) return false;
            if (filterRam.size > 0 && !filterRam.has(ramBucket(row.totalRamMB))) return false;
            return true;
        });
    }, [report, search, filterOnline, filterRam]);

    const sortedRows = useMemo(() => {
        return [...filteredRows].sort((a, b) => compareRows(a, b, sortKey, sortDir));
    }, [filteredRows, sortKey, sortDir]);

    const toggleSort = (key: SortKey) => {
        if (sortKey === key) setSortDir(d => d === "asc" ? "desc" : "asc");
        else { setSortKey(key); setSortDir("asc"); }
    };

    const toggleFilter = (set: Set<string>, value: string, setter: React.Dispatch<React.SetStateAction<Set<string>>>) => {
        const next = new Set(set);
        if (next.has(value)) next.delete(value); else next.add(value);
        setter(next);
    };

    const clearAllFilters = () => {
        setFilterOnline("all");
        setFilterRam(new Set());
        setSearch("");
    };

    // Stats
    const onlineCount = filteredRows.filter(r => r.online).length;

    return (
        <div className="mx-auto max-w-[1800px] space-y-5 p-6">
            {/* Header */}
            <div className="flex flex-col justify-between gap-4 xl:flex-row xl:items-end">
                <div>
                    <h1 className="flex items-center gap-3 text-2xl font-bold text-ms-text">
                        <Server className="h-6 w-6 text-violet-400" />
                        Donanım Envanteri
                    </h1>
                    <p className="mt-1 text-sm text-ms-text-muted">
                        {report ? `${report.totalDevices} cihaz` : "Yükleniyor..."} — CPU, RAM, disk, sağlık durumu tek ekranda.
                    </p>
                </div>

                <div className="flex flex-wrap items-center gap-2">
                    <div className="relative">
                        <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ms-text-muted" />
                        <input
                            value={search}
                            onChange={e => setSearch(e.target.value)}
                            placeholder="Mağaza, cihaz, CPU, OS…"
                            className="w-64 !pl-9"
                        />
                    </div>

                    <button
                        onClick={() => setShowFilters(!showFilters)}
                        className={`btn-secondary !px-3 relative ${showFilters ? 'ring-2 ring-violet-500/50' : ''}`}
                    >
                        <Filter className="h-4 w-4" /> Filtre
                        {activeFilterCount > 0 && (
                            <span className="absolute -top-1.5 -right-1.5 h-4 w-4 rounded-full bg-violet-500 text-[10px] font-bold text-white flex items-center justify-center">
                                {activeFilterCount}
                            </span>
                        )}
                    </button>

                    <button onClick={() => exportCsv(sortedRows)} className="btn-secondary !px-3" title="CSV İndir">
                        <Download className="h-4 w-4" /> CSV
                    </button>

                    <button onClick={load} disabled={loading} className="btn-primary !px-3">
                        <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} /> Yenile
                    </button>
                </div>
            </div>

            {/* Filter panel */}
            {showFilters && (
                <div className="card animate-fade-in">
                    <div className="flex items-center justify-between mb-3">
                        <span className="text-xs font-semibold uppercase tracking-wider text-ms-text-muted">Filtreler</span>
                        {activeFilterCount > 0 && (
                            <button onClick={clearAllFilters} className="text-[11px] text-violet-400 hover:text-violet-300 transition-colors">
                                Tümünü temizle
                            </button>
                        )}
                    </div>
                    <div className="flex flex-wrap gap-6">
                        {/* Online/Offline */}
                        <div>
                            <div className="text-[11px] font-medium text-ms-text-muted mb-1.5">Bağlantı</div>
                            <div className="flex gap-1.5">
                                {(["all", "online", "offline"] as const).map(v => (
                                    <Chip key={v} label={v === "all" ? "Tümü" : v === "online" ? "Online" : "Offline"}
                                        active={filterOnline === v} onClick={() => setFilterOnline(v)}
                                        color={v === "online" ? "emerald" : v === "offline" ? "rose" : "slate"} />
                                ))}
                            </div>
                        </div>
                        {/* RAM */}
                        <div>
                            <div className="text-[11px] font-medium text-ms-text-muted mb-1.5">RAM Kapasitesi</div>
                            <div className="flex flex-wrap gap-1.5">
                                {uniqueRam.map(r => (
                                    <Chip key={r} label={r} active={filterRam.has(r)} onClick={() => toggleFilter(filterRam, r, setFilterRam)} color="violet" />
                                ))}
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* Stats row */}
            <div className="grid gap-3 grid-cols-2 md:grid-cols-4">
                <StatCard icon={<Server className="h-4 w-4" />} label="Toplam" value={filteredRows.length} sub={`/ ${report?.totalDevices ?? 0}`} color="violet" />
                <StatCard icon={<Wifi className="h-4 w-4" />} label="Online" value={onlineCount} sub={`${filteredRows.length ? Math.round(onlineCount / filteredRows.length * 100) : 0}%`} color="emerald" />
                <StatCard icon={<WifiOff className="h-4 w-4" />} label="Offline" value={filteredRows.length - onlineCount} color="rose" />
                <StatCard icon={<Cpu className="h-4 w-4" />} label="Filtrelenen" value={filteredRows.length} sub={activeFilterCount > 0 ? `(${activeFilterCount} filtre)` : ""} color="sky" />
            </div>

            {/* Table */}
            <div className="card !p-0 overflow-hidden">
                <div className="overflow-x-auto">
                    <table className="w-full min-w-[900px] text-left text-sm">
                        <thead>
                            <tr className="border-b border-ms-border">
                                <SortTh label="Kod" sortKey="storeCode" current={sortKey} dir={sortDir} onSort={toggleSort} width="w-16" />
                                <SortTh label="Mağaza" sortKey="storeName" current={sortKey} dir={sortDir} onSort={toggleSort} />
                                <SortTh label="CPU Modeli" sortKey="cpuModel" current={sortKey} dir={sortDir} onSort={toggleSort} />
                                <SortTh label="RAM" sortKey="totalRamMB" current={sortKey} dir={sortDir} onSort={toggleSort} width="w-20" />
                                <SortTh label="Disk" sortKey="totalDiskGB" current={sortKey} dir={sortDir} onSort={toggleSort} width="w-20" />
                                <SortTh label="OS" sortKey="os" current={sortKey} dir={sortDir} onSort={toggleSort} />
                            </tr>
                        </thead>
                        <tbody>
                            {sortedRows.map(row => <HwRow key={row.deviceId} row={row} />)}
                            {!loading && sortedRows.length === 0 && (
                                <tr><td colSpan={6} className="px-4 py-12 text-center text-ms-text-muted">Kayıt bulunamadı.</td></tr>
                            )}
                        </tbody>
                    </table>
                </div>
                {/* Footer */}
                <div className="border-t border-ms-border px-4 py-2 flex items-center justify-between text-[11px] text-ms-text-muted">
                    <span>{sortedRows.length} kayıt gösteriliyor</span>
                    {report && <span>Son güncelleme: {new Date(report.generatedAtUtc).toLocaleString("tr-TR")}</span>}
                </div>
            </div>
        </div>
    );
};

// ── Subcomponents ────────────────────────────────────────────────────────

const Chip: React.FC<{ label: string; active: boolean; onClick: () => void; color: string }> = ({ label, active, onClick, color }) => {
    const base = active
        ? `bg-${color}-500/20 text-${color}-300 border-${color}-500/40`
        : "bg-ms-hover-bg text-ms-text-muted border-transparent hover:border-ms-border";
    return (
        <button onClick={onClick} className={`px-2.5 py-1 rounded-lg text-xs font-medium border transition-colors ${base}`}>
            {label}
        </button>
    );
};

const StatCard: React.FC<{ icon: React.ReactNode; label: string; value: string | number; sub?: string; color: string }> = ({ icon, label, value, sub, color }) => (
    <div className={`card !p-3 border-${color}-500/20`}>
        <div className="flex items-center gap-2 mb-1">
            <span className={`text-${color}-400`}>{icon}</span>
            <span className="text-[10px] font-semibold uppercase tracking-wider text-ms-text-muted">{label}</span>
        </div>
        <div className="flex items-baseline gap-1.5">
            <span className="text-xl font-bold text-ms-text">{value}</span>
            {sub && <span className="text-[11px] text-ms-text-muted">{sub}</span>}
        </div>
    </div>
);

const SortTh: React.FC<{
    label: string; sortKey: SortKey; current: SortKey; dir: SortDir;
    onSort: (key: SortKey) => void; width?: string;
}> = ({ label, sortKey: key, current, dir, onSort, width }) => (
    <th
        onClick={() => onSort(key)}
        className={`px-4 py-3 text-[11px] font-semibold uppercase tracking-wider text-ms-text-muted cursor-pointer hover:text-ms-text select-none transition-colors ${width || ""}`}
    >
        <div className="flex items-center gap-1">
            {label}
            {current === key && (dir === "asc" ? <ChevronUp className="w-3 h-3 text-violet-400" /> : <ChevronDown className="w-3 h-3 text-violet-400" />)}
        </div>
    </th>
);

const HwRow: React.FC<{ row: HardwareInventoryRow }> = ({ row }) => (
    <tr className="border-t border-ms-border text-ms-text hover:bg-ms-hover-bg transition-colors">
        <td className="px-4 py-2.5 font-mono text-xs text-ms-text-muted">{row.storeCode > 0 ? row.storeCode : "-"}</td>
        <td className="px-4 py-2.5">
            <div className="font-medium">{row.storeName || "-"}</div>
            <div className="text-[11px] text-ms-text-muted">{row.hostname}</div>
        </td>
        <td className="px-4 py-2.5">
            <div className="text-[12px]">{row.cpuModel || "-"}</div>
        </td>
        <td className="px-4 py-2.5 font-semibold">{formatRam(row.totalRamMB)}</td>
        <td className="px-4 py-2.5">
            <div>{formatDisk(row.totalDiskGB)}</div>
            {row.totalDiskDGB ? <div className="text-[10px] text-ms-text-muted">D: {formatDisk(row.totalDiskDGB)}</div> : null}
        </td>
        <td className="px-4 py-2.5 text-[12px] text-ms-text-muted">{row.os || "-"}</td>
    </tr>
);

export default HardwareInventoryReportPage;
