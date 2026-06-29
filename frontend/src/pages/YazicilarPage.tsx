import React, { useEffect, useMemo, useState } from "react";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import {
    Printer, RefreshCw, Search, Wifi, WifiOff, Store, Network,
    PauseCircle, PlayCircle, Trash2, Zap, X, AlertTriangle, Hash
} from "lucide-react";
import DeviceTabs from "../components/DeviceTabs";
import { useAuth } from "../contexts/AuthContext";

interface OfflinePrinterRow {
    storeCode: number;
    storeName: string;
    kasaNo: number;
    printerIp: string;
    printerSerialNumber: string | null;
    lastSeen: string | null;
}

interface PrinterStoreRow {
    storeCode: number;
    storeName: string;
    yazici1: SqlDeviceWithStatus | null;
    yazici2: SqlDeviceWithStatus | null;
    yazici3: SqlDeviceWithStatus | null;
}

const formatOfflineDuration = (lastSeen: string | null): string => {
    if (!lastSeen) return "Bilinmiyor";
    const diff = Date.now() - new Date(lastSeen).getTime();
    const minutes = Math.floor(diff / 60000);
    if (minutes < 60) return `${minutes} dakikadır offline`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours} saattir offline`;
    const days = Math.floor(hours / 24);
    return `${days} gündür offline`;
};

const PrinterCard = ({
    title,
    device,
    onDelete,
    onToggleClose,
}: {
    title: string;
    device: SqlDeviceWithStatus | null;
    onDelete?: (deviceId: string) => void;
    onToggleClose?: (deviceId: string, isClosed: boolean, reason?: string) => void;
}) => {
    const [showCloseDialog, setShowCloseDialog] = useState(false);
    const [closeReason, setCloseReason] = useState("");

    if (!device) {
        return (
            <div className="bg-slate-800/30 border border-slate-700/50 border-dashed rounded-2xl p-6 flex flex-col items-center justify-center text-slate-500 min-h-[180px]">
                <Printer className="w-10 h-10 mb-3 opacity-30" />
                <div className="font-semibold">{title}</div>
                <div className="text-xs mt-1">Sistemde Bulunamadı</div>
            </div>
        );
    }

    return (
        <div className={`bg-slate-800/60 border rounded-2xl p-5 flex flex-col hover:border-slate-500/50 transition-all shadow-lg shadow-black/20 relative overflow-hidden group h-full ${device.isTemporarilyClosed ? "border-amber-500/30" : "border-slate-700"}`}>
            <div className={`absolute top-0 left-0 w-full h-1 ${device.isTemporarilyClosed ? "bg-amber-500" : device.isOnline ? "bg-violet-500" : "bg-rose-500 shadow-[0_0_15px_rgba(225,29,72,0.8)]"}`} />

            <div className="flex justify-between items-start mb-5 pb-4 border-b border-slate-700/50">
                <div className="pr-2">
                    <h3 className="text-lg font-bold text-white mb-1 tracking-wide">{title}</h3>
                    <div className="text-xs text-slate-400 font-mono" title={device.deviceName}>{device.deviceName || "Bilinmiyor"}</div>
                </div>
                <div className="flex flex-col items-end gap-1.5 shrink-0">
                    {device.isTemporarilyClosed ? (
                        <div className="flex items-center gap-1.5 px-2 py-1 rounded-md text-[10px] font-bold tracking-widest border bg-amber-500/10 text-amber-400 border-amber-500/20">
                            <PauseCircle className="w-3 h-3" />
                            GEÇİCİ KAPALI
                        </div>
                    ) : (
                        <div className={`flex items-center gap-1.5 px-2 py-1 rounded-md text-[10px] font-bold tracking-widest border ${device.isOnline
                            ? "bg-violet-500/10 text-violet-400 border-violet-500/20"
                            : "bg-rose-500/10 text-rose-400 border-rose-500/20"
                            }`}>
                            <div className={`w-1.5 h-1.5 rounded-full ${device.isOnline ? "bg-violet-500" : "bg-rose-500 animate-pulse"}`} />
                            {device.isOnline ? "ONLINE" : "OFFLINE"}
                        </div>
                    )}
                    {device.isTemporarilyClosed && device.temporaryCloseReason && (
                        <div className="text-[10px] text-amber-400/70 font-mono max-w-[140px] truncate" title={device.temporaryCloseReason}>
                            {device.temporaryCloseReason}
                        </div>
                    )}
                    {!device.isTemporarilyClosed && !device.isOnline && (
                        <div className="text-[10px] text-rose-400/70 font-mono">
                            {formatOfflineDuration(device.lastSeen)}
                        </div>
                    )}
                    <div className="flex items-center gap-1">
                        {onToggleClose && (
                            device.isTemporarilyClosed ? (
                                <button onClick={() => onToggleClose(device.deviceId, false)}
                                    className="p-1.5 text-amber-500 hover:text-violet-400 hover:bg-violet-500/10 rounded-lg transition-all" title="Tekrar Aç">
                                    <PlayCircle className="w-4 h-4" />
                                </button>
                            ) : (
                                <button onClick={() => setShowCloseDialog(true)}
                                    className="p-1.5 text-slate-500 hover:text-amber-400 hover:bg-amber-500/10 rounded-lg transition-all" title="Geçici Kapalı Olarak İşaretle">
                                    <PauseCircle className="w-4 h-4" />
                                </button>
                            )
                        )}
                        {!device.isOnline && !device.isTemporarilyClosed && onDelete && (
                            <button onClick={() => {
                                if (confirm(`"${device.deviceName}" yazıcısını envanterden silmek istediğinize emin misiniz?`))
                                    onDelete(device.deviceId);
                            }}
                                className="p-1.5 text-slate-500 hover:text-rose-400 hover:bg-rose-500/10 rounded-lg transition-all" title="Cihazı Kaldır">
                                <Trash2 className="w-4 h-4" />
                            </button>
                        )}
                    </div>
                </div>
            </div>

            {showCloseDialog && (
                <div className="mb-4 p-3 bg-amber-500/5 border border-amber-500/20 rounded-xl space-y-2">
                    <div className="text-xs font-bold text-amber-400">Kapatma Sebebi (Opsiyonel)</div>
                    <input type="text" placeholder="Arıza, değişim..."
                        value={closeReason} onChange={e => setCloseReason(e.target.value)}
                        className="w-full px-3 py-2 bg-slate-900/60 border border-slate-700 rounded-lg text-sm text-white placeholder-slate-500 focus:outline-none focus:border-amber-500/50"
                    />
                    <div className="flex gap-2">
                        <button onClick={() => { onToggleClose?.(device.deviceId, true, closeReason || undefined); setShowCloseDialog(false); setCloseReason(""); }}
                            className="flex-1 px-3 py-1.5 bg-amber-500/20 text-amber-400 text-xs font-bold rounded-lg hover:bg-amber-500/30 transition-colors">
                            Onayla
                        </button>
                        <button onClick={() => { setShowCloseDialog(false); setCloseReason(""); }}
                            className="px-3 py-1.5 bg-slate-700/50 text-slate-400 text-xs font-bold rounded-lg hover:bg-slate-700 transition-colors">
                            İptal
                        </button>
                    </div>
                </div>
            )}

            <div className="space-y-3 flex-1">
                <div className="flex items-center gap-3 p-2.5 rounded-xl bg-slate-900/50 border border-slate-700/50">
                    <div className="p-1.5 bg-slate-800 rounded-md text-slate-400"><Network className="w-4 h-4" /></div>
                    <div>
                        <div className="text-[10px] text-slate-500 uppercase tracking-wider font-semibold">IP Adresi</div>
                        <div className="text-sm text-slate-200 font-mono mt-0.5">{device.calculatedIpAddress || "N/A"}</div>
                    </div>
                </div>

                {device.lastSeen && (
                    <div className="flex items-center gap-3 p-2.5 rounded-xl bg-slate-900/50 border border-slate-700/50">
                        <div className="p-1.5 bg-slate-800 rounded-md text-slate-400"><Printer className="w-4 h-4" /></div>
                        <div>
                            <div className="text-[10px] text-slate-500 uppercase tracking-wider font-semibold">Son Görülme</div>
                            <div className="text-sm text-slate-200 font-mono mt-0.5">
                                {new Date(device.lastSeen).toLocaleString("tr-TR")}
                            </div>
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
};

function readSharedSqlCache(): SqlDeviceWithStatus[] | null {
    try {
        const raw = localStorage.getItem("ms_sql_devices_cache_v4");
        if (!raw) return null;
        const parsed = JSON.parse(raw) as { savedAt?: string; items?: SqlDeviceWithStatus[] } | SqlDeviceWithStatus[];
        const items = Array.isArray(parsed) ? parsed : parsed.items;
        if (!Array.isArray(items) || items.length === 0) return null;
        if (!Array.isArray(parsed) && parsed.savedAt) {
            const age = Date.now() - new Date(parsed.savedAt).getTime();
            if (age > 120_000) return null;
        }
        return items;
    } catch { return null; }
}

const YazicilarPage: React.FC = () => {
    const { isAdmin } = useAuth();

    const cachedYazicilar = useMemo(() => {
        const all = readSharedSqlCache();
        return all?.filter(d => d.deviceType?.toLowerCase().startsWith("yazici")) ?? [];
    }, []);

    const [devices, setDevices] = useState<SqlDeviceWithStatus[]>(cachedYazicilar);
    const [search, setSearch] = useState("");
    const [isLoading, setIsLoading] = useState(cachedYazicilar.length === 0);
    const [lastUpdated, setLastUpdated] = useState<Date>(new Date());
    const [selectedStoreCode, setSelectedStoreCode] = useState<number | null>(null);
    const [statusFilter, setStatusFilter] = useState<"offline" | "closed" | null>(null);
    const [provisioning, setProvisioning] = useState(false);
    const [provisionResult, setProvisionResult] = useState<string | null>(null);
    const [offlineReport, setOfflineReport] = useState<OfflinePrinterRow[]>([]);
    const [reportLoading, setReportLoading] = useState(false);

    const handleDeleteDevice = async (deviceId: string) => {
        try {
            await apiClient.deleteStoreDevice(deviceId);
            setDevices(prev => prev.filter(d => d.deviceId !== deviceId));
        } catch {
            alert("Cihaz silinirken hata oluştu.");
        }
    };

    const handleToggleClose = async (deviceId: string, isClosed: boolean, reason?: string) => {
        try {
            await apiClient.toggleTemporaryClose(deviceId, isClosed, reason);
            setDevices(prev => prev.map(d =>
                d.deviceId === deviceId
                    ? { ...d, isTemporarilyClosed: isClosed, temporaryCloseReason: isClosed ? (reason || null) : null }
                    : d
            ));
        } catch {
            alert("Durum güncellenirken hata oluştu.");
        }
    };

    const handleProvision = async () => {
        if (!confirm("Tüm mağazalar için yazıcı cihazları oluşturulsun mu?\n(Zaten var olanlar atlanır)")) return;
        setProvisioning(true);
        setProvisionResult(null);
        try {
            const res = await apiClient.post<{ added: number; skipped: number; total: number }>(
                "/api/store-devices/provision-printers", undefined
            );
            setProvisionResult(`${res.added} yazıcı eklendi, ${res.skipped} zaten mevcuttu.`);
            // Reload
            const data = await apiClient.getSqlDevicesWithStatus({ timeoutMs: 500, maxConcurrency: 40 });
            setDevices((data ?? []).filter(d => d.deviceType?.toLowerCase().startsWith("yazici")));
            setLastUpdated(new Date());
        } catch (err: any) {
            setProvisionResult(`Hata: ${err?.message || "Bilinmiyor"}`);
        } finally {
            setProvisioning(false);
        }
    };

    useEffect(() => {
        let isMounted = true;
        const load = async (silent = false) => {
            try {
                if (!silent && devices.length === 0) setIsLoading(true);
                const data = await apiClient.getSqlDevicesWithStatus({ timeoutMs: 500, maxConcurrency: 40 });
                if (isMounted) {
                    setDevices((data ?? []).filter(d => d.deviceType?.toLowerCase().startsWith("yazici")));
                    setLastUpdated(new Date());
                }
            } catch (err) {
                console.error("Yazıcı devices load failed:", err);
            } finally {
                if (isMounted) setIsLoading(false);
            }
        };
        load(cachedYazicilar.length > 0);
        const intervalId = setInterval(() => load(true), 30000);
        return () => { isMounted = false; clearInterval(intervalId); };
    }, []);

    // Offline raporu yükle
    useEffect(() => {
        if (devices.length === 0) return;
        setReportLoading(true);
        apiClient.get<OfflinePrinterRow[]>("/api/yazicilar/offline-report")
            .then(data => setOfflineReport(data ?? []))
            .catch(() => setOfflineReport([]))
            .finally(() => setReportLoading(false));
    }, [devices]);

    const storeRows = useMemo(() => {
        const storeMap = new Map<number, PrinterStoreRow>();
        devices.forEach(d => {
            if (!storeMap.has(d.storeCode)) {
                storeMap.set(d.storeCode, { storeCode: d.storeCode, storeName: d.storeName, yazici1: null, yazici2: null, yazici3: null });
            }
            const row = storeMap.get(d.storeCode)!;
            const type = d.deviceType?.toLowerCase() || "";
            if (type === "yazici-1") row.yazici1 = d;
            else if (type === "yazici-2") row.yazici2 = d;
            else if (type === "yazici-3") row.yazici3 = d;
        });
        return Array.from(storeMap.values()).sort((a, b) => a.storeCode - b.storeCode);
    }, [devices]);

    const filteredRows = useMemo(() => {
        let rows = storeRows;
        const s = search.trim().toLowerCase();
        if (s) rows = rows.filter(r => r.storeName.toLowerCase().includes(s) || String(r.storeCode).includes(s));
        if (statusFilter) {
            const yazicilar = (r: PrinterStoreRow) => [r.yazici1, r.yazici2, r.yazici3];
            const hasMatch = (r: PrinterStoreRow) => {
                if (statusFilter === "offline") return yazicilar(r).some(y => y && !y.isOnline && !y.isTemporarilyClosed);
                if (statusFilter === "closed") return yazicilar(r).some(y => y && y.isTemporarilyClosed);
                return false;
            };
            rows = [...rows].sort((a, b) => (hasMatch(a) ? 0 : 1) - (hasMatch(b) ? 0 : 1) || a.storeCode - b.storeCode);
        }
        return rows;
    }, [storeRows, search, statusFilter]);

    const selectedStore = useMemo(() =>
        selectedStoreCode === null ? null : storeRows.find(r => r.storeCode === selectedStoreCode) || null,
        [selectedStoreCode, storeRows]
    );

    const closedCount = devices.filter(d => d.isTemporarilyClosed).length;
    const onlineCount = devices.filter(d => d.isOnline && !d.isTemporarilyClosed).length;
    const offlineCount = devices.length - onlineCount - closedCount;

    if (isLoading) {
        return <div className="flex h-[80vh] items-center justify-center"><Spinner size="lg" /></div>;
    }

    const Led = ({ device }: { device: SqlDeviceWithStatus | null }) => {
        if (!device) return <div className="w-3.5 h-3.5 rounded-full bg-slate-700/40 mx-auto" />;
        if (device.isTemporarilyClosed) {
            return <div title={`${device.calculatedIpAddress} — Geçici Kapalı${device.temporaryCloseReason ? `: ${device.temporaryCloseReason}` : ""}`}
                className="w-3.5 h-3.5 rounded-full mx-auto bg-amber-500 shadow-[0_0_8px_rgba(245,158,11,0.6)]" />;
        }
        const tooltip = device.isOnline ? device.calculatedIpAddress : `${device.calculatedIpAddress} — ${formatOfflineDuration(device.lastSeen)}`;
        return <div title={tooltip}
            className={`w-3.5 h-3.5 rounded-full mx-auto ${device.isOnline
                ? "bg-violet-500 shadow-[0_0_8px_rgba(139,92,246,0.8)]"
                : "bg-rose-500 animate-pulse shadow-[0_0_8px_rgba(225,29,72,0.8)]"}`} />;
    };

    return (
        <div className="p-6 h-[calc(100vh-2rem)] flex flex-col gap-6 max-w-[1920px] mx-auto w-full">
            <DeviceTabs />

            <div className="flex items-center justify-between">
                <h1 className="text-2xl font-bold text-white flex items-center gap-3">
                    <Printer className="w-7 h-7 text-violet-500" />
                    YAZICI Dashboard
                </h1>
                <div className="flex items-center gap-3">
                    {isAdmin && (
                        <button onClick={handleProvision} disabled={provisioning}
                            className="flex items-center gap-2 px-3 py-1.5 bg-violet-500/10 text-violet-400 border border-violet-500/20 rounded-lg text-xs font-semibold hover:bg-violet-500/20 transition-colors disabled:opacity-50">
                            <Zap className="w-3.5 h-3.5" />
                            {provisioning ? "Ekleniyor..." : "Yazıcıları Provision Et"}
                        </button>
                    )}
                    {provisionResult && (
                        <div className="flex items-center gap-2 px-3 py-1.5 bg-slate-800 border border-slate-700 rounded-lg text-xs text-slate-300">
                            {provisionResult}
                            <button onClick={() => setProvisionResult(null)} className="text-slate-500 hover:text-slate-300">
                                <X className="w-3 h-3" />
                            </button>
                        </div>
                    )}
                    <div className="flex items-center gap-4 bg-slate-900/40 px-4 py-2 rounded-xl border border-slate-700/50 shadow-sm">
                        <span className="text-xs text-slate-400 font-mono">
                            Son güncelleme: {lastUpdated.toLocaleTimeString("tr-TR")}
                        </span>
                        <button onClick={() => window.location.reload()}
                            className="p-1.5 bg-slate-800 hover:bg-slate-700 rounded-lg transition-colors border border-slate-700/50" title="Sayfayı Yenile">
                            <RefreshCw className="w-3.5 h-3.5 text-violet-500" />
                        </button>
                    </div>
                </div>
            </div>

            {/* Stats */}
            <div className="flex gap-6">
                <div className="flex-[0.3] bg-gradient-to-br from-violet-900/30 to-violet-900/10 border border-violet-500/20 rounded-2xl p-5 flex items-center gap-5 shadow-lg relative overflow-hidden group">
                    <div className="absolute top-0 right-0 w-32 h-32 bg-violet-500/5 rounded-full blur-3xl -translate-y-10 translate-x-10 group-hover:bg-violet-500/10 transition-colors" />
                    <div className="p-4 bg-violet-500/10 rounded-2xl relative z-10 border border-violet-500/20">
                        <Wifi className="w-8 h-8 text-violet-400" />
                    </div>
                    <div className="relative z-10">
                        <div className="text-sm font-bold text-violet-500 tracking-wider mb-0.5">ONLINE YAZICILAR</div>
                        <div className="text-4xl font-black text-white tabular-nums">{onlineCount}</div>
                    </div>
                </div>
                <div onClick={() => setStatusFilter(f => f === "offline" ? null : "offline")}
                    className={`flex-[0.3] bg-gradient-to-br from-rose-900/30 to-rose-900/10 border rounded-2xl p-5 flex items-center gap-5 shadow-lg relative overflow-hidden group cursor-pointer transition-all ${statusFilter === "offline" ? "border-rose-400 ring-2 ring-rose-500/30" : "border-rose-500/20"}`}>
                    <div className="absolute top-0 right-0 w-32 h-32 bg-rose-500/5 rounded-full blur-3xl -translate-y-10 translate-x-10 group-hover:bg-rose-500/10 transition-colors" />
                    <div className="p-4 bg-rose-500/10 rounded-2xl relative z-10 border border-rose-500/20">
                        <WifiOff className="w-8 h-8 text-rose-400" />
                    </div>
                    <div className="relative z-10">
                        <div className="text-sm font-bold text-rose-500 tracking-wider mb-0.5">OFFLINE</div>
                        <div className="text-4xl font-black text-white tabular-nums">{offlineCount}</div>
                    </div>
                </div>
                {closedCount > 0 && (
                    <div onClick={() => setStatusFilter(f => f === "closed" ? null : "closed")}
                        className={`flex-[0.3] bg-gradient-to-br from-amber-900/30 to-amber-900/10 border rounded-2xl p-5 flex items-center gap-5 shadow-lg relative overflow-hidden group cursor-pointer transition-all ${statusFilter === "closed" ? "border-amber-400 ring-2 ring-amber-500/30" : "border-amber-500/20"}`}>
                        <div className="absolute top-0 right-0 w-32 h-32 bg-amber-500/5 rounded-full blur-3xl -translate-y-10 translate-x-10 group-hover:bg-amber-500/10 transition-colors" />
                        <div className="p-4 bg-amber-500/10 rounded-2xl relative z-10 border border-amber-500/20">
                            <PauseCircle className="w-8 h-8 text-amber-400" />
                        </div>
                        <div className="relative z-10">
                            <div className="text-sm font-bold text-amber-500 tracking-wider mb-0.5">GEÇİCİ KAPALI</div>
                            <div className="text-4xl font-black text-white tabular-nums">{closedCount}</div>
                        </div>
                    </div>
                )}
                {(offlineReport.length > 0 || reportLoading) && (
                <div className="flex-1 bg-gradient-to-br from-rose-900/20 to-rose-900/5 border border-rose-500/20 rounded-2xl p-5 flex flex-col gap-3 shadow-lg relative overflow-hidden">
                    <div className="flex items-center gap-2 mb-1">
                        <AlertTriangle className="w-4 h-4 text-rose-400 shrink-0" />
                        <span className="text-sm font-bold text-rose-400">Offline Yazıcılar</span>
                        {!reportLoading && <span className="ml-auto text-xs text-rose-400/70 font-mono">{offlineReport.length} yazıcı</span>}
                    </div>
                    {reportLoading ? (
                        <div className="text-xs text-slate-500">Yükleniyor...</div>
                    ) : (
                        <div className="overflow-auto max-h-48 scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent">
                            <table className="w-full text-xs border-collapse">
                                <thead>
                                    <tr className="text-[10px] text-slate-500 uppercase tracking-wider">
                                        <th className="text-left pb-2 pr-3 font-bold">Kod</th>
                                        <th className="text-left pb-2 pr-3 font-bold">Mağaza</th>
                                        <th className="text-center pb-2 pr-3 font-bold">Kasa</th>
                                        <th className="text-left pb-2 pr-3 font-bold">IP</th>
                                        <th className="text-left pb-2 font-bold">Sicil No</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-800/60">
                                    {offlineReport.map((row, i) => (
                                        <tr key={i} className="hover:bg-rose-500/5 transition-colors">
                                            <td className="py-1.5 pr-3 font-mono text-slate-400">{row.storeCode}</td>
                                            <td className="py-1.5 pr-3 text-slate-200 font-medium">{row.storeName}</td>
                                            <td className="py-1.5 pr-3 text-center">
                                                <span className="px-1.5 py-0.5 bg-rose-500/10 text-rose-400 rounded text-[10px] font-bold">K{row.kasaNo}</span>
                                            </td>
                                            <td className="py-1.5 pr-3 font-mono text-slate-400">{row.printerIp}</td>
                                            <td className="py-1.5">
                                                {row.printerSerialNumber
                                                    ? <span className="flex items-center gap-1 font-mono text-slate-200"><Hash className="w-3 h-3 text-slate-500" />{row.printerSerialNumber}</span>
                                                    : <span className="text-slate-600">—</span>
                                                }
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                </div>
            )}
            {devices.length === 0 && !isLoading && (
                    <div className="flex-1 bg-slate-800/30 border border-violet-500/10 border-dashed rounded-2xl p-5 flex items-center gap-4 text-slate-400">
                        <Zap className="w-8 h-8 text-violet-500/50 shrink-0" />
                        <div>
                            <div className="font-semibold text-slate-300">Henüz yazıcı cihazı yok</div>
                            <div className="text-xs mt-1">{isAdmin ? `"Yazıcıları Provision Et" butonuna basarak mevcut kasalar için yazıcı IP'lerini otomatik ekleyin.` : "Admin bir yazıcı provision gerekiyor."}</div>
                        </div>
                    </div>
                )}
            </div>

            {/* Split Pane */}
            <div className="flex flex-1 gap-6 min-h-0">
                {/* Left Pane */}
                <div className="w-[420px] shrink-0 flex flex-col gap-4 bg-slate-900/60 rounded-2xl border border-slate-700/50 p-5 shadow-2xl relative overflow-hidden backdrop-blur-xl">
                    <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-violet-500/20 via-violet-500/40 to-violet-500/20" />

                    <div className="flex items-center gap-3 bg-slate-800/80 p-2.5 rounded-xl border border-slate-600/40 shadow-inner">
                        <div className="flex-1 relative">
                            <Search className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
                            <input type="text" placeholder="Mağaza veya kod ara..."
                                value={search} onChange={e => setSearch(e.target.value)}
                                className="w-full pl-10 pr-4 py-2.5 bg-slate-900/50 border border-slate-700/50 rounded-lg text-sm text-white placeholder-slate-500 focus:outline-none focus:border-violet-500/50 focus:ring-1 focus:ring-violet-500/50 transition-all font-medium shadow-sm"
                            />
                        </div>
                        <div className="px-4 py-2 bg-slate-900 rounded-lg border border-slate-700 flex flex-col items-center justify-center shrink-0 min-w-16">
                            <span className="text-lg font-black text-violet-500 leading-none">{filteredRows.length}</span>
                            <span className="text-[9px] text-slate-500 font-bold uppercase mt-0.5">Kayıt</span>
                        </div>
                    </div>

                    <div className="flex-1 overflow-auto rounded-xl border border-slate-700/80 bg-slate-900/60 scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent">
                        <table className="w-full border-collapse text-left">
                            <thead className="sticky top-0 z-20 backdrop-blur-md bg-slate-800/95 shadow-md border-b border-slate-700/80">
                                <tr>
                                    <th className="px-5 py-3.5 text-[11px] font-bold text-slate-400 uppercase tracking-widest w-20">Kod</th>
                                    <th className="px-5 py-3.5 text-[11px] font-bold text-slate-400 uppercase tracking-widest">Mağaza Adı</th>
                                    <th className="px-3 py-3.5 text-center text-[10px] font-black text-violet-500 uppercase tracking-widest w-12">Y1</th>
                                    <th className="px-3 py-3.5 text-center text-[10px] font-black text-violet-500 uppercase tracking-widest w-12">Y2</th>
                                    <th className="px-3 py-3.5 text-center text-[10px] font-black text-violet-500 uppercase tracking-widest w-12">Y3</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-slate-800/60">
                                {filteredRows.map(row => {
                                    const isSelected = selectedStoreCode === row.storeCode;
                                    return (
                                        <tr key={row.storeCode} onClick={() => setSelectedStoreCode(row.storeCode)}
                                            className={`cursor-pointer transition-colors ${isSelected ? "bg-violet-500/10" : "hover:bg-slate-800/60"}`}>
                                            <td className="px-5 py-3.5">
                                                <span className={`text-sm font-mono font-bold ${isSelected ? "text-violet-400" : "text-slate-400"}`}>{row.storeCode}</span>
                                            </td>
                                            <td className="px-5 py-3.5">
                                                <span className={`text-sm font-medium ${isSelected ? "text-white" : "text-slate-300"}`}>{row.storeName}</span>
                                            </td>
                                            <td className="px-3 py-3.5"><Led device={row.yazici1} /></td>
                                            <td className="px-3 py-3.5"><Led device={row.yazici2} /></td>
                                            <td className="px-3 py-3.5"><Led device={row.yazici3} /></td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                        {filteredRows.length === 0 && (
                            <div className="flex flex-col items-center justify-center h-48 text-slate-500">
                                <Search className="w-12 h-12 mb-4 opacity-20" />
                                <p className="text-sm font-medium">Böyle bir mağaza bulunamadı</p>
                            </div>
                        )}
                    </div>
                </div>

                {/* Right Pane */}
                <div className="flex-1 bg-slate-900/60 rounded-2xl border border-slate-700/50 p-6 flex flex-col overflow-y-auto shadow-2xl relative backdrop-blur-xl">
                    {selectedStore ? (
                        <div className="animate-in fade-in slide-in-from-bottom-4 duration-300 relative z-10 flex flex-col h-full">
                            <div className="flex items-start justify-between mb-8 pb-6 border-b border-slate-700/50">
                                <div className="flex items-center gap-6">
                                    <div className="w-20 h-20 rounded-3xl bg-gradient-to-br from-violet-500/20 to-violet-500/5 flex items-center justify-center border border-violet-500/30 shadow-xl shadow-violet-500/10">
                                        <Store className="w-10 h-10 text-violet-400" />
                                    </div>
                                    <div>
                                        <h2 className="text-4xl font-black text-white tracking-tight mb-2">{selectedStore.storeName}</h2>
                                        <div className="flex items-center gap-4">
                                            <div className="flex items-center gap-2 bg-slate-900/80 px-3 py-1.5 rounded-lg border border-slate-700/80 shadow-sm">
                                                <span className="text-[10px] text-slate-500 font-bold uppercase tracking-wider">MAĞAZA KODU</span>
                                                <span className="font-mono text-violet-400 font-black text-sm">{selectedStore.storeCode}</span>
                                            </div>
                                            <div className="text-sm text-slate-400 font-medium flex items-center gap-2">
                                                <span className="w-1.5 h-1.5 rounded-full bg-slate-500"></span>
                                                {[selectedStore.yazici1, selectedStore.yazici2, selectedStore.yazici3].filter(Boolean).length} yazıcı
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </div>

                            <div className={`flex-1 grid gap-6 auto-rows-max ${
                                [selectedStore.yazici1, selectedStore.yazici2, selectedStore.yazici3].filter(Boolean).length === 1
                                    ? "grid-cols-1 max-w-md"
                                    : [selectedStore.yazici1, selectedStore.yazici2, selectedStore.yazici3].filter(Boolean).length === 2
                                        ? "grid-cols-1 md:grid-cols-2"
                                        : "grid-cols-1 md:grid-cols-2 xl:grid-cols-3"
                            }`}>
                                {selectedStore.yazici1 && <PrinterCard title="YAZICI 1" device={selectedStore.yazici1} onDelete={handleDeleteDevice} onToggleClose={handleToggleClose} />}
                                {selectedStore.yazici2 && <PrinterCard title="YAZICI 2" device={selectedStore.yazici2} onDelete={handleDeleteDevice} onToggleClose={handleToggleClose} />}
                                {selectedStore.yazici3 && <PrinterCard title="YAZICI 3" device={selectedStore.yazici3} onDelete={handleDeleteDevice} onToggleClose={handleToggleClose} />}
                                {!selectedStore.yazici1 && !selectedStore.yazici2 && !selectedStore.yazici3 && (
                                    <div className="col-span-full flex flex-col items-center justify-center py-20 text-slate-500">
                                        <Printer className="w-16 h-16 mb-4 opacity-20" />
                                        <p className="text-lg">Bu mağazaya ait yazıcı bulunamadı</p>
                                    </div>
                                )}
                            </div>

                            <div className="absolute right-0 bottom-0 opacity-[0.03] pointer-events-none transform translate-x-10 translate-y-10">
                                <Printer className="w-96 h-96" />
                            </div>
                        </div>
                    ) : (
                        <div className="flex flex-col items-center justify-center h-full text-slate-500 w-full relative z-10">
                            <div className="w-40 h-40 mb-8 rounded-full bg-slate-800/30 border border-slate-700/30 flex items-center justify-center relative shadow-inner">
                                <Printer className="w-16 h-16 text-slate-600/50" />
                                <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-full h-full border-2 border-slate-600/20 rounded-full animate-ping opacity-20" style={{ animationDuration: "4s" }}></div>
                                <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-3/4 h-3/4 border-2 border-violet-500/20 rounded-full animate-pulse opacity-20"></div>
                            </div>
                            <h3 className="text-3xl font-bold text-slate-300 mb-4 tracking-tight drop-shadow-sm">Seçim Bekleniyor</h3>
                            <p className="max-w-md text-center text-slate-500/90 leading-relaxed text-base">
                                Sol taraftaki <span className="text-violet-500/80 font-medium">mağaza listesinden</span> bir kayıt seçerek
                                o mağazanın yazarkasa yazıcılarının ağ durumlarını görebilirsiniz.
            </p>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
};

export default YazicilarPage;
