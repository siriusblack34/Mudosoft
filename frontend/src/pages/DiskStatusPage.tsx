import React, { useState } from "react";
import { createPortal } from "react-dom";
import { apiClient, type CommandResultRecord } from "../lib/apiClient";
import {
    HardDrive, RefreshCw, Wifi, WifiOff,
    ArrowUpDown, Search, AlertTriangle, Loader2, Monitor, ShoppingCart,
    FolderSearch, X, Folder, Trash2, CheckCircle2, Database
} from "lucide-react";
import type { DiskDevice } from "../contexts/PrefetchContext";

type FolderSizeItem = {
    name: string;
    localPath: string;
    sizeGB: number;
    fileCount: number;
    timedOut: boolean;
    category: string;
};

type DiskAnalysisResult = {
    deviceId: string;
    deviceName: string;
    storeName: string;
    ipAddress: string;
    folders: FolderSizeItem[];
    error?: string;
};

type AnalyzeModal = {
    device: DiskDevice;
    data: DiskAnalysisResult | null;
    loading: boolean;
    error: string | null;
};

type SortKey = "deviceName" | "storeCode" | "diskCPercent" | "diskDPercent";
type SortDir = "asc" | "desc";
type Tab = "pc" | "kasa";

type DiskStatusPageProps = {
    embedded?: boolean;
};

