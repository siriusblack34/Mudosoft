import React, { useEffect, useMemo, useState } from "react";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import DeviceTabs from "../components/DeviceTabs";
import {
    Laptop, RefreshCw, Search, Wifi, WifiOff, Network, PauseCircle, PlayCircle,
    Store, Eye, EyeOff, LayoutGrid, List, Monitor, ScanSearch, Loader2,
} from "lucide-react";

/* ─── helpers ─── */
const fmtOffline = (lastSeen: string | null): string => {
    if (!lastSeen) return "Bilinmiyor";
    const m = Math.floor((Date.now() - new Date(lastSeen).getTime()) / 60000);
    if (m < 60) return `${m} dk`;
    const h = Math.floor(m / 60);
    if (h < 24) return `${h} sa`;
    return `${Math.floor(h / 24)} gun`;
};

const getOsGroup = (v: string | null): "Win7" | "Win10" | "Win11" | "Server" | "Diger" => {
    if (!v) return "Diger";
    if (v.startsWith("Win11")) return "Win11";
    if (v.startsWith("Win10")) return "Win10";
    if (v.startsWith("Win 7")) return "Win7";
    if (v.includes("Svr") || v.includes("Server")) return "Server";
    return "Diger";
};

const OS_COLORS: Record<string, string> = {
    Win11: "bg-violet-500/15 text-violet-300 border-violet-500/30",
    Win10: "bg-sky-500/15 text-sky-300 border-sky-500/30",
    Win7:  "bg-amber-500/15 text-amber-400 border-amber-500/30",
    Server:"bg-emerald-500/15 text-emerald-300 border-emerald-500/30",
    Diger: "bg-slate-500/15 text-slate-400 border-slate-500/30",
};

const WinBadge: React.FC<{ version: string | null }> = ({ version }) => {
    if (!version) return <span className="text-[9px] text-slate-600 font-mono">—</span>;
    const group = getOsGroup(version);
    return (
        <span className={`inline-flex items-center px-1.5 py-0.5 rounded text-[9px] font-bold border ${OS_COLORS[group]}`}>
            {version}
        </span>
    );
};

/* ─── CSS (once) ─── */
const STYLE_ID = "ms-pc-breathe";
if (typeof document !== "undefined" && !document.getElementById(STYLE_ID)) {
    const s = document.createElement("style");
    s.id = STYLE_ID;
    s.textContent = `
        @keyframes ms-breathe {
            0%,100% { box-shadow: 0 0 6px rgba(16,185,129,0.4); }
            50%     { box-shadow: 0 0 18px rgba(16,185,129,0.8); }
        }
        .ms-breathe { animation: ms-breathe 3s ease-in-out infinite; }
    `;
    document.head.appendChild(s);
}

type OsFilter = "all" | "Win11" | "Win10" | "Win7" | "Server" | "Diger";
type ViewMode = "grid" | "list";

/* ═══════════════════════════════════════════
   MAIN PAGE
   ═══════════════════════════════════════════ */
