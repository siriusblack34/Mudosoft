import React, { useState } from "react";
import { apiClient } from "../lib/apiClient";
import {
    HardDrive, RefreshCw, Wifi, WifiOff,
    ArrowUpDown, Search, AlertTriangle, Loader2, Monitor, ShoppingCart
} from "lucide-react";

interface DiskDevice {
    deviceId: string;
    storeCode: number;
    storeName: string;
    deviceName: string;
    ipAddress: string;
    isOnline: boolean;
    status: "online" | "offline" | "error" | "unknown";
    diskCPercent: number | null;
    diskCTotalGB: number | null;
    diskCFreeGB: number | null;
    diskCUsedGB: number | null;
    diskDPercent: number | null;
    diskDTotalGB: number | null;
    diskDFreeGB: number | null;
    diskDUsedGB: number | null;
    errorMessage?: string;
}

type SortKey = "deviceName" | "storeCode" | "diskCPercent" | "diskDPercent";
type SortDir = "asc" | "desc";
type Tab = "pc" | "kasa";

const DiskStatusPage: React.FC = () => {
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

    const loadPcData = async () => {
        setPcLoading(true);
        try {
            const res = await apiClient.post<DiskDevice[]>("/api/disk-status/check-all");
            setPcDevices(res);
            setPcLastUpdated(new Date());
        } catch (err) {
            console.error("PC disk status load failed:", err);
        } finally {
            setPcLoading(false);
        }
    };

    const loadKasaData = async () => {
        setKasaLoading(true);
        try {
            const res = await apiClient.post<DiskDevice[]>("/api/disk-status/check-all-kasa");
            setKasaDevices(res);
            setKasaLastUpdated(new Date());
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
        <div className="p-6 space-y-6 bg-transparent min-h-screen text-slate-200">
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
                    {lastUpdated && (
                        <span className="text-xs text-slate-500">
                            Son kontrol: {lastUpdated.toLocaleTimeString('tr-TR')}
                        </span>
                    )}
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
                        <PcTable sorted={sorted} sortKey={sortKey} sortDir={sortDir} handleSort={handleSort} />
                    ) : (
                        <KasaTable sorted={sorted} sortKey={sortKey} sortDir={sortDir} handleSort={handleSort} />
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
        </div>
    );
};

// === PC TABLE ===
const PcTable: React.FC<{
    sorted: DiskDevice[];
    sortKey: SortKey;
    sortDir: SortDir;
    handleSort: (key: SortKey) => void;
}> = ({ sorted, sortKey, sortDir, handleSort }) => (
    <div className="glass-card border-white/5 rounded-2xl overflow-hidden shadow-xl">
        <div className="grid grid-cols-[180px_80px_80px_1fr_1fr] items-center px-6 py-4 bg-white/5 border-b border-white/5 text-xs font-semibold text-slate-300 uppercase tracking-widest">
            <SortHeader label="Cihaz" sortKey="deviceName" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <SortHeader label="Mağaza" sortKey="storeCode" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <span>Durum</span>
            <SortHeader label="C: Sürücü" sortKey="diskCPercent" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <SortHeader label="D: Sürücü" sortKey="diskDPercent" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
        </div>
        <div className="divide-y divide-slate-700/20">
            {sorted.map(device => (
                <div
                    key={device.deviceId}
                    className={`grid grid-cols-[180px_80px_80px_1fr_1fr] items-center px-6 py-4 hover:bg-slate-800/30 transition-colors ${device.status === "offline" ? "opacity-50" : ""}`}
                >
                    <DeviceCell device={device} />
                    <StoreCodeCell device={device} />
                    <StatusCell device={device} />
                    <DiskBar percent={device.diskCPercent} totalGB={device.diskCTotalGB} usedGB={device.diskCUsedGB} freeGB={device.diskCFreeGB} driveLabel="C:" offline={device.status === "offline"} />
                    <DiskBar percent={device.diskDPercent} totalGB={device.diskDTotalGB} usedGB={device.diskDUsedGB} freeGB={device.diskDFreeGB} driveLabel="D:" offline={device.status === "offline"} />
                </div>
            ))}
            {sorted.length === 0 && <div className="text-center py-12 text-slate-500">Aramanıza uygun cihaz bulunamadı</div>}
        </div>
    </div>
);

// === KASA TABLE (sadece C:) ===
const KasaTable: React.FC<{
    sorted: DiskDevice[];
    sortKey: SortKey;
    sortDir: SortDir;
    handleSort: (key: SortKey) => void;
}> = ({ sorted, sortKey, sortDir, handleSort }) => (
    <div className="glass-card border-white/5 rounded-2xl overflow-hidden shadow-xl">
        <div className="grid grid-cols-[200px_80px_80px_80px_1fr] items-center px-6 py-4 bg-white/5 border-b border-white/5 text-xs font-semibold text-slate-300 uppercase tracking-widest">
            <SortHeader label="Mağaza" sortKey="storeCode" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <SortHeader label="Mağaza No" sortKey="storeCode" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
            <span>Kasa</span>
            <span>Durum</span>
            <SortHeader label="C: Sürücü" sortKey="diskCPercent" currentSort={sortKey} dir={sortDir} onSort={handleSort} />
        </div>
        <div className="divide-y divide-slate-700/20">
            {sorted.map(device => (
                <div
                    key={device.deviceId}
                    className={`grid grid-cols-[200px_80px_80px_80px_1fr] items-center px-6 py-4 hover:bg-slate-800/30 transition-colors ${device.status === "offline" ? "opacity-50" : ""}`}
                >
                    <div className="flex flex-col">
                        <span className="text-sm font-medium text-white truncate">{device.storeName}</span>
                        <span className="text-[11px] text-slate-500 font-mono">{device.ipAddress}</span>
                    </div>
                    <StoreCodeCell device={device} />
                    <div className="text-xs text-slate-400 font-mono">{device.deviceName}</div>
                    <StatusCell device={device} />
                    <DiskBar percent={device.diskCPercent} totalGB={device.diskCTotalGB} usedGB={device.diskCUsedGB} freeGB={device.diskCFreeGB} driveLabel="C:" offline={device.status === "offline"} />
                </div>
            ))}
            {sorted.length === 0 && <div className="text-center py-12 text-slate-500">Aramanıza uygun kasa bulunamadı</div>}
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

export default DiskStatusPage;