const DiskStatusPage: React.FC<DiskStatusPageProps> = ({ embedded = false }) => {
    const [activeTab, setActiveTab] = useState<Tab>("pc");

    const [pcDevices, setPcDevices] = useState<DiskDevice[]>([]);
    const [pcLoading, setPcLoading] = useState(false);
    const [pcLastUpdated, setPcLastUpdated] = useState<Date | null>(null);

    const [kasaDevices, setKasaDevices] = useState<DiskDevice[]>([]);
    const [kasaLoading, setKasaLoading] = useState(false);
    const [kasaLastUpdated, setKasaLastUpdated] = useState<Date | null>(null);

    const [sortKey, setSortKey] = useState<SortKey>("storeCode");
    const [sortDir, setSortDir] = useState<SortDir>("asc");
    const [search, setSearch] = useState("");

    const [analyzeModal, setAnalyzeModal] = useState<AnalyzeModal | null>(null);

    const handleAnalyze = async (device: DiskDevice) => {
        setAnalyzeModal({ device, data: null, loading: true, error: null });
        try {
            const res = await apiClient.get<DiskAnalysisResult>(
                `/api/disk-status/analyze/${device.deviceId}`,
                90_000
            );
            setAnalyzeModal(prev => prev ? { ...prev, data: res, loading: false } : null);
        } catch (err: unknown) {
            const msg = err instanceof Error ? err.message : "Analiz başarısız";
            setAnalyzeModal(prev => prev ? { ...prev, loading: false, error: msg } : null);
        }
    };

    const loadPcData = async () => {
        setPcLoading(true);
        try {
            const res = await apiClient.post<DiskDevice[]>("/api/disk-status/check-all", undefined, 120_000);
            setPcDevices(res);
            const now = new Date();
            setPcLastUpdated(now);
        } catch (err) {
            console.error("PC disk status load failed:", err);
        } finally {
            setPcLoading(false);
        }
    };

    const loadKasaData = async () => {
        setKasaLoading(true);
        try {
            const res = await apiClient.post<DiskDevice[]>("/api/disk-status/check-all-kasa", undefined, 120_000);
            setKasaDevices(res);
            const now = new Date();
            setKasaLastUpdated(now);
        } catch (err) {
            console.error("Kasa disk status load failed:", err);
        } finally {
            setKasaLoading(false);
        }
    };

    const devices = activeTab === "pc" ? pcDevices : kasaDevices;
    const loading = activeTab === "pc" ? pcLoading : kasaLoading;
    const lastUpdated = activeTab === "pc" ? pcLastUpdated : kasaLastUpdated;
    const loadData = activeTab === "pc" ? loadPcData : loadKasaData;

    const filtered = devices.filter(d => {
        if (!search) return true;
        const q = search.toLowerCase();
        return (
            d.deviceName?.toLowerCase().includes(q) ||
            d.storeCode?.toString().includes(q) ||
            d.storeName?.toLowerCase().includes(q) ||
            d.ipAddress?.includes(q)
        );
    });

    const sorted = [...filtered].sort((a, b) => {
        let cmp = 0;
        switch (sortKey) {
            case "deviceName":
                cmp = (a.deviceName || "").localeCompare(b.deviceName || "");
                break;
            case "storeCode":
                cmp = a.storeCode - b.storeCode;
                break;
            case "diskCPercent":
                cmp = (a.diskCPercent || 0) - (b.diskCPercent || 0);
                break;
            case "diskDPercent":
                cmp = (a.diskDPercent || 0) - (b.diskDPercent || 0);
                break;
        }
        return sortDir === "asc" ? cmp : -cmp;
    });

    const handleSort = (key: SortKey) => {
        if (sortKey === key) {
            setSortDir(d => d === "asc" ? "desc" : "asc");
        } else {
            setSortKey(key);
            setSortDir("asc");
        }
    };

    const onlineCount = devices.filter(d => d.status === "online").length;
    const criticalC = devices.filter(d => d.diskCPercent !== null && d.diskCPercent >= 90).length;
    const criticalD = devices.filter(d => d.diskDPercent !== null && d.diskDPercent >= 90).length;

    const handleTabChange = (tab: Tab) => {
        setActiveTab(tab);
        setSearch("");
        setSortKey("storeCode");
        setSortDir("asc");
    };

    return (
        <div className={embedded ? "space-y-6 bg-transparent text-slate-200" : "p-6 space-y-6 bg-transparent min-h-screen text-slate-200"}>
            {/* Header */}
            <div className="flex items-center justify-between">
                <div>
                    <h1 className="text-2xl font-bold text-white tracking-tight flex items-center gap-2">
                        <HardDrive className="text-violet-500" />
                        Disk Durumu
                    </h1>
                    <p className="text-sm text-slate-400">Mağaza PC ve kasaların disk kullanım durumu</p>
                </div>
                <div className="flex items-center gap-3">
                    {lastUpdated ? (
                        <span className="text-xs text-slate-500">
                            Son kontrol: {lastUpdated.toLocaleTimeString('tr-TR')}
                        </span>
                    ) : null}
                    <button
                        onClick={loadData}
                        disabled={loading}
                        className="flex items-center gap-2 px-6 py-2.5 bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 disabled:opacity-50 text-white rounded-xl text-sm font-medium transition-all duration-300 shadow-[0_0_15px_rgba(139,92,246,0.3)] hover:-translate-y-0.5 border-none"
                    >
                        {loading ? (
                            <>
                                <Loader2 className="w-4 h-4 animate-spin" />
                                Kontrol ediliyor...
                            </>
                        ) : (
                            <>
                                <RefreshCw className="w-4 h-4" />
                                Tümünü Kontrol Et
                            </>
                        )}
                    </button>
                </div>
            </div>

            {/* Tabs */}
            <div className="flex gap-1 p-1 glass-card rounded-xl border-white/5 w-fit">
                <button
                    onClick={() => handleTabChange("pc")}
                    className={`flex items-center gap-2 px-5 py-2.5 rounded-lg text-sm font-medium transition-all duration-200 ${activeTab === "pc"
                        ? "bg-violet-600 text-white shadow-lg shadow-violet-500/20"
                        : "text-slate-400 hover:text-slate-200 hover:bg-white/5"
                    }`}
                >
                    <Monitor className="w-4 h-4" />
                    Mağaza PC
                    {pcDevices.length > 0 && (
                        <span className={`text-xs px-1.5 py-0.5 rounded-full ${activeTab === "pc" ? "bg-white/20" : "bg-white/10"}`}>
                            {pcDevices.length}
                        </span>
                    )}
                </button>
                <button
                    onClick={() => handleTabChange("kasa")}
                    className={`flex items-center gap-2 px-5 py-2.5 rounded-lg text-sm font-medium transition-all duration-200 ${activeTab === "kasa"
                        ? "bg-violet-600 text-white shadow-lg shadow-violet-500/20"
                        : "text-slate-400 hover:text-slate-200 hover:bg-white/5"
                    }`}
                >
                    <ShoppingCart className="w-4 h-4" />
                    Kasalar
                    {kasaDevices.length > 0 && (
                        <span className={`text-xs px-1.5 py-0.5 rounded-full ${activeTab === "kasa" ? "bg-white/20" : "bg-white/10"}`}>
                            {kasaDevices.length}
                        </span>
                    )}
                </button>
            </div>

            {/* Summary Cards */}
            {devices.length > 0 && (
                <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
                    <SummaryCard icon={HardDrive} label="Toplam Cihaz" value={devices.length} color="indigo" />
                    <SummaryCard icon={Wifi} label="Online" value={`${onlineCount} / ${devices.length}`} color="emerald" />
                    <SummaryCard icon={AlertTriangle} label="Kritik C:" value={criticalC} color={criticalC > 0 ? "rose" : "slate"} subtitle="%90+ dolu" />
                    {activeTab === "pc" && (
                        <SummaryCard icon={AlertTriangle} label="Kritik D:" value={criticalD} color={criticalD > 0 ? "amber" : "slate"} subtitle="%90+ dolu" />
                    )}
                    {activeTab === "kasa" && (
                        <SummaryCard icon={Wifi} label="Offline" value={devices.filter(d => d.status === "offline").length} color="slate" subtitle="erişilemeyen" />
                    )}
                </div>
            )}

            {/* Empty State */}
            {devices.length === 0 && !loading && (
                <div className="flex flex-col items-center justify-center py-20 text-slate-500">
                    <HardDrive className="w-16 h-16 mb-4 text-slate-700" />
                    <p className="text-lg font-medium text-slate-400 mb-2">
                        {activeTab === "pc" ? "Mağaza PC disk durumu henüz kontrol edilmedi" : "Kasa disk durumu henüz kontrol edilmedi"}
                    </p>
                    <p className="text-sm mb-6">Yukarıdaki "Tümünü Kontrol Et" butonuna tıklayarak başlayın</p>
                    <button
                        onClick={loadData}
                        className="flex items-center gap-2 px-6 py-3 bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 rounded-xl text-sm font-bold transition-all shadow-[0_0_15px_rgba(139,92,246,0.3)] hover:-translate-y-0.5"
                    >
                        <RefreshCw className="w-4 h-4" />
                        Tümünü Kontrol Et
                    </button>
                </div>
            )}

            {/* Loading State */}
            {loading && devices.length === 0 && (
                <div className="flex flex-col items-center justify-center py-20">
                    <Loader2 className="w-10 h-10 text-violet-500 animate-spin mb-4" />
                    <p className="text-slate-400 font-medium">
                        {activeTab === "pc" ? "Tüm mağaza PC'leri kontrol ediliyor..." : "Tüm kasalar kontrol ediliyor..."}
                    </p>
                    <p className="text-sm text-slate-600 mt-1">Bu işlem 30-60 saniye sürebilir</p>
                </div>
            )}

            {/* Results */}
            {devices.length > 0 && (
                <>
                    {/* Search */}
                    <div className="flex items-center gap-4">
                        <div className="relative flex-1 max-w-md">
                            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                            <input
                                type="text"
                                placeholder="Mağaza kodu, cihaz adı, IP veya isim ara..."
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                                className="w-full pl-10 pr-4 py-3 glass-card rounded-xl text-sm text-slate-200 placeholder:text-slate-500 focus-ring transition-all border-white/5"
                            />
                        </div>
                        <div className="flex items-center gap-2 text-xs text-slate-500">
                            <span>{sorted.length} cihaz</span>
                            {search && <span>({devices.length} toplam)</span>}
                        </div>
                    </div>

                    {/* Table */}
                    {activeTab === "pc" ? (
                        <PcTable sorted={sorted} sortKey={sortKey} sortDir={sortDir} handleSort={handleSort} onAnalyze={handleAnalyze} />
                    ) : (
                        <KasaTable sorted={sorted} sortKey={sortKey} sortDir={sortDir} handleSort={handleSort} onAnalyze={handleAnalyze} />
                    )}

                    {/* Legend */}
                    <div className="flex items-center gap-6 text-xs text-slate-500">
                        <div className="flex items-center gap-2">
                            <div className="w-3 h-1.5 rounded-full bg-gradient-to-r from-emerald-500 to-emerald-400" />
                            <span>Normal (&lt;70%)</span>
                        </div>
                        <div className="flex items-center gap-2">
                            <div className="w-3 h-1.5 rounded-full bg-gradient-to-r from-amber-500 to-amber-400" />
                            <span>Uyarı (70–89%)</span>
                        </div>
                        <div className="flex items-center gap-2">
                            <div className="w-3 h-1.5 rounded-full bg-gradient-to-r from-rose-500 to-rose-400" />
                            <span>Kritik (90%+)</span>
                        </div>
                        <div className="flex items-center gap-2">
                            <div className="w-3 h-1.5 rounded-full bg-slate-700" />
                            <span>Veri yok / Offline</span>
                        </div>
                    </div>
                </>
            )}
            {analyzeModal && (
                <DiskAnalysisModal
                    modal={analyzeModal}
                    onClose={() => setAnalyzeModal(null)}
                />
            )}
        </div>
    );
};