const BilgisayarlarPage: React.FC = () => {
    const [devices, setDevices] = useState<SqlDeviceWithStatus[]>([]);
    const [search, setSearch] = useState("");
    const [isLoading, setIsLoading] = useState(true);
    const [lastUpdated, setLastUpdated] = useState<Date>(new Date());
    const [statusFilter, setStatusFilter] = useState<"all" | "online" | "offline" | "closed">("all");
    const [osFilter, setOsFilter] = useState<OsFilter>("all");
    const [showClosed, setShowClosed] = useState(true);
    const [viewMode, setViewMode] = useState<ViewMode>("grid");
    const [scanning, setScanning] = useState(false);

    /* ─── data load ─── */
    useEffect(() => {
        let alive = true;
        const load = async (silent = false) => {
            try {
                if (!silent) setIsLoading(true);
                const data = await apiClient.getSqlDevicesWithStatus({ timeoutMs: 500, maxConcurrency: 40 });
                if (alive) {
                    const t = (data ?? []).filter(d => {
                        const dt = d.deviceType?.toUpperCase();
                        return dt === "PC" || dt === "GECICI";
                    });
                    setDevices(t);
                    setLastUpdated(new Date());
                }
            } catch (err) { console.error("PC load failed:", err); }
            finally { if (alive && !silent) setIsLoading(false); }
        };
        load();
        const t = setInterval(() => load(true), 30000);
        return () => { alive = false; clearInterval(t); };
    }, []);

    /* ─── toggle close ─── */
    const handleToggleClose = async (deviceId: string, isClosed: boolean, reason?: string) => {
        try {
            await apiClient.toggleTemporaryClose(deviceId, isClosed, reason);
            setDevices(prev => prev.map(d =>
                d.deviceId === deviceId
                    ? { ...d, isTemporarilyClosed: isClosed, temporaryCloseReason: isClosed ? (reason || null) : null }
                    : d
            ));
        } catch { alert("Durum guncellenirken hata olustu."); }
    };

    /* ─── scan windows versions ─── */
    const handleScanVersions = async () => {
        setScanning(true);
        try {
            await apiClient.post("/api/sqlquery/refresh-windows-versions", undefined, 200_000);
            // Reload to get updated versions
            const data = await apiClient.getSqlDevicesWithStatus({ timeoutMs: 500, maxConcurrency: 40 });
            const t = (data ?? []).filter(d => {
                const dt = d.deviceType?.toUpperCase();
                return dt === "PC" || dt === "GECICI";
            });
            setDevices(t);
        } catch (err) { console.error("Scan versions failed:", err); }
        finally { setScanning(false); }
    };

    /* ─── derived ─── */
    const onlineCount = devices.filter(d => d.isOnline && !d.isTemporarilyClosed).length;
    const offlineCount = devices.filter(d => !d.isOnline && !d.isTemporarilyClosed).length;
    const closedCount = devices.filter(d => d.isTemporarilyClosed).length;

    // OS group counts
    const osCounts = useMemo(() => {
        const counts: Record<OsFilter, number> = { all: 0, Win11: 0, Win10: 0, Win7: 0, Server: 0, Diger: 0 };
        devices.forEach(d => {
            counts.all++;
            const g = getOsGroup(d.windowsVersion) as OsFilter;
            counts[g] = (counts[g] || 0) + 1;
        });
        return counts;
    }, [devices]);

    const filtered = useMemo(() => {
        let list = [...devices].sort((a, b) => (a.storeCode ?? 0) - (b.storeCode ?? 0));
        const q = search.trim().toLowerCase();
        if (q) list = list.filter(d =>
            d.storeName.toLowerCase().includes(q) ||
            String(d.storeCode).includes(q) ||
            d.calculatedIpAddress.includes(q) ||
            d.deviceName.toLowerCase().includes(q) ||
            (d.windowsVersion ?? "").toLowerCase().includes(q)
        );
        if (statusFilter === "online") list = list.filter(d => d.isOnline && !d.isTemporarilyClosed);
        if (statusFilter === "offline") list = list.filter(d => !d.isOnline && !d.isTemporarilyClosed);
        if (statusFilter === "closed") list = list.filter(d => d.isTemporarilyClosed);
        if (!showClosed) list = list.filter(d => !d.isTemporarilyClosed);
        if (osFilter !== "all") list = list.filter(d => getOsGroup(d.windowsVersion) === osFilter);
        return list;
    }, [devices, search, statusFilter, osFilter, showClosed]);

    const scannedCount = devices.filter(d => d.windowsVersion).length;

    if (isLoading) return <div className="flex h-[80vh] items-center justify-center"><Spinner size="lg" /></div>;

    return (
        <div className="p-6 h-[calc(100vh-2rem)] flex flex-col gap-4 max-w-[1920px] mx-auto w-full">
            <DeviceTabs />

            {/* ═══ Header ═══ */}
            <div className="flex items-center justify-between">
                <h1 className="text-2xl font-bold text-white flex items-center gap-3">
                    <Laptop className="w-7 h-7 text-sky-500" />
                    Bilgisayarlar
                </h1>
                <div className="flex items-center gap-2">
                    {/* Windows version scan button */}
                    <button
                        onClick={handleScanVersions}
                        disabled={scanning}
                        title="Tüm PC'lerin Windows sürümünü SMB üzerinden tara"
                        className="flex items-center gap-2 px-3 py-2 bg-violet-500/10 hover:bg-violet-500/20 disabled:opacity-50 border border-violet-500/20 rounded-xl text-xs font-medium text-violet-300 transition-colors"
                    >
                        {scanning ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <ScanSearch className="w-3.5 h-3.5" />}
                        {scanning ? "Taranıyor…" : `Win Sürüm Tara ${scannedCount > 0 ? `(${scannedCount}/${devices.length})` : ""}`}
                    </button>

                    {/* View mode toggle */}
                    <div className="flex items-center bg-slate-900/60 border border-slate-700/50 rounded-xl p-1">
                        <button onClick={() => setViewMode("grid")}
                            className={`p-1.5 rounded-lg transition-colors ${viewMode === "grid" ? "bg-sky-500/20 text-sky-400" : "text-slate-500 hover:text-white"}`}
                            title="Kart görünümü">
                            <LayoutGrid className="w-4 h-4" />
                        </button>
                        <button onClick={() => setViewMode("list")}
                            className={`p-1.5 rounded-lg transition-colors ${viewMode === "list" ? "bg-sky-500/20 text-sky-400" : "text-slate-500 hover:text-white"}`}
                            title="Liste görünümü">
                            <List className="w-4 h-4" />
                        </button>
                    </div>

                    <div className="flex items-center gap-3 text-xs text-slate-400 font-mono bg-slate-900/40 px-4 py-2 rounded-xl border border-slate-700/50">
                        Son: {lastUpdated.toLocaleTimeString("tr-TR")}
                        <button onClick={() => window.location.reload()} className="p-1.5 bg-slate-800 hover:bg-slate-700 rounded-lg border border-slate-700/50" title="Yenile">
                            <RefreshCw className="w-3.5 h-3.5 text-sky-500" />
                        </button>
                    </div>
                </div>
            </div>

            {/* ═══ Stats ═══ */}
            <div className="grid grid-cols-3 gap-4">
                <StatCard icon={<Wifi />} label="Online" count={onlineCount} total={devices.length} color="emerald" />
                <StatCard icon={<WifiOff />} label="Offline" count={offlineCount} total={devices.length} color="rose" />
                <StatCard icon={<PauseCircle />} label="Gecici Kapali" count={closedCount} total={devices.length} color="amber" />
            </div>

            {/* ═══ Filters ═══ */}
            <div className="flex flex-col gap-2 bg-slate-900/60 p-3 rounded-xl border border-slate-700/50">
                {/* Row 1: Search + status filters */}
                <div className="flex gap-3 items-center">
                    <div className="flex-1 relative">
                        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
                        <input
                            type="text" placeholder="Magaza adi, kod, IP veya Windows sürüm ara..."
                            value={search} onChange={e => setSearch(e.target.value)}
                            className="w-full pl-10 pr-4 py-2.5 bg-slate-800/50 border border-slate-600/50 rounded-lg text-white placeholder-slate-500 text-sm focus:outline-none focus:border-sky-500/50"
                        />
                    </div>
                    {(["all", "online", "offline", "closed"] as const).map(f => (
                        <button key={f} onClick={() => setStatusFilter(f)}
                            className={`px-3 py-2 rounded-lg text-xs font-bold transition-colors ${statusFilter === f ? "bg-sky-500/20 text-sky-400 border border-sky-500/30" : "text-slate-400 hover:text-white hover:bg-slate-800 border border-transparent"}`}>
                            {f === "all" ? "Hepsi" : f === "online" ? "Online" : f === "offline" ? "Offline" : "Kapali"}
                        </button>
                    ))}
                    <button onClick={() => setShowClosed(!showClosed)} className="p-2 rounded-lg text-slate-400 hover:text-white hover:bg-slate-800 transition-colors">
                        {showClosed ? <Eye className="w-4 h-4" /> : <EyeOff className="w-4 h-4" />}
                    </button>
                    <div className="text-xs text-slate-500 font-mono shrink-0">{filtered.length} Cihaz</div>
                </div>

                {/* Row 2: OS version filters */}
                <div className="flex gap-2 items-center">
                    <Monitor className="w-3.5 h-3.5 text-slate-500 shrink-0" />
                    {(["all", "Win11", "Win10", "Win7", "Server", "Diger"] as OsFilter[]).map(f => (
                        <button key={f} onClick={() => setOsFilter(f)}
                            className={`px-2.5 py-1 rounded-lg text-[11px] font-semibold transition-colors border ${osFilter === f
                                ? f === "all" ? "bg-slate-600/30 text-white border-slate-500/50"
                                    : OS_COLORS[f] + " !border-opacity-100"
                                : "text-slate-500 border-transparent hover:bg-slate-800 hover:text-slate-300"
                            }`}>
                            {f === "all" ? "Tüm Sürümler" : f === "Diger" ? "Bilinmiyor" : f}
                            {f !== "all" && <span className="ml-1 opacity-60">({osCounts[f] ?? 0})</span>}
                        </button>
                    ))}
                </div>
            </div>

            {/* ═══ Content ═══ */}
            <div className="flex-1 overflow-auto min-h-0 scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent">
                {filtered.length === 0 ? (
                    <div className="flex flex-col items-center justify-center h-64 text-slate-500">
                        <Search className="w-16 h-16 mb-4 opacity-20" />
                        <p className="text-lg font-medium text-slate-400">Sonuc bulunamadi.</p>
                    </div>
                ) : viewMode === "grid" ? (
                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4 pb-4">
                        {filtered.map(d => (
                            <PcCard key={d.deviceId} device={d} onToggleClose={handleToggleClose} />
                        ))}
                    </div>
                ) : (
                    <PcTable devices={filtered} onToggleClose={handleToggleClose} />
                )}
            </div>
        </div>
    );
};

