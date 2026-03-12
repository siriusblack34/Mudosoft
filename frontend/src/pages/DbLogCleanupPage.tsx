import { useState, useEffect } from "react";
import { apiClient } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import {
    Trash2, RefreshCw, Search, CheckCircle, AlertTriangle, WifiOff,
    Database, Check, Loader2, FileText, ArrowDown, ArrowUp, ArrowUpDown
} from "lucide-react";

interface DbLogStatus {
    deviceId: string;
    storeCode: number;
    storeName: string;
    ipAddress: string;
    isOnline: boolean;
    exportLogCount: number;
    exportErrLogCount: number;
    total: number;
    status: "clean" | "dirty" | "error" | "offline" | "unknown";
    errorMessage?: string;
}

export default function DbLogCleanupPage() {
    const [searchTerm, setSearchTerm] = useState("");
    const [cleaningDevices, setCleaningDevices] = useState<Set<string>>(new Set());
    const [statusFilter, setStatusFilter] = useState<"all" | "dirty" | "offline" | "error">("all");
    const [data, setData] = useState<DbLogStatus[] | null>(null);
    const [isFetching, setIsFetching] = useState(false);
    const [sortCol, setSortCol] = useState<"exportLogCount" | "exportErrLogCount" | "total">("total");
    const [sortDir, setSortDir] = useState<"asc" | "desc">("desc");

    const handleSort = (col: typeof sortCol) => {
        if (sortCol === col) {
            setSortDir(d => d === "desc" ? "asc" : "desc");
        } else {
            setSortCol(col);
            setSortDir("desc");
        }
    };

    // Veri Çekme (Tümünü Kontrol Et)
    const fetchData = async () => {
        setIsFetching(true);
        try {
            const result = await apiClient.post<DbLogStatus[]>("/api/db-log-cleanup/check-all", {});
            setData(result);
        } catch (error) {
            console.error("Fetch error:", error);
        } finally {
            setIsFetching(false);
        }
    };

    // Tekil Temizleme (Truncate)
    const handleClean = async (device: DbLogStatus) => {
        if (!window.confirm(`${device.storeName} için EXPORT_LOG ve EXPORT_ERR_LOG tabloları TEMİZLENECEK (Truncate). Emin misiniz?`)) return;

        setCleaningDevices((prev) => new Set(prev).add(device.deviceId));
        try {
            await apiClient.post(`/api/db-log-cleanup/clean/${device.deviceId}`, {});
            // Başarılı olursa listeyi güncelle
            if (data) {
                const updatedData = data.map(d =>
                    d.deviceId === device.deviceId
                        ? { ...d, exportLogCount: 0, exportErrLogCount: 0, total: 0, status: "clean" as const }
                        : d
                );
                setData(updatedData);
            }
        } catch (error) {
            console.error("Clean error:", error);
            alert("Temizleme hatası!");
        } finally {
            setCleaningDevices((prev) => {
                const next = new Set(prev);
                next.delete(device.deviceId);
                return next;
            });
        }
    };

    // Toplu Temizleme (Clean All)
    const handleCleanAll = async () => {
        const confirmMsg = "DİKKAT! Tüm mağazaların EXPORT_LOG ve EXPORT_ERR_LOG tabloları SİLİNECEK (Truncate).\n\nBu işlem geri alınamaz!\nDevam etmek istiyor musunuz?";
        if (!window.confirm(confirmMsg)) return;

        // İkinci onay
        if (!window.confirm("Gerçekten EMİN MİSİNİZ? Veriler silinecek!")) return;

        setIsFetching(true);
        try {
            await apiClient.post("/api/db-log-cleanup/clean-all", {});
            alert("Tüm mağazalar için temizleme isteği gönderildi.\nSonuçları görmek için tekrar kontrol ediniz.");
            // İşlem bitince otomatik yenile
            fetchData();
        } catch (error) {
            console.error("Clean All error:", error);
            alert("Toplu temizleme sırasında hata oluştu!");
            setIsFetching(false);
        }
    };

    // Filtreleme + Sıralama
    const filteredData = (data?.filter((d) => {
        const matchesSearch =
            d.storeName.toLowerCase().includes(searchTerm.toLowerCase()) ||
            d.storeCode.toString().includes(searchTerm) ||
            d.ipAddress.includes(searchTerm);

        if (statusFilter === "all") return matchesSearch;
        return matchesSearch && d.status === statusFilter;
    }) ?? []).slice().sort((a, b) => {
        const aVal = a[sortCol] ?? 0;
        const bVal = b[sortCol] ?? 0;
        return sortDir === "desc" ? bVal - aVal : aVal - bVal;
    });

    // İstatistikler
    const stats = {
        clean: data?.filter((d) => d.status === "clean").length ?? 0,
        dirty: data?.filter((d) => d.status === "dirty").length ?? 0,
        offline: data?.filter((d) => d.status === "offline").length ?? 0,
        error: data?.filter((d) => d.status === "error").length ?? 0,
        totalLogs: data?.reduce((acc, curr) => acc + curr.total, 0) ?? 0,
    };

    return (
        <div className="p-6 h-[calc(100vh-2rem)] flex flex-col gap-5">
            {/* Page Header */}
            <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between glass-panel p-6 rounded-2xl border-white/5 shadow-lg">
                <div>
                    <h1 className="text-2xl font-bold tracking-tight text-white flex items-center gap-3">
                        <div className="p-2 rounded-xl bg-indigo-500/20 shadow-inner border border-indigo-500/30">
                            <FileText className="h-7 w-7 text-indigo-400" />
                        </div>
                        DB Log Temizliği
                    </h1>
                    <p className="text-sm text-slate-400 mt-2">
                        EXPORT_LOG ve EXPORT_ERR_LOG tablolarındaki kayıtları kontrol et ve temizle.
                    </p>
                </div>
                <div className="flex gap-4 items-center">
                    <div className="text-xs text-slate-400 font-mono hidden md:block">
                        {isFetching ? "İşlem yapılıyor..." : data ? `Son işlem: ${new Date().toLocaleTimeString()}` : ""}
                    </div>

                    <button
                        onClick={handleCleanAll}
                        disabled={isFetching}
                        className={`inline-flex items-center justify-center gap-2 rounded-xl py-2.5 px-5 text-sm font-semibold transition-all shadow-lg hover:-translate-y-0.5 disabled:opacity-50 disabled:cursor-not-allowed
                            bg-transparent border border-rose-500/50 text-rose-400 hover:bg-rose-500/10
                        `}
                    >
                        <Trash2 className="h-4 w-4" />
                        TÜMÜNÜ TEMİZLE
                    </button>

                    <button
                        onClick={fetchData}
                        disabled={isFetching}
                        className={`inline-flex items-center justify-center gap-2 rounded-xl py-2.5 px-6 text-sm font-semibold transition-all shadow-lg hover:-translate-y-0.5 disabled:opacity-50 disabled:cursor-not-allowed border-none
                            bg-gradient-to-r from-indigo-600 to-blue-600 text-white shadow-[0_0_15px_rgba(79,70,229,0.3)] hover:from-indigo-500 hover:to-blue-500
                        `}
                    >
                        {isFetching ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
                        Tümünü Kontrol Et
                    </button>
                </div>
            </div>

            {/* İstatistik Kartları */}
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-5">
                <div className="glass-panel border-white/5 rounded-2xl p-4 shadow-sm hover-lift group transition-all duration-300">
                    <div className="text-[10px] font-bold text-emerald-400 uppercase tracking-widest opacity-80 mb-2 group-hover:opacity-100 transition-opacity">TEMİZ</div>
                    <div className="text-3xl font-bold text-emerald-400">{stats.clean}</div>
                </div>
                <div className="glass-panel border-white/5 rounded-2xl p-4 shadow-sm hover-lift group transition-all duration-300">
                    <div className="text-[10px] font-bold text-amber-400 uppercase tracking-widest opacity-80 mb-2 group-hover:opacity-100 transition-opacity">DOLU</div>
                    <div className="text-3xl font-bold text-amber-400">{stats.dirty}</div>
                </div>
                <div className="glass-panel border-white/5 rounded-2xl p-4 shadow-sm hover-lift group transition-all duration-300">
                    <div className="text-[10px] font-bold text-slate-400 uppercase tracking-widest opacity-80 mb-2 group-hover:opacity-100 transition-opacity">OFFLINE</div>
                    <div className="text-3xl font-bold text-slate-400">{stats.offline}</div>
                </div>
                <div className="glass-panel border-white/5 rounded-2xl p-4 shadow-sm hover-lift group transition-all duration-300">
                    <div className="text-[10px] font-bold text-rose-400 uppercase tracking-widest opacity-80 mb-2 group-hover:opacity-100 transition-opacity">HATA</div>
                    <div className="text-3xl font-bold text-rose-400">{stats.error}</div>
                </div>
                <div className="glass-panel border-white/5 rounded-2xl p-4 shadow-sm hover-lift group transition-all duration-300">
                    <div className="text-[10px] font-bold text-violet-400 uppercase tracking-widest opacity-80 mb-2 group-hover:opacity-100 transition-opacity">TOPLAM LOG</div>
                    <div className="text-3xl font-bold text-violet-400">{stats.totalLogs}</div>
                </div>
            </div>

            {/* Filtre ve Tablo */}
            <div className="flex-1 overflow-hidden glass-card border-white/5 rounded-2xl shadow-xl flex flex-col">
                <div className="border-b border-white/5 p-4 bg-white/5">
                    <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                        <div className="relative max-w-sm flex-1">
                            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-500" />
                            <input
                                type="text"
                                placeholder="Mağaza adı, kodu veya IP ile ara..."
                                value={searchTerm}
                                onChange={(e) => setSearchTerm(e.target.value)}
                                className="w-full glass-card rounded-xl py-2.5 pl-10 pr-4 text-sm text-slate-200 placeholder-slate-500 focus-ring transition-all border-white/5"
                            />
                        </div>
                        <div className="flex items-center gap-3">
                            <select
                                value={statusFilter}
                                onChange={(e) => setStatusFilter(e.target.value as any)}
                                className="glass-button rounded-xl py-2.5 pl-4 pr-10 text-sm focus-ring transition-all appearance-none cursor-pointer"
                                style={{ backgroundPosition: "right 0.75rem center", backgroundRepeat: "no-repeat", backgroundSize: "1em", backgroundImage: `url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%239ca3af' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e")` }}
                            >
                                <option value="all" className="bg-slate-800 text-white">Tümü</option>
                                <option value="dirty" className="bg-slate-800 text-amber-400">Dolu</option>
                                <option value="offline" className="bg-slate-800 text-slate-400">Offline</option>
                                <option value="error" className="bg-slate-800 text-rose-400">Hatalı</option>
                            </select>
                            <div className="text-xs text-slate-500 font-mono px-3 py-1.5 bg-black/20 rounded-full">
                                {filteredData.length} PC
                            </div>
                        </div>
                    </div>
                </div>

                <div className="overflow-auto flex-1">
                    <table className="w-full text-left border-collapse">
                        <thead className="sticky top-0 z-20">
                            <tr className="bg-white/5 border-b border-white/5">
                                <th className="px-4 py-3 text-xs font-semibold text-slate-400 uppercase tracking-widest w-16">Kod</th>
                                <th className="px-4 py-3 text-xs font-semibold text-slate-400 uppercase tracking-widest">Mağaza</th>
                                <th className="px-4 py-3 text-center text-xs font-semibold text-slate-400 uppercase tracking-widest w-32">IP Adresi</th>
                                <th className="px-4 py-3 text-center text-xs font-semibold text-slate-400 uppercase tracking-widest w-24">Durum</th>

                                <th className="px-3 py-3 text-center text-xs font-semibold text-sky-400 border-b border-white/5 w-32">
                                    <button onClick={() => handleSort("exportLogCount")} className="inline-flex items-center gap-1 hover:text-sky-300 transition-colors">
                                        EXPORT_LOG
                                        {sortCol === "exportLogCount" ? (sortDir === "desc" ? <ArrowDown className="w-3 h-3" /> : <ArrowUp className="w-3 h-3" />) : <ArrowUpDown className="w-3 h-3 opacity-30" />}
                                    </button>
                                </th>
                                <th className="px-3 py-3 text-center text-xs font-semibold text-rose-400 border-b border-white/5 w-32">
                                    <button onClick={() => handleSort("exportErrLogCount")} className="inline-flex items-center gap-1 hover:text-rose-300 transition-colors">
                                        EXPORT_ERR_LOG
                                        {sortCol === "exportErrLogCount" ? (sortDir === "desc" ? <ArrowDown className="w-3 h-3" /> : <ArrowUp className="w-3 h-3" />) : <ArrowUpDown className="w-3 h-3 opacity-30" />}
                                    </button>
                                </th>
                                <th className="px-3 py-3 text-center text-xs font-semibold text-amber-500 border-b border-white/5 w-24 bg-amber-500/10">
                                    <button onClick={() => handleSort("total")} className="inline-flex items-center gap-1 hover:text-amber-300 transition-colors">
                                        Toplam
                                        {sortCol === "total" ? (sortDir === "desc" ? <ArrowDown className="w-3 h-3" /> : <ArrowUp className="w-3 h-3" />) : <ArrowUpDown className="w-3 h-3 opacity-30" />}
                                    </button>
                                </th>

                                <th className="px-4 py-3 text-center text-xs font-semibold text-slate-400 uppercase tracking-widest w-24">İşlem</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-white/5">
                            {isFetching && filteredData.length === 0 ? (
                                <tr>
                                    <td colSpan={8} className="p-12 text-center text-slate-500">
                                        <div className="flex flex-col items-center gap-3">
                                            <Spinner size="lg" className="text-indigo-500" />
                                            <span>Veriler yükleniyor...</span>
                                        </div>
                                    </td>
                                </tr>
                            ) : filteredData.length === 0 ? (
                                <tr>
                                    <td colSpan={8} className="p-12 text-center text-slate-500">
                                        <div className="flex flex-col items-center gap-3 opacity-50">
                                            <Database className="h-12 w-12 text-slate-600" />
                                            <span>Görüntülenecek kayıt bulunamadı.</span>
                                        </div>
                                    </td>
                                </tr>
                            ) : (
                                filteredData.map((d) => (
                                    <tr key={d.deviceId} className="group hover:bg-white/5 transition-colors">
                                        <td className="px-4 py-3 text-xs font-mono text-violet-400 font-bold border-r border-white/5">
                                            [{d.storeCode}]
                                        </td>
                                        <td className="px-4 py-3 text-sm text-slate-200 font-medium">
                                            {d.storeName}
                                        </td>
                                        <td className="px-4 py-3 text-center text-xs font-mono text-slate-500 group-hover:text-slate-400 transition-colors">
                                            {d.ipAddress}
                                        </td>
                                        <td className="px-4 py-3 text-center">
                                            {d.status === "offline" ? (
                                                <span className="inline-flex items-center gap-1 rounded-full bg-slate-500/10 px-2.5 py-1 text-[11px] font-medium text-slate-400 border border-slate-500/20">
                                                    <WifiOff className="h-3 w-3" /> Offline
                                                </span>
                                            ) : d.status === "error" ? (
                                                <div className="group/err relative inline-flex justify-center">
                                                    <span className="inline-flex items-center gap-1 rounded-full bg-rose-500/10 px-2.5 py-1 text-[11px] font-medium text-rose-400 border border-rose-500/20 cursor-help">
                                                        <AlertTriangle className="h-3 w-3" /> Hata
                                                    </span>
                                                    {d.errorMessage && (
                                                        <div className="absolute bottom-full left-1/2 mb-2 w-48 -translate-x-1/2 rounded bg-slate-900 px-2 py-1 text-[10px] text-slate-300 shadow-xl border border-slate-700 opacity-0 group-hover/err:opacity-100 transition-opacity z-10 pointer-events-none">
                                                            {d.errorMessage}
                                                        </div>
                                                    )}
                                                </div>
                                            ) : d.status === "dirty" ? (
                                                <span className="inline-flex items-center gap-1 rounded-full bg-amber-500/10 px-2.5 py-1 text-[11px] font-medium text-amber-400 border border-amber-500/20">
                                                    <Database className="h-3 w-3" /> {d.total} Kayıt
                                                </span>
                                            ) : (
                                                <span className="inline-flex items-center gap-1 rounded-full bg-emerald-500/10 px-2.5 py-1 text-[11px] font-medium text-emerald-400 border border-emerald-500/20">
                                                    <Check className="h-3 w-3" /> Temiz
                                                </span>
                                            )}
                                        </td>

                                        {/* Log Sayıları */}
                                        <td className="px-3 py-3 text-center text-xs font-mono text-sky-400 border-l border-white/5">{d.status === "offline" ? '-' : d.exportLogCount}</td>
                                        <td className="px-3 py-3 text-center text-xs font-mono text-rose-400">{d.status === "offline" ? '-' : d.exportErrLogCount}</td>
                                        <td className="px-3 py-3 text-center text-xs font-mono text-amber-400 font-bold bg-amber-500/10 border-l border-r border-white/5">{d.status === "offline" ? '-' : d.total}</td>

                                        <td className="px-4 py-3 text-center">
                                            <button
                                                disabled={d.status !== "dirty" && d.status !== "clean"}
                                                onClick={() => handleClean(d)}
                                                className={`
                                                    inline-flex items-center justify-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-medium transition-all
                                                    ${cleaningDevices.has(d.deviceId) ? "opacity-50 cursor-wait" : ""}
                                                    ${d.status === "dirty"
                                                        ? "bg-rose-500/10 text-rose-400 hover:bg-rose-500 hover:text-white border border-rose-500/30"
                                                        : "bg-transparent text-emerald-500 cursor-default"
                                                    }
                                                `}
                                            >
                                                {cleaningDevices.has(d.deviceId) ? (
                                                    <Loader2 className="h-3 w-3 animate-spin" />
                                                ) : d.status === "dirty" ? (
                                                    <>
                                                        <Trash2 className="h-3 w-3" />
                                                        DB Temizle
                                                    </>
                                                ) : (
                                                    <CheckCircle className="h-4 w-4" />
                                                )}
                                            </button>
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
    );
}