// === PC TABLE ===
const PcTable: React.FC<{
    sorted: DiskDevice[];
    sortKey: SortKey;
    sortDir: SortDir;
    handleSort: (key: SortKey) => void;
    onAnalyze: (device: DiskDevice) => void;
}> = ({ sorted, sortKey, sortDir, handleSort, onAnalyze }) => (
    <div className="glass-card border-white/5 rounded-2xl overflow-hidden shadow-xl">
        <div className="grid grid-cols-[180px_80px_80px_1fr_1fr_44px] items-center px-6 py-4 bg-white/5 border-b border-white/5 text-xs font-semibold text-slate-300 uppercase tracking-widest">
            <SortHeader label="Cihaz" sortKey="deviceName" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <SortHeader label="Mağaza" sortKey="storeCode" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <span>Durum</span>
            <SortHeader label="C: Sürücü" sortKey="diskCPercent" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <SortHeader label="D: Sürücü" sortKey="diskDPercent" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <span />
        </div>
        <div className="divide-y divide-slate-700/20">
            {sorted.map(device => (
                <div
                    key={device.deviceId}
                    className={`grid grid-cols-[180px_80px_80px_1fr_1fr_44px] items-center px-6 py-4 hover:bg-slate-800/30 transition-colors ${device.status === "offline" ? "opacity-50" : ""} ${(device.diskCPercent !== null && device.diskCPercent >= 90) || (device.diskDPercent !== null && device.diskDPercent >= 90) ? "bg-rose-500/5 border-l-2 border-l-rose-500/50" : ""}`}
                >
                    <DeviceCell device={device} />
                    <StoreCodeCell device={device} />
                    <StatusCell device={device} />
                    <DiskBar percent={device.diskCPercent} totalGB={device.diskCTotalGB} usedGB={device.diskCUsedGB} freeGB={device.diskCFreeGB} driveLabel="C:" offline={device.status === "offline"} />
                    <DiskBar percent={device.diskDPercent} totalGB={device.diskDTotalGB} usedGB={device.diskDUsedGB} freeGB={device.diskDFreeGB} driveLabel="D:" offline={device.status === "offline"} />
                    <div className="flex justify-center">
                        <button
                            onClick={() => onAnalyze(device)}
                            disabled={device.status === "offline"}
                            title="Disk analizi — hangi klasör yer kaplıyor?"
                            className="p-1.5 rounded-lg text-slate-500 hover:text-violet-400 hover:bg-violet-500/10 disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                        >
                            <FolderSearch className="w-4 h-4" />
                        </button>
                    </div>
                </div>
            ))}
            {sorted.length === 0 && <div className="text-center py-12 text-slate-500">Aramaniza uygun cihaz bulunamadi</div>}
        </div>
    </div>
);