/* ═══════════════════════════════════════════
   STAT CARD
   ═══════════════════════════════════════════ */
const StatCard: React.FC<{ icon: React.ReactNode; label: string; count: number; total: number; color: string }> = ({ icon, label, count, total, color }) => {
    const colors: Record<string, { bg: string; border: string; text: string; icon: string }> = {
        emerald: { bg: "from-emerald-900/30 to-emerald-900/10", border: "border-emerald-500/20", text: "text-emerald-400", icon: "bg-emerald-500/10 border-emerald-500/20" },
        rose:    { bg: "from-rose-900/30 to-rose-900/10", border: "border-rose-500/20", text: "text-rose-400", icon: "bg-rose-500/10 border-rose-500/20" },
        amber:   { bg: "from-amber-900/30 to-amber-900/10", border: "border-amber-500/20", text: "text-amber-400", icon: "bg-amber-500/10 border-amber-500/20" },
    };
    const c = colors[color] ?? colors.emerald;
    return (
        <div className={`bg-gradient-to-br ${c.bg} border ${c.border} rounded-2xl p-4 flex items-center gap-4 shadow-lg`}>
            <div className={`p-3 rounded-xl border ${c.icon}`}>
                {React.cloneElement(icon as React.ReactElement, { className: `w-6 h-6 ${c.text}` })}
            </div>
            <div>
                <div className={`text-2xl font-bold ${c.text}`}>{count}</div>
                <div className="text-xs text-slate-400">{label} / {total} toplam</div>
            </div>
        </div>
    );
};

