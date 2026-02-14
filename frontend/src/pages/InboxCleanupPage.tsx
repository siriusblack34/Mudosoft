import React, { useState, useMemo } from "react";
import { apiClient } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import {
    Trash2, RefreshCw, Search, CheckCircle, AlertTriangle, WifiOff,
    FolderOpen, Zap, FileText
} from "lucide-react";

interface InboxStatus {
    deviceId: string;
    storeCode: number;
    storeName: string;
    ipAddress: string;
    isOnline: boolean;
    rdyCount: number;
    txtCount: number;
    tmp1Count: number; // Kasa
    tmp2Count: number; // Ready
    totalCount: number;
    status: "clean" | "dirty" | "error" | "offline" | "unknown";
    errorMessage?: string;
}

const InboxCleanupPage: React.FC = () => {
    const [devices, setDevices] = useState<InboxStatus[]>([]);
    const [search, setSearch] = useState("");
    const [isChecking, setIsChecking] = useState(false);
    const [isCleaning, setIsCleaning] = useState(false);
    const [cleaningDeviceId, setCleaningDeviceId] = useState<string | null>(null);
    const [lastChecked, setLastChecked] = useState<Date | null>(null);
    const [statusMessage, setStatusMessage] = useState<string | null>(null);

    // ===================== CHECK ALL =====================
    const handleCheckAll = async () => {
        setIsChecking(true);
        setStatusMessage(null);
        try {
            const data = await apiClient.post<InboxStatus[]>("/api/inbox-cleanup/check-all");
            setDevices(data);
            setLastChecked(new Date());

            const dirty = data.filter(d => d.status === "dirty").length;
            const clean = data.filter(d => d.status === "clean").length;
            const offline = data.filter(d => d.status === "offline").length;
            setStatusMessage(`✅ Kontrol tamamlandı: ${clean} temiz, ${dirty} dolu, ${offline} offline`);
        } catch (err: any) {
            setStatusMessage(`❌ Hata: ${err.message}`);
        } finally {
            setIsChecking(false);
        }
    };

    // ===================== CLEAN SINGLE =====================
    const handleCleanSingle = async (deviceId: string) => {
        setCleaningDeviceId(deviceId);
        try {
            await apiClient.post<any>(`/api/inbox-cleanup/clean/${deviceId}`);
            // Re-check this device
            const updated = await apiClient.post<InboxStatus>(`/api/inbox-cleanup/check/${deviceId}`);
            setDevices(prev => prev.map(d => d.deviceId === deviceId ? updated : d));
            setStatusMessage(`✅ ${updated.storeName} temizlendi`);
        } catch (err: any) {
            setStatusMessage(`❌ Temizleme hatası: ${err.message}`);
        } finally {
            setCleaningDeviceId(null);
        }
    };

    // ===================== CLEAN ALL =====================
    const handleCleanAll = async () => {
        if (!window.confirm("Tüm online PC'lerdeki .rdy, .txt ve .tmp dosyaları silinecek. Devam edilsin mi?")) return;

        setIsCleaning(true);
        setStatusMessage("Tüm PC'ler temizleniyor...");
        try {
            const result = await apiClient.post<any>("/api/inbox-cleanup/clean-all");
            setStatusMessage(`✅ Toplu temizlik tamamlandı: ${result.successCount}/${result.totalCount} başarılı`);
            // Re-check all
            await handleCheckAll();
        } catch (err: any) {
            setStatusMessage(`❌ Toplu temizlik hatası: ${err.message}`);
        } finally {
            setIsCleaning(false);
        }
    };

    // ===================== STATS =====================
    const stats = useMemo(() => {
        const clean = devices.filter(d => d.status === "clean").length;
        const dirty = devices.filter(d => d.status === "dirty").length;
        const offline = devices.filter(d => d.status === "offline").length;
        const error = devices.filter(d => d.status === "error").length;
        const totalFiles = devices.reduce((sum, d) => sum + d.totalCount, 0);
        return { clean, dirty, offline, error, totalFiles };
    }, [devices]);

    const [sortNotClean, setSortNotClean] = useState(false);

    // ===================== FILTER & SORT =====================
    const filtered = useMemo(() => {
        let result = devices;

        // Search
        const s = search.trim().toLowerCase();
        if (s) {
            result = result.filter(d =>
                d.storeName.toLowerCase().includes(s) ||
                String(d.storeCode).includes(s) ||
                d.ipAddress.includes(s)
            );
        }

        // Sort: Dirty > Error > Offline > Clean
        if (sortNotClean) {
            result = [...result].sort((a, b) => {
                const getScore = (status: string) => {
                    if (status === "dirty") return 0;
                    if (status === "error") return 1;
                    if (status === "offline") return 2;
                    return 3; // clean/unknown
                };
                return getScore(a.status) - getScore(b.status);
            });
        }

        return result;
    }, [devices, search, sortNotClean]);

    // ===================== STATUS BADGE =====================
    const StatusBadge = ({ status, totalCount }: { status: string; totalCount: number }) => {
        switch (status) {
            case "clean":
                return (
                    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-emerald-500/20 text-emerald-400 border border-emerald-500/30">
                        <CheckCircle className="w-3 h-3" /> Temiz
                    </span>
                );
            case "dirty":
                return (
                    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-amber-500/20 text-amber-400 border border-amber-500/30">
                        <AlertTriangle className="w-3 h-3" /> {totalCount} dosya
                    </span>
                );
            case "offline":
                return (
                    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-slate-500/20 text-slate-400 border border-slate-500/30">
                        <WifiOff className="w-3 h-3" /> Offline
                    </span>
                );
            case "error":
                return (
                    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-rose-500/20 text-rose-400 border border-rose-500/30">
                        <AlertTriangle className="w-3 h-3" /> Hata
                    </span>
                );
            default:
                return <span className="text-xs text-slate-500">—</span>;
        }
    };

    return (
        <div className="p-6 h-[calc(100vh-2rem)] flex flex-col gap-5">
            {/* Header */}
            <div className="flex items-center justify-between">
                <h1 className="text-2xl font-bold text-white flex items-center gap-3">
                    <FolderOpen className="w-7 h-7 text-orange-500" />
                    Inbox Temizlik
                </h1>
                <div className="flex items-center gap-3">
                    {lastChecked && (
                        <span className="text-xs text-slate-500">
                            Son kontrol: {lastChecked.toLocaleTimeString("tr-TR")}
                        </span>
                    )}
                    <button
                        onClick={handleCheckAll}
                        disabled={isChecking}
                        className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-500 disabled:bg-slate-700 text-white text-sm font-medium rounded-lg transition-colors"
                    >
                        {isChecking ? <Spinner size="sm" /> : <RefreshCw className="w-4 h-4" />}
                        {isChecking ? "Kontrol ediliyor..." : "Tümünü Kontrol Et"}
                    </button>
                    <button
                        onClick={handleCleanAll}
                        disabled={isCleaning || stats.dirty === 0}
                        className="flex items-center gap-2 px-4 py-2 bg-rose-600 hover:bg-rose-500 disabled:bg-slate-700 disabled:text-slate-500 text-white text-sm font-medium rounded-lg transition-colors"
                    >
                        {isCleaning ? <Spinner size="sm" /> : <Trash2 className="w-4 h-4" />}
                        Tümünü Temizle
                    </button>
                </div>
            </div>

            {/* Status Message */}
            {statusMessage && (
                <div className={`px-4 py-2 rounded-lg text-sm ${statusMessage.startsWith("❌")
                    ? "bg-rose-900/30 border border-rose-500/30 text-rose-300"
                    : "bg-emerald-900/30 border border-emerald-500/30 text-emerald-300"
                    }`}>
                    {statusMessage}
                </div>
            )}

            {/* Stats Cards */}
            {devices.length > 0 && (
                <div className="grid grid-cols-5 gap-3">
                    <div className="bg-emerald-900/20 border border-emerald-500/30 rounded-xl p-3 flex items-center gap-3">
                        <CheckCircle className="w-6 h-6 text-emerald-400" />
                        <div>
                            <div className="text-[10px] text-emerald-400/80 uppercase">Temiz</div>
                            <div className="text-2xl font-bold text-emerald-400">{stats.clean}</div>
                        </div>
                    </div>
                    <div className="bg-amber-900/20 border border-amber-500/30 rounded-xl p-3 flex items-center gap-3">
                        <AlertTriangle className="w-6 h-6 text-amber-400" />
                        <div>
                            <div className="text-[10px] text-amber-400/80 uppercase">Dolu</div>
                            <div className="text-2xl font-bold text-amber-400">{stats.dirty}</div>
                        </div>
                    </div>
                    <div className="bg-slate-800/50 border border-slate-600/30 rounded-xl p-3 flex items-center gap-3">
                        <WifiOff className="w-6 h-6 text-slate-400" />
                        <div>
                            <div className="text-[10px] text-slate-400/80 uppercase">Offline</div>
                            <div className="text-2xl font-bold text-slate-400">{stats.offline}</div>
                        </div>
                    </div>
                    <div className="bg-rose-900/20 border border-rose-500/30 rounded-xl p-3 flex items-center gap-3">
                        <AlertTriangle className="w-6 h-6 text-rose-400" />
                        <div>
                            <div className="text-[10px] text-rose-400/80 uppercase">Hata</div>
                            <div className="text-2xl font-bold text-rose-400">{stats.error}</div>
                        </div>
                    </div>
                    <div className="bg-orange-900/20 border border-orange-500/30 rounded-xl p-3 flex items-center gap-3">
                        <FileText className="w-6 h-6 text-orange-400" />
                        <div>
                            <div className="text-[10px] text-orange-400/80 uppercase">Toplam Dosya</div>
                            <div className="text-2xl font-bold text-orange-400">{stats.totalFiles}</div>
                        </div>
                    </div>
                </div>
            )}

            {/* Search */}
            <div className="flex items-center gap-4 bg-slate-800/40 p-3 rounded-xl border border-slate-700/50">
                <div className="flex-1 relative">
                    <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                    <input
                        type="text"
                        placeholder="Mağaza adı, kodu veya IP ile ara..."
                        value={search}
                        onChange={e => setSearch(e.target.value)}
                        className="w-full pl-10 pr-4 py-2 bg-slate-900 border border-slate-700 rounded-lg text-sm text-white placeholder-slate-500 focus:outline-none focus:border-orange-500"
                    />
                </div>
                <div className="flex items-center gap-3 px-3 py-1.5 bg-slate-900/50 rounded-lg border border-slate-700/50 cursor-pointer hover:bg-slate-900/80 transition-colors" onClick={() => setSortNotClean(!sortNotClean)}>
                    <input
                        type="checkbox"
                        id="sortNotClean"
                        checked={sortNotClean}
                        onChange={() => { }} // handled by parent div click
                        className="w-4 h-4 rounded border-slate-600 bg-slate-700 text-orange-500 focus:ring-orange-500 focus:ring-offset-0 cursor-pointer pointer-events-none"
                    />
                    <label htmlFor="sortNotClean" className="text-sm text-slate-300 cursor-pointer select-none whitespace-nowrap pointer-events-none">
                        ⚠️ Sorunlular Üstte
                    </label>
                </div>
                <span className="text-sm text-slate-400">{filtered.length} PC</span>
            </div>

            {/* Initial State - No check done yet */}
            {devices.length === 0 && !isChecking && (
                <div className="flex-1 flex flex-col items-center justify-center text-slate-500">
                    <FolderOpen className="w-16 h-16 mb-4 opacity-30" />
                    <p className="text-lg mb-2">Henüz kontrol yapılmadı</p>
                    <p className="text-sm mb-6">Tüm PC'lerin Inbox durumunu görmek için kontrol başlatın</p>
                    <button
                        onClick={handleCheckAll}
                        className="flex items-center gap-2 px-6 py-3 bg-blue-600 hover:bg-blue-500 text-white font-medium rounded-lg transition-colors"
                    >
                        <Zap className="w-5 h-5" />
                        Kontrol Başlat
                    </button>
                </div>
            )}

            {/* Loading */}
            {isChecking && devices.length === 0 && (
                <div className="flex-1 flex flex-col items-center justify-center">
                    <Spinner size="lg" />
                    <p className="text-slate-400 mt-4">Tüm PC'ler kontrol ediliyor...</p>
                    <p className="text-xs text-slate-500 mt-1">Bu işlem 1-2 dakika sürebilir</p>
                </div>
            )}

            {/* Table */}
            {filtered.length > 0 && (
                <div className="flex-1 overflow-auto">
                    <table className="w-full border-collapse rounded-xl border border-slate-700 bg-slate-900/60">
                        <thead className="sticky top-0 z-20">
                            <tr className="bg-slate-800">
                                <th className="px-3 py-2 text-left text-xs font-semibold text-slate-300 border-b border-slate-700 w-14">Kod</th>
                                <th className="px-3 py-2 text-left text-xs font-semibold text-slate-300 border-b border-slate-700">Mağaza</th>
                                <th className="px-3 py-2 text-left text-xs font-semibold text-slate-300 border-b border-slate-700 w-32">IP Adresi</th>
                                <th className="px-3 py-2 text-center text-xs font-semibold text-slate-300 border-b border-slate-700 w-20">Durum</th>
                                <th className="px-3 py-2 text-center text-xs font-semibold text-orange-400 border-b border-slate-700 w-14">RDY</th>
                                <th className="px-3 py-2 text-center text-xs font-semibold text-orange-400 border-b border-slate-700 w-14">TXT</th>
                                <th className="px-3 py-2 text-center text-xs font-semibold text-blue-400 border-b border-slate-700 w-16" title="Kasa Klasörü (.tmp)">TMP (Kasa)</th>
                                <th className="px-3 py-2 text-center text-xs font-semibold text-blue-400 border-b border-slate-700 w-16" title="Ready Klasörü (.tmp)">TMP (Ready)</th>
                                <th className="px-3 py-2 text-center text-xs font-semibold text-slate-300 border-b border-slate-700 w-20">İşlem</th>
                            </tr>
                        </thead>
                        <tbody>
                            {filtered.map((d, idx) => (
                                <tr
                                    key={d.deviceId}
                                    className={`${idx % 2 === 0 ? "bg-slate-900/30" : "bg-slate-800/20"} hover:bg-slate-700/30 transition-colors`}
                                >
                                    <td className="px-3 py-1.5 border-b border-slate-800">
                                        <span className="text-orange-400 font-bold text-xs">[{d.storeCode}]</span>
                                    </td>
                                    <td className="px-3 py-1.5 border-b border-slate-800">
                                        <span className="text-white text-xs">{d.storeName}</span>
                                    </td>
                                    <td className="px-3 py-1.5 border-b border-slate-800">
                                        <span className="text-slate-400 text-xs font-mono">{d.ipAddress}</span>
                                    </td>
                                    <td className="px-3 py-1.5 border-b border-slate-800 text-center">
                                        <StatusBadge status={d.status} totalCount={d.totalCount} />
                                    </td>
                                    <td className="px-3 py-1.5 border-b border-slate-800 text-center">
                                        <span className={`text-xs font-mono ${d.rdyCount > 0 ? "text-amber-400 font-bold" : "text-slate-600"}`}>
                                            {d.status === "offline" || d.status === "unknown" ? "—" : d.rdyCount}
                                        </span>
                                    </td>
                                    <td className="px-3 py-1.5 border-b border-slate-800 text-center">
                                        <span className={`text-xs font-mono ${d.txtCount > 0 ? "text-amber-400 font-bold" : "text-slate-600"}`}>
                                            {d.status === "offline" || d.status === "unknown" ? "—" : d.txtCount}
                                        </span>
                                    </td>
                                    <td className="px-3 py-1.5 border-b border-slate-800 text-center">
                                        <span className={`text-xs font-mono ${d.tmp1Count > 0 ? "text-blue-400 font-bold" : "text-slate-600"}`}>
                                            {d.status === "offline" || d.status === "unknown" ? "—" : d.tmp1Count}
                                        </span>
                                    </td>
                                    <td className="px-3 py-1.5 border-b border-slate-800 text-center">
                                        <span className={`text-xs font-mono ${d.tmp2Count > 0 ? "text-blue-400 font-bold" : "text-slate-600"}`}>
                                            {d.status === "offline" || d.status === "unknown" ? "—" : d.tmp2Count}
                                        </span>
                                    </td>
                                    <td className="px-3 py-1.5 border-b border-slate-800 text-center">
                                        {d.status === "dirty" && (
                                            <button
                                                onClick={() => handleCleanSingle(d.deviceId)}
                                                disabled={cleaningDeviceId === d.deviceId}
                                                className="inline-flex items-center gap-1 px-2 py-1 bg-rose-600/80 hover:bg-rose-500 disabled:bg-slate-700 text-white text-[11px] font-medium rounded transition-colors"
                                                title="Bu PC'yi temizle"
                                            >
                                                {cleaningDeviceId === d.deviceId ? (
                                                    <Spinner size="sm" />
                                                ) : (
                                                    <Trash2 className="w-3 h-3" />
                                                )}
                                                Temizle
                                            </button>
                                        )}
                                        {d.status === "clean" && (
                                            <CheckCircle className="w-4 h-4 text-emerald-500 mx-auto" />
                                        )}
                                        {d.status === "error" && (
                                            <span className="text-[10px] text-rose-400" title={d.errorMessage}>
                                                {d.errorMessage?.substring(0, 30)}...
                                            </span>
                                        )}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </div>
    );
};

export default InboxCleanupPage;