// === KASA TABLE (sadece C:) ===
const KasaTable: React.FC<{
    sorted: DiskDevice[];
    sortKey: SortKey;
    sortDir: SortDir;
    handleSort: (key: SortKey) => void;
    onAnalyze: (device: DiskDevice) => void;
}> = ({ sorted, sortKey, sortDir, handleSort, onAnalyze }) => (
    <div className="glass-card border-white/5 rounded-2xl overflow-hidden shadow-xl">
        <div className="grid grid-cols-[200px_80px_80px_80px_1fr_44px] items-center px-6 py-4 bg-white/5 border-b border-white/5 text-xs font-semibold text-slate-300 uppercase tracking-widest">
            <SortHeader label="Mağaza" sortKey="storeCode" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <SortHeader label="Mağaza No" sortKey="storeCode" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <span>Kasa</span>
            <span>Durum</span>
            <SortHeader label="C: Sürücü" sortKey="diskCPercent" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <span />
        </div>
        <div className="divide-y divide-slate-700/20">
            {sorted.map(device => (
                <div
                    key={device.deviceId}
                    className={`grid grid-cols-[200px_80px_80px_80px_1fr_44px] items-center px-6 py-4 hover:bg-slate-800/30 transition-colors ${device.status === "offline" ? "opacity-50" : ""} ${device.diskCPercent !== null && device.diskCPercent >= 90 ? "bg-rose-500/5 border-l-2 border-l-rose-500/50" : ""}`}
                >
                    <div className="flex flex-col">
                        <span className="text-sm font-medium text-white truncate">{device.storeName}</span>
                        <span className="text-[11px] text-slate-500 font-mono">{device.ipAddress}</span>
                    </div>
                    <StoreCodeCell device={device} />
                    <div className="text-xs text-slate-400 font-mono">{device.deviceName}</div>
                    <StatusCell device={device} />
                    <DiskBar percent={device.diskCPercent} totalGB={device.diskCTotalGB} usedGB={device.diskCUsedGB} freeGB={device.diskCFreeGB} driveLabel="C:" offline={device.status === "offline"} />
                    <div className="flex justify-center">
                        <button
                            onClick={() => onAnalyze(device)}
                            disabled={device.status === "offline"}
                            title="Disk analizi — hangi klasör yer kaplıyor?"
                            className="p-1.5 rounded-lg text-slate-500 hover:text-violet-400 hover:bg-violet-500/10 disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                        >
                            <FolderSearch className="w-4 h-4" />
                        </button>
                    </div>
                </div>
            ))}
            {sorted.length === 0 && <div className="text-center py-12 text-slate-500">Aramaniza uygun kasa bulunamadi</div>}
        </div>
    </div>
);

// === SHARED CELLS ===
const DeviceCell: React.FC<{ device: DiskDevice }> = ({ device }) => (
    <div className="flex flex-col">
        <span className="text-sm font-medium text-white truncate" title={`${device.deviceName} (${device.storeName})`}>
            {device.storeName}
        </span>
        <span className="text-[11px] text-slate-500 font-mono">{device.ipAddress}</span>
    </div>
);

const StoreCodeCell: React.FC<{ device: DiskDevice }> = ({ device }) => (
    <div className="text-sm text-slate-400 font-mono">{device.storeCode || "—"}</div>
);

const StatusCell: React.FC<{ device: DiskDevice }> = ({ device }) => (
    <div>
        {device.status === "online" ? (
            <span className="flex items-center gap-1.5 text-xs text-emerald-400">
                <span className="w-2 h-2 rounded-full bg-emerald-500 shadow-[0_0_6px_rgba(16,185,129,0.5)]" />
                Online
            </span>
        ) : device.status === "offline" ? (
            <span className="flex items-center gap-1.5 text-xs text-slate-500">
                <WifiOff className="w-3 h-3" />
                Offline
            </span>
        ) : (
            <span className="flex items-center gap-1.5 text-xs text-rose-400" title={device.errorMessage}>
                <AlertTriangle className="w-3 h-3" />
                Hata
            </span>
        )}
    </div>
);

// === SUB-COMPONENTS ===