/* ═══════════════════════════════════════════
   PC CARD (grid view)
   ═══════════════════════════════════════════ */
const PcCard: React.FC<{ device: SqlDeviceWithStatus; onToggleClose: (id: string, closed: boolean, reason?: string) => void }> = ({ device, onToggleClose }) => {
    const [showDialog, setShowDialog] = useState(false);
    const [reason, setReason] = useState("");
    const d = device;
    const isClosed = d.isTemporarilyClosed;
    const isOn = d.isOnline && !isClosed;
    const isOff = !d.isOnline && !isClosed;
    const isGecici = d.deviceType?.toUpperCase() === "GECICI";

    return (
        <div className={`relative rounded-2xl border p-4 flex flex-col gap-3 transition-all hover:-translate-y-0.5 hover:shadow-lg
            ${isClosed ? "border-amber-500/30 bg-slate-800/40" : isGecici ? "border-violet-500/30 bg-slate-800/60 border-dashed" : isOn ? "border-slate-700 bg-slate-800/60" : "border-rose-500/30 bg-slate-800/60"}`}>
            <div className={`absolute top-0 left-0 w-full h-1 rounded-t-2xl ${isClosed ? "bg-amber-500" : isGecici ? "bg-violet-500" : isOn ? "bg-emerald-500" : "bg-rose-500"}`} />

            <div className="flex items-start justify-between gap-2">
                <div className="flex items-center gap-3 min-w-0">
                    <div className={`w-3 h-3 shrink-0 rounded-full
                        ${isClosed ? "bg-amber-500 shadow-[0_0_8px_rgba(245,158,11,0.6)]"
                            : isOn ? "bg-emerald-500 ms-breathe"
                            : "bg-rose-500 animate-pulse shadow-[0_0_8px_rgba(225,29,72,0.8)]"}`}
                    />
                    <div className="min-w-0">
                        <div className="text-sm font-bold text-white truncate flex items-center gap-1.5">
                            [{d.storeCode}] {d.storeName}
                            {isGecici && <span className="shrink-0 px-1.5 py-0.5 text-[8px] font-bold uppercase tracking-wider bg-violet-500/15 text-violet-400 border border-violet-500/25 rounded">Geçici</span>}
                        </div>
                        <div className="text-[11px] text-slate-400 font-mono">{d.calculatedIpAddress}</div>
                    </div>
                </div>
                <div className="flex items-center gap-1 shrink-0">
                    {isClosed ? (
                        <button onClick={() => onToggleClose(d.deviceId, false)}
                            className="p-1.5 text-amber-500 hover:text-emerald-400 hover:bg-emerald-500/10 rounded-lg transition-all" title="Tekrar Ac">
                            <PlayCircle className="w-4 h-4" />
                        </button>
                    ) : (
                        <button onClick={() => setShowDialog(true)}
                            className="p-1.5 text-slate-500 hover:text-amber-400 hover:bg-amber-500/10 rounded-lg transition-all" title="Gecici Kapat">
                            <PauseCircle className="w-4 h-4" />
                        </button>
                    )}
                </div>
            </div>

            <div className="flex items-center gap-2 flex-wrap">
                {isClosed ? (
                    <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded text-[10px] font-bold bg-amber-500/10 text-amber-400 border border-amber-500/20">
                        <PauseCircle className="w-3 h-3" /> GECICI KAPALI
                    </span>
                ) : isOn ? (
                    <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded text-[10px] font-bold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                        <Wifi className="w-3 h-3" /> ONLINE
                    </span>
                ) : (
                    <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded text-[10px] font-bold bg-rose-500/10 text-rose-400 border border-rose-500/20">
                        <WifiOff className="w-3 h-3" /> OFFLINE
                    </span>
                )}
                <WinBadge version={d.windowsVersion} />
                {isClosed && d.temporaryCloseReason && (
                    <span className="text-[10px] text-amber-400/70 truncate" title={d.temporaryCloseReason}>{d.temporaryCloseReason}</span>
                )}
                {isOff && (
                    <span className="text-[10px] text-rose-400/70 font-mono">{fmtOffline(d.lastSeen)}</span>
                )}
            </div>

            <div className="space-y-1.5 text-[11px]">
                <div className="flex items-center gap-2 text-slate-400">
                    <Network className="w-3 h-3 shrink-0" />
                    <span className="font-mono">{d.deviceName}</span>
                </div>
                <div className="flex items-center gap-2 text-slate-400">
                    <Store className="w-3 h-3 shrink-0" />
                    <span>Magaza {d.storeCode}</span>
                </div>
            </div>

            {showDialog && (
                <div className="p-3 bg-amber-500/5 border border-amber-500/20 rounded-xl space-y-2">
                    <div className="text-xs font-bold text-amber-400">Kapatma Sebebi (Opsiyonel)</div>
                    <input type="text" placeholder="Tadilat, tasima, ariza..." value={reason} onChange={e => setReason(e.target.value)}
                        className="w-full px-3 py-2 bg-slate-900/60 border border-slate-700 rounded-lg text-sm text-white placeholder-slate-500 focus:outline-none focus:border-amber-500/50" />
                    <div className="flex gap-2">
                        <button onClick={() => { onToggleClose(d.deviceId, true, reason || undefined); setShowDialog(false); setReason(""); }}
                            className="flex-1 px-3 py-1.5 bg-amber-500/20 text-amber-400 text-xs font-bold rounded-lg hover:bg-amber-500/30">Onayla</button>
                        <button onClick={() => { setShowDialog(false); setReason(""); }}
                            className="px-3 py-1.5 bg-slate-700/50 text-slate-400 text-xs font-bold rounded-lg hover:bg-slate-700">Iptal</button>
                    </div>
                </div>
            )}
        </div>
    );
};