const DiskBar: React.FC<{
    percent: number | null;
    totalGB: number | null;
    usedGB: number | null;
    freeGB: number | null;
    driveLabel: string;
    offline?: boolean;
}> = ({ percent, totalGB, usedGB, freeGB, driveLabel, offline }) => {
    if (offline || percent === null || percent === undefined) {
        return (
            <div className="px-3">
                <div className="flex items-center justify-between mb-1">
                    <span className="text-xs text-slate-600">{driveLabel}</span>
                    <span className="text-xs text-slate-600">—</span>
                </div>
                <div className="h-3 bg-slate-800/50 rounded-full overflow-hidden border border-slate-700/30">
                    <div className="h-full w-0" />
                </div>
            </div>
        );
    }

    const getBarColor = (p: number) => {
        if (p >= 90) return "from-rose-600 to-rose-400";
        if (p >= 70) return "from-amber-600 to-amber-400";
        return "from-emerald-600 to-emerald-400";
    };

    const getTextColor = (p: number) => {
        if (p >= 90) return "text-rose-400";
        if (p >= 70) return "text-amber-400";
        return "text-emerald-400";
    };

    const getBorderColor = (p: number) => {
        if (p >= 90) return "border-rose-500/30";
        if (p >= 70) return "border-amber-500/20";
        return "border-slate-700/30";
    };

    return (
        <div className="px-3">
            <div className="flex items-center justify-between mb-1.5">
                <div className="flex items-center gap-2">
                    <span className={`text-sm font-bold ${getTextColor(percent)}`}>
                        %{percent}
                    </span>
                    {percent >= 90 && (
                        <AlertTriangle className="w-3.5 h-3.5 text-rose-400 animate-pulse" />
                    )}
                </div>
                <div className="text-[11px] text-slate-500 flex gap-2">
                    <span>{usedGB ?? 0} / {totalGB ?? 0} GB</span>
                    <span className="text-slate-600">({freeGB ?? 0} GB boş)</span>
                </div>
            </div>
            <div className={`h-3 bg-slate-800/50 rounded-full overflow-hidden border ${getBorderColor(percent)} relative`}>
                <div
                    className={`h-full bg-gradient-to-r ${getBarColor(percent)} rounded-full transition-all duration-700 ease-out relative`}
                    style={{ width: `${Math.min(percent, 100)}%` }}
                >
                    {/* Shimmer effect */}
                    <div className="absolute inset-0 bg-gradient-to-r from-transparent via-white/10 to-transparent animate-shimmer" />
                </div>
            </div>
        </div>
    );
};

const SortHeader: React.FC<{
    label: string;
    sortKey: SortKey;
    currentSort: SortKey;
    dir: SortDir;
    onSort: (key: SortKey) => void;
}> = ({ label, sortKey, currentSort, dir, onSort }) => (
    <button
        onClick={() => onSort(sortKey)}
        className="flex items-center gap-1 hover:text-slate-200 transition-colors group"
    >
        {label}
        <ArrowUpDown className={`w-3 h-3 transition-colors ${currentSort === sortKey ? 'text-violet-400' : 'text-slate-600 group-hover:text-slate-400'}`} />
        {currentSort === sortKey && (
            <span className="text-violet-400 text-[10px]">{dir === "asc" ? "↑" : "↓"}</span>
        )}
    </button>
);

const SummaryCard: React.FC<{
    icon: any;
    label: string;
    value: any;
    color: string;
    subtitle?: string;
}> = ({ icon: Icon, label, value, color, subtitle }) => {
    const colors: Record<string, string> = {
        slate: "text-slate-400",
        emerald: "text-emerald-400",
        sky: "text-sky-400",
        rose: "text-rose-400",
        amber: "text-amber-400",
        indigo: "text-indigo-400",
        violet: "text-violet-400"
    };

    return (
        <div className="glass-panel border-white/5 p-5 rounded-2xl flex items-center gap-4 hover-lift transition-all duration-300 group">
            <div className={`p-3 rounded-xl bg-${color}-500/10 shadow-inner group-hover:bg-${color}-500/20 transition-colors`}>
                <Icon className={`w-5 h-5 ${colors[color]}`} />
            </div>
            <div>
                <div className="text-[11px] text-slate-400 uppercase font-semibold tracking-wider mb-0.5">{label}</div>
                <div className={`text-xl font-bold ${colors[color]}`}>{value}</div>
                {subtitle && <div className="text-xs text-slate-500 mt-0.5">{subtitle}</div>}
            </div>
        </div>
    );
};

// === DISK ANALYSIS MODAL ===
const CATEGORY_META: Record<string, { label: string; color: string; bg: string; barColor: string }> = {
    temp:      { label: "Geçici",    color: "text-rose-400",   bg: "bg-rose-500/15",   barColor: "from-rose-600 to-rose-400" },
    updates:   { label: "Win.Upd",   color: "text-amber-400",  bg: "bg-amber-500/15",  barColor: "from-amber-600 to-amber-400" },
    pos:       { label: "POS",       color: "text-sky-400",    bg: "bg-sky-500/15",    barColor: "from-sky-600 to-sky-400" },
    users:     { label: "Kullanıcı", color: "text-violet-400", bg: "bg-violet-500/15", barColor: "from-violet-600 to-violet-400" },
    logs:      { label: "Log",       color: "text-orange-400", bg: "bg-orange-500/15", barColor: "from-orange-600 to-orange-400" },
    sql:       { label: "SQL Log",   color: "text-cyan-400",   bg: "bg-cyan-500/15",   barColor: "from-cyan-600 to-cyan-400" },
    system:    { label: "Sistem",    color: "text-slate-400",  bg: "bg-slate-500/15",  barColor: "from-slate-600 to-slate-400" },
    installer: { label: "Installer", color: "text-purple-400", bg: "bg-purple-500/15", barColor: "from-purple-600 to-purple-400" },
    cache:     { label: "Cache",     color: "text-teal-400",   bg: "bg-teal-500/15",   barColor: "from-teal-600 to-teal-400" },
    wer:       { label: "Hata Rpr.", color: "text-red-400",    bg: "bg-red-500/15",    barColor: "from-red-600 to-red-400" },
    dumps:     { label: "Dump",      color: "text-zinc-400",   bg: "bg-zinc-500/15",   barColor: "from-zinc-600 to-zinc-400" },
    other:     { label: "Diğer",     color: "text-slate-400",  bg: "bg-slate-500/15",  barColor: "from-slate-600 to-slate-400" },
};