/* ═══════════════════════════════════════════
   PC TABLE (list view)
   ═══════════════════════════════════════════ */
const PcTable: React.FC<{
    devices: SqlDeviceWithStatus[];
    onToggleClose: (id: string, closed: boolean, reason?: string) => void;
}> = ({ devices, onToggleClose }) => (
    <div className="rounded-2xl border border-slate-700/50 overflow-hidden bg-slate-900/40">
        {/* Header */}
        <div className="grid grid-cols-[2fr_1fr_1fr_1fr_1fr_1fr_44px] items-center px-4 py-3 bg-slate-800/60 border-b border-slate-700/50 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">
            <span>Mağaza</span>
            <span>Mağaza No</span>
            <span>Cihaz</span>
            <span>IP</span>
            <span>Durum</span>
            <span>Windows</span>
            <span />
        </div>
        {/* Rows */}
        <div className="divide-y divide-slate-700/30">
            {devices.map(d => {
                const isClosed = d.isTemporarilyClosed;
                const isOn = d.isOnline && !isClosed;
                const isOff = !d.isOnline && !isClosed;
                return (
                    <div key={d.deviceId}
                        className={`grid grid-cols-[2fr_1fr_1fr_1fr_1fr_1fr_44px] items-center px-4 py-3 text-sm hover:bg-slate-800/30 transition-colors
                            ${isClosed ? "opacity-60" : ""}`}>
                        <div className="flex items-center gap-2 min-w-0">
                            <div className={`w-2 h-2 rounded-full shrink-0 ${isClosed ? "bg-amber-500" : isOn ? "bg-emerald-500 ms-breathe" : "bg-rose-500 animate-pulse"}`} />
                            <span className="text-white font-medium truncate">{d.storeName}</span>
                            {d.deviceType?.toUpperCase() === "GECICI" && (
                                <span className="shrink-0 px-1 py-0.5 text-[8px] font-bold uppercase bg-violet-500/15 text-violet-400 border border-violet-500/25 rounded">Geçici</span>
                            )}
                        </div>
                        <span className="text-slate-400 font-mono">{d.storeCode}</span>
                        <span className="text-slate-300 font-mono text-xs">{d.deviceName}</span>
                        <span className="text-slate-400 font-mono text-xs">{d.calculatedIpAddress}</span>
                        <div>
                            {isClosed ? (
                                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold bg-amber-500/10 text-amber-400 border border-amber-500/20">
                                    <PauseCircle className="w-3 h-3" /> Kapalı
                                </span>
                            ) : isOn ? (
                                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                                    <Wifi className="w-3 h-3" /> Online
                                </span>
                            ) : (
                                <div>
                                    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold bg-rose-500/10 text-rose-400 border border-rose-500/20">
                                        <WifiOff className="w-3 h-3" /> Offline
                                    </span>
                                    {isOff && <div className="text-[10px] text-rose-400/60 font-mono mt-0.5">{fmtOffline(d.lastSeen)}</div>}
                                </div>
                            )}
                        </div>
                        <div><WinBadge version={d.windowsVersion} /></div>
                        <div className="flex justify-center">
                            {isClosed ? (
                                <button onClick={() => onToggleClose(d.deviceId, false)}
                                    className="p-1.5 text-amber-500 hover:text-emerald-400 hover:bg-emerald-500/10 rounded-lg transition-all" title="Tekrar Ac">
                                    <PlayCircle className="w-3.5 h-3.5" />
                                </button>
                            ) : (
                                <button onClick={() => onToggleClose(d.deviceId, true)}
                                    className="p-1.5 text-slate-600 hover:text-amber-400 hover:bg-amber-500/10 rounded-lg transition-all" title="Gecici Kapat">
                                    <PauseCircle className="w-3.5 h-3.5" />
                                </button>
                            )}
                        </div>
                    </div>
                );
            })}
        </div>
    </div>
);

export default BilgisayarlarPage;