// Categories where we can safely offer a cleanup button
const CLEANABLE_CATEGORIES = new Set(["temp", "updates", "logs", "sql", "cache", "wer", "dumps"]);

type CleanupStatus = "idle" | "loading" | "done" | "error";

const pollCommandResult = async (commandId: string, maxMs = 150_000): Promise<CommandResultRecord> => {
    const deadline = Date.now() + maxMs;
    while (Date.now() < deadline) {
        await new Promise(r => setTimeout(r, 3000));
        try {
            const res = await apiClient.getCommandDetails(commandId);
            if (res.completedAtUtc !== null) return res;
            // completedAtUtc null → agent aldı ama henüz bitmedi, bekle
        } catch (err: unknown) {
            const msg = err instanceof Error ? err.message : "";
            // 404 = agent henüz sonucu kaydetmedi, polling devam etsin
            if (msg.includes("404")) continue;
            throw err; // gerçek hata
        }
    }
    throw new Error("Komut 2.5 dakika içinde tamamlanmadı — agent bağlı olmayabilir");
};

const DiskAnalysisModal: React.FC<{
    modal: AnalyzeModal;
    onClose: () => void;
}> = ({ modal, onClose }) => {
    const { device, data, loading, error } = modal;
    const maxSizeGB = data?.folders[0]?.sizeGB ?? 1;

    const [cleanupStatus, setCleanupStatus] = React.useState<Record<string, CleanupStatus>>({});
    const [cleanupOutput, setCleanupOutput] = React.useState<Record<string, string>>({});
    const [sqlStatus, setSqlStatus] = React.useState<CleanupStatus>("idle");
    const [sqlOutput, setSqlOutput] = React.useState<string>("");

    const handleCleanup = async (folder: FolderSizeItem) => {
        const key = folder.name;
        setCleanupStatus(prev => ({ ...prev, [key]: "loading" }));

        try {
            let cleanupType = "folder";
            if (folder.name === "Windows\\Temp") cleanupType = "windows-temp";
            else if (folder.name === "Windows\\SoftwareDistribution\\Download") cleanupType = "software-distribution";

            const body: Record<string, string> = { cleanupType };
            if (cleanupType === "folder") body.folderPath = folder.localPath;

            const res = await apiClient.post<{ deletedCount: number; freedMB: number; errors: string[] }>(
                `/api/disk-status/cleanup/${device.deviceId}`, body, 60_000
            );
            const summary = `${res.deletedCount} dosya silindi, ${res.freedMB} MB kazanıldı` +
                (res.errors.length > 0 ? `\n${res.errors.slice(0, 5).join("\n")}${res.errors.length > 5 ? `\n... +${res.errors.length - 5} hata` : ""}` : "");
            setCleanupOutput(prev => ({ ...prev, [key]: summary }));
            setCleanupStatus(prev => ({ ...prev, [key]: res.deletedCount > 0 ? "done" : "error" }));
        } catch (err: unknown) {
            const msg = err instanceof Error ? err.message : "Hata";
            setCleanupOutput(prev => ({ ...prev, [key]: msg }));
            setCleanupStatus(prev => ({ ...prev, [key]: "error" }));
        }
    };

    const cleanableFolders = (data?.folders ?? []).filter(f => f.sizeGB > 0 && CLEANABLE_CATEGORIES.has(f.category));
    const allDone = cleanableFolders.length > 0 && cleanableFolders.every(f => cleanupStatus[f.name] === "done");
    const anyLoading = cleanableFolders.some(f => cleanupStatus[f.name] === "loading") || sqlStatus === "loading";

    const handleCleanAll = async () => {
        const pending = cleanableFolders.filter(f => cleanupStatus[f.name] !== "done");
        await Promise.all([
            ...pending.map(f => handleCleanup(f)),
            ...(sqlStatus !== "done" ? [handleSqlCleanup()] : []),
        ]);
    };

    const handleSqlCleanup = async () => {
        setSqlStatus("loading");
        setSqlOutput("");
        try {
            const res = await apiClient.post<{
                deletedCount: number;
                freedMB: number;
                files: string[];
                errors: string[];
            }>(`/api/disk-status/cleanup-sql-logs/${device.deviceId}`, undefined, 120_000);

            const lines = [
                `Tamamlandı: ${res.deletedCount} arşiv log dosyası silindi, ${res.freedMB} MB kazanıldı`,
                ...(res.files.length > 0 ? res.files.map(f => `✓ ${f}`) : ["(silinecek arşiv log bulunamadı)"]),
                ...(res.errors.length > 0 ? res.errors.map(e => `✗ ${e}`) : []),
            ];
            setSqlOutput(lines.join("\n"));
            setSqlStatus(res.errors.length > 0 && res.deletedCount === 0 ? "error" : "done");
        } catch (err: unknown) {
            const msg = err instanceof Error ? err.message : "Hata";
            setSqlOutput(msg);
            setSqlStatus("error");
        }
    };

    return createPortal(
        <div
            className="fixed inset-0 z-[9999] flex items-center justify-center p-4"
            style={{ background: "rgba(0,0,0,0.80)" }}
            onClick={e => { if (e.target === e.currentTarget && !loading && sqlStatus !== "loading") onClose(); }}
        >
            <div
                className="rounded-2xl w-full max-w-2xl max-h-[90vh] flex flex-col shadow-2xl border border-slate-700"
                style={{ background: "#111827", zIndex: 10000 }}
            >
                {/* Header */}
                <div className="flex items-start justify-between px-6 pt-5 pb-4 border-b border-white/5">
                    <div>
                        <div className="flex items-center gap-2 mb-1">
                            <FolderSearch className="w-5 h-5 text-violet-400" />
                            <h2 className="text-base font-bold text-white">Disk Analizi &amp; Temizleme</h2>
                        </div>
                        <p className="text-sm text-slate-300 font-medium">{device.storeName}</p>
                        <p className="text-xs text-slate-500 font-mono mt-0.5">
                            {device.ipAddress}
                            {device.diskCPercent != null && (
                                <span className="ml-2 text-rose-400 font-semibold">
                                    C: %{device.diskCPercent} dolu · {device.diskCUsedGB ?? "?"} / {device.diskCTotalGB ?? "?"} GB
                                </span>
                            )}
                        </p>
                    </div>
                    <div className="flex items-center gap-2 mt-0.5">
                        {data && !loading && cleanableFolders.length > 0 && (
                            <button
                                onClick={handleCleanAll}
                                disabled={anyLoading || allDone}
                                className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold transition-all ${
                                    allDone
                                        ? "bg-emerald-500/20 text-emerald-400 cursor-default"
                                        : anyLoading
                                        ? "bg-white/5 text-slate-500 cursor-wait"
                                        : "bg-rose-500/15 text-rose-300 hover:bg-rose-500/25"
                                }`}
                            >
                                {anyLoading ? (
                                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                                ) : allDone ? (
                                    <CheckCircle2 className="w-3.5 h-3.5" />
                                ) : (
                                    <Trash2 className="w-3.5 h-3.5" />
                                )}
                                {allDone ? "Temizlendi" : anyLoading ? "Temizleniyor…" : "Tümünü Temizle"}
                            </button>
                        )}
                        <button
                            onClick={onClose}
                            className="p-1.5 rounded-lg text-slate-500 hover:text-slate-200 hover:bg-white/5 transition-colors"
                        >
                            <X className="w-5 h-5" />
                        </button>
                    </div>
                </div>

                {/* Body */}
                <div className="flex-1 overflow-y-auto px-6 py-5 space-y-3">
                    {loading && (
                        <div className="flex flex-col items-center justify-center py-14 gap-3">
                            <Loader2 className="w-8 h-8 text-violet-400 animate-spin" />
                            <p className="text-slate-400 text-sm">Klasörler taranıyor, lütfen bekleyin…</p>
                            <p className="text-slate-600 text-xs">Bu işlem 60–120 saniye sürebilir</p>
                        </div>
                    )}

                    {error && !loading && (
                        <div className="flex items-center gap-3 p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-400 text-sm">
                            <AlertTriangle className="w-5 h-5 shrink-0" /><span>{error}</span>
                        </div>
                    )}

                    {data?.error && !loading && (
                        <div className="flex items-center gap-3 p-4 rounded-xl bg-amber-500/10 border border-amber-500/20 text-amber-400 text-sm">
                            <AlertTriangle className="w-5 h-5 shrink-0" /><span>{data.error}</span>
                        </div>
                    )}

                    {data && !loading && data.folders.length === 0 && !data.error && (
                        <div className="text-center py-12 text-slate-500">
                            <Folder className="w-10 h-10 mx-auto mb-3 text-slate-700" />
                            <p>Taranan klasörlerde anlamlı veri bulunamadı.</p>
                        </div>
                    )}

                    {data && !loading && data.folders.length > 0 && (
                        <>
                            <p className="text-xs text-slate-500 pb-1">
                                {data.folders.length} klasör tarandı — en büyükten küçüğe
                            </p>

                            {data.folders.filter(f => f.sizeGB > 0).map((f, i) => {
                                const meta = CATEGORY_META[f.category] ?? CATEGORY_META.other;
                                const barPct = maxSizeGB > 0 ? Math.min((f.sizeGB / maxSizeGB) * 100, 100) : 0;
                                const canClean = CLEANABLE_CATEGORIES.has(f.category);
                                const cStatus = cleanupStatus[f.name] ?? "idle";
                                const cOutput = cleanupOutput[f.name];
                                return (
                                    <div key={i} className="flex flex-col gap-1.5">
                                        <div className="flex items-center justify-between gap-2">
                                            <div className="flex items-center gap-2 min-w-0 flex-1">
                                                <span className={`text-[10px] font-semibold px-1.5 py-0.5 rounded-md shrink-0 ${meta.color} ${meta.bg}`}>
                                                    {meta.label}
                                                </span>
                                                <span className="text-sm font-mono text-slate-300 truncate" title={f.localPath}>
                                                    {f.name}
                                                </span>
                                                {f.timedOut && (
                                                    <span className="text-[10px] text-amber-500 shrink-0">(zaman aşımı)</span>
                                                )}
                                            </div>
                                            <div className="flex items-center gap-3 shrink-0">
                                                <div className="text-right">
                                                    <span className={`text-sm font-bold ${meta.color}`}>
                                                        {f.sizeGB >= 1 ? `${f.sizeGB} GB` : `${Math.round(f.sizeGB * 1024)} MB`}
                                                    </span>
                                                    <span className="text-[11px] text-slate-600 ml-2">
                                                        {f.fileCount.toLocaleString("tr-TR")} dosya
                                                    </span>
                                                </div>
                                                {canClean && (
                                                    <button
                                                        onClick={() => handleCleanup(f)}
                                                        disabled={cStatus === "loading" || cStatus === "done"}
                                                        title={cStatus === "done" ? cOutput : "Temizle"}
                                                        className={`flex items-center gap-1 px-2 py-1 rounded-lg text-xs font-medium transition-all shrink-0 ${
                                                            cStatus === "done"
                                                                ? "bg-emerald-500/15 text-emerald-400 cursor-default"
                                                                : cStatus === "error"
                                                                ? "bg-rose-500/15 text-rose-400 hover:bg-rose-500/25"
                                                                : cStatus === "loading"
                                                                ? "bg-white/5 text-slate-500 cursor-wait"
                                                                : "bg-white/5 text-slate-400 hover:bg-rose-500/15 hover:text-rose-300"
                                                        }`}
                                                    >
                                                        {cStatus === "loading" ? (
                                                            <Loader2 className="w-3 h-3 animate-spin" />
                                                        ) : cStatus === "done" ? (
                                                            <CheckCircle2 className="w-3 h-3" />
                                                        ) : (
                                                            <Trash2 className="w-3 h-3" />
                                                        )}
                                                        {cStatus === "loading" ? "Temizleniyor…" : cStatus === "done" ? "Temizlendi" : cStatus === "error" ? "Hata" : "Temizle"}
                                                    </button>
                                                )}
                                            </div>
                                        </div>
                                        <div className="h-2 bg-slate-800/60 rounded-full overflow-hidden border border-white/5">
                                            <div
                                                className={`h-full rounded-full transition-all duration-500 bg-gradient-to-r ${meta.barColor}`}
                                                style={{ width: `${barPct}%` }}
                                            />
                                        </div>
                                        {cStatus === "error" && cOutput && (
                                            <p className="text-[11px] text-rose-400 font-mono">{cOutput.slice(0, 120)}</p>
                                        )}
                                        {cStatus === "done" && cOutput && (
                                            <p className="text-[11px] text-emerald-400 font-mono line-clamp-2">{cOutput.slice(0, 200)}</p>
                                        )}
                                    </div>
                                );
                            })}

                            <div className="pt-2 border-t border-white/5 text-xs text-slate-500">
                                Toplam taranan:{" "}
                                <span className="text-slate-300 font-semibold">
                                    {data.folders.reduce((s, f) => s + f.sizeGB, 0).toFixed(1)} GB
                                </span>
                            </div>
                        </>
                    )}

                    {/* SQL Log Temizleme — her zaman göster */}
                    {!loading && (
                        <div className="border border-cyan-500/20 rounded-xl p-4 bg-cyan-500/5 space-y-3">
                            <div className="flex items-center justify-between">
                                <div className="flex items-center gap-2">
                                    <Database className="w-4 h-4 text-cyan-400" />
                                    <span className="text-sm font-semibold text-cyan-300">SQL Server Log Temizleme</span>
                                </div>
                                <button
                                    onClick={handleSqlCleanup}
                                    disabled={sqlStatus === "loading" || sqlStatus === "done"}
                                    className={`flex items-center gap-2 px-3 py-1.5 rounded-lg text-xs font-semibold transition-all ${
                                        sqlStatus === "done"
                                            ? "bg-emerald-500/20 text-emerald-400 cursor-default"
                                            : sqlStatus === "error"
                                            ? "bg-rose-500/20 text-rose-400 hover:bg-rose-500/30"
                                            : sqlStatus === "loading"
                                            ? "bg-white/5 text-slate-500 cursor-wait"
                                            : "bg-cyan-500/20 text-cyan-300 hover:bg-cyan-500/30"
                                    }`}
                                >
                                    {sqlStatus === "loading" ? (
                                        <><Loader2 className="w-3.5 h-3.5 animate-spin" /> Temizleniyor…</>
                                    ) : sqlStatus === "done" ? (
                                        <><CheckCircle2 className="w-3.5 h-3.5" /> Tamamlandı</>
                                    ) : (
                                        <><Trash2 className="w-3.5 h-3.5" /> SQL Loglarını Temizle</>
                                    )}
                                </button>
                            </div>
                            <p className="text-[11px] text-slate-500 leading-relaxed">
                                SQL çalışırken arşiv log dosyaları (<span className="text-slate-300 font-mono">ERRORLOG.1, ERRORLOG.2…</span>) doğrudan silinir.
                                Servis durdurulmaz, POS kesintisiz çalışır.
                                Veritabanı dosyalarına (.mdf/.ldf) <span className="text-emerald-500">kesinlikle dokunulmaz</span>.
                            </p>
                            {sqlStatus === "loading" && (
                                <p className="text-[11px] text-cyan-500/70">
                                    Arşiv log dosyaları siliniyor… (birkaç saniye)
                                </p>
                            )}
                            {sqlOutput && (
                                <pre className={`text-[11px] font-mono whitespace-pre-wrap rounded-lg p-3 border max-h-40 overflow-y-auto ${
                                    sqlStatus === "error"
                                        ? "bg-rose-500/10 border-rose-500/20 text-rose-300"
                                        : "bg-slate-900/60 border-white/5 text-slate-300"
                                }`}>
                                    {sqlOutput}
                                </pre>
                            )}
                        </div>
                    )}
                </div>
            </div>
        </div>,
        document.body
    );
};

export default DiskStatusPage;
