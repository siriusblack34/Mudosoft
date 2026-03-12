import React, { useEffect, useMemo, useState } from "react";
import { Search, Briefcase, Users, LayoutGrid, List, Store, Phone, Upload, X, MapPin } from "lucide-react";
import * as Icons from "../components/icons/Icons";
import { apiClient, StoreManager } from "../lib/apiClient";

const StoreManagersPage: React.FC = () => {
    const [search, setSearch] = useState("");
    const [viewMode, setViewMode] = useState<"grid" | "list">("grid");
    const [managers, setManagers] = useState<StoreManager[]>([]);
    const [isLoading, setIsLoading] = useState(true);

    // Import Modal State
    const [isImportModalOpen, setIsImportModalOpen] = useState(false);
    const [importText, setImportText] = useState("");
    const [isImporting, setIsImporting] = useState(false);
    const [importError, setImportError] = useState("");

    const loadData = async () => {
        setIsLoading(true);
        try {
            const managersData = await apiClient.getStoreManagers().catch(() => []);
            if (managersData) {
                setManagers(managersData);
            }
        } catch (err) {
            console.error("Failed to load data for managers page:", err);
        } finally {
            setIsLoading(false);
        }
    };

    useEffect(() => {
        loadData();
    }, []);

    const handleImportSubmit = async () => {
        if (!importText.trim()) {
            setImportError("Lütfen aktarılacak metni girin.");
            return;
        }

        setIsImporting(true);
        setImportError("");
        try {
            const result = await apiClient.importStoreManagers(importText);
            if (result.success) {
                setImportText("");
                setIsImportModalOpen(false);
                await loadData();
            }
        } catch (err: any) {
            setImportError(err.message || "İçe aktarma sırasında bir hata oluştu.");
        } finally {
            setIsImporting(false);
        }
    };

    const filteredManagers = useMemo(() => {
        return managers.filter(m => {
            const code = m.storeCode ? String(m.storeCode) : "";
            const matchesSearch =
                m.storeName.toLowerCase().includes(search.toLowerCase()) ||
                m.fullName.toLowerCase().includes(search.toLowerCase()) ||
                code.includes(search) ||
                (m.phone && m.phone.includes(search));

            return matchesSearch;
        });
    }, [managers, search]);

    return (
        <div className="p-6 h-[calc(100vh-2rem)] flex flex-col gap-6 max-w-[1920px] mx-auto w-full relative">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div>
                    <h1 className="text-3xl font-black text-white flex items-center gap-3 tracking-tight mb-1">
                        <Users className="w-8 h-8 text-sky-500" />
                        Mağaza Müdürleri
                    </h1>
                    <p className="text-slate-400 text-sm font-medium">Sistemde ve veritabanında kayıtlı mağaza yetkilileri</p>
                </div>

                <div className="flex items-center gap-4">
                    <button
                        onClick={() => setIsImportModalOpen(true)}
                        className="flex items-center gap-2 px-4 py-2 bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 rounded-xl hover:bg-emerald-500/20 transition-colors font-semibold text-sm shadow-lg shadow-emerald-500/5"
                    >
                        <Upload className="w-4 h-4" />
                        Excel'den İçe Aktar
                    </button>

                    <div className="flex items-center gap-1 bg-slate-900/40 p-1.5 rounded-xl border border-slate-700/50 shadow-sm">
                        <button
                            onClick={() => setViewMode("grid")}
                            className={`p-2 rounded-lg transition-all ${viewMode === "grid" ? "bg-sky-500/20 text-sky-400 shadow-inner" : "text-slate-500 hover:text-slate-300 hover:bg-slate-800"}`}
                            title="Izgara Görünümü"
                        >
                            <LayoutGrid className="w-5 h-5" />
                        </button>
                        <button
                            onClick={() => setViewMode("list")}
                            className={`p-2 rounded-lg transition-all ${viewMode === "list" ? "bg-sky-500/20 text-sky-400 shadow-inner" : "text-slate-500 hover:text-slate-300 hover:bg-slate-800"}`}
                            title="Liste Görünümü"
                        >
                            <List className="w-5 h-5" />
                        </button>
                    </div>
                </div>
            </div>

            {/* Filters Area */}
            <div className="flex gap-4 items-center bg-slate-900/60 p-4 rounded-2xl border border-slate-700/50 shadow-lg backdrop-blur-md">
                <div className="flex-1 relative">
                    <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-slate-400" />
                    <input
                        type="text"
                        placeholder="Ad, mağaza, kod veya telefon ile ara..."
                        value={search}
                        onChange={e => setSearch(e.target.value)}
                        className="w-full pl-12 pr-4 py-3 bg-slate-800/50 border border-slate-600/50 rounded-xl text-white placeholder-slate-500 focus:outline-none focus:border-sky-500/50 focus:ring-1 focus:ring-sky-500/50 transition-all font-medium text-sm"
                    />
                </div>

                <div className="px-5 py-2.5 bg-slate-800/80 rounded-xl border border-slate-700 flex flex-col items-center justify-center shrink-0 min-w-[100px]">
                    <span className="text-xl font-black text-sky-500 leading-none">{filteredManagers.length}</span>
                    <span className="text-[10px] text-slate-400 font-bold uppercase mt-1 tracking-wider">Kayıt Bulundu</span>
                </div>
            </div>

            {/* Content Area */}
            <div className="flex-1 overflow-auto rounded-2xl min-h-0 scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent">
                {isLoading ? (
                    <div className="flex flex-col items-center justify-center h-64 text-sky-500">
                        <div className="w-12 h-12 border-4 border-sky-500/20 border-t-sky-500 rounded-full animate-spin"></div>
                        <p className="mt-4 font-medium text-slate-400">Veritabanından yükleniyor...</p>
                    </div>
                ) : filteredManagers.length === 0 ? (
                    <div className="flex flex-col items-center justify-center h-64 text-slate-500 bg-slate-900/30 rounded-2xl border border-slate-800/50 border-dashed">
                        <Search className="w-16 h-16 mb-4 opacity-20 text-sky-500" />
                        <p className="text-lg font-medium text-slate-400">Arama kriterlerine uygun yönetici bulunamadı.</p>
                        <button
                            onClick={() => { setSearch(""); }}
                            className="mt-4 px-4 py-2 bg-sky-500/10 text-sky-400 rounded-lg text-sm font-semibold hover:bg-sky-500/20 transition-colors"
                        >
                            Aramayı Temizle
                        </button>
                    </div>
                ) : viewMode === "grid" ? (
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6 pb-6">
                        {filteredManagers.map((manager, idx) => {
                            const code = manager.storeCode ? String(manager.storeCode) : "-";
                            return (
                                <div key={`manager-${idx}`} className="bg-slate-900/60 rounded-2xl border border-slate-700/50 p-6 flex flex-col hover:border-sky-500/40 transition-all duration-300 shadow-lg shadow-black/10 hover:shadow-sky-900/20 group relative overflow-hidden backdrop-blur-xl hover:-translate-y-1">
                                    <div className="absolute top-0 right-0 w-32 h-32 bg-sky-500/5 rounded-full blur-3xl -translate-y-10 translate-x-10 group-hover:bg-sky-500/10 transition-colors" />

                                    <div className="flex justify-between items-start mb-5 relative z-10">
                                        <div className="w-12 h-12 rounded-xl bg-gradient-to-br from-sky-500/20 to-indigo-500/20 flex items-center justify-center border border-sky-500/30 text-sky-400 font-bold text-lg shadow-inner uppercase">
                                            {manager.fullName.charAt(0)}
                                        </div>
                                        <div className="px-2.5 py-1 bg-slate-800 rounded-md text-xs font-mono text-slate-400 border border-slate-700 flex flex-col items-end gap-1">
                                            <div>Kod: <span className="text-amber-400 font-bold text-sm">{code}</span></div>
                                        </div>
                                    </div>

                                    <div className="mb-6 relative z-10 flex-1">
                                        <h3 className="text-lg font-bold text-white mb-1 tracking-wide group-hover:text-sky-300 transition-colors">{manager.fullName}</h3>
                                        <div className="flex items-center gap-1.5 text-sm font-medium text-emerald-400">
                                            <Phone className="w-3.5 h-3.5" />
                                            {manager.phone}
                                        </div>
                                    </div>

                                    <div className="space-y-3 relative z-10">
                                        <div className="flex items-center gap-3 p-3 rounded-xl bg-slate-800/50 border border-slate-700/50">
                                            <Store className="w-4 h-4 text-amber-500 shrink-0" />
                                            <div className="overflow-hidden">
                                                <div className="text-[10px] text-slate-500 uppercase tracking-wider font-semibold">Mağaza Adı</div>
                                                <div className="text-sm font-medium text-slate-200 truncate" title={manager.storeName}>{manager.storeName}</div>
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            )
                        })}
                    </div>
                ) : (
                    <div className="bg-slate-900/60 rounded-2xl border border-slate-700/50 shadow-xl backdrop-blur-xl overflow-hidden pb-4">
                        <table className="w-full text-left border-collapse">
                            <thead className="bg-slate-800/80 border-b border-slate-700/80 text-[11px] font-bold text-slate-400 uppercase tracking-wider sticky top-0 z-10 backdrop-blur-md">
                                <tr>
                                    <th className="px-5 py-4">KOD</th>
                                    <th className="px-5 py-4">MAĞAZA ADI</th>
                                    <th className="px-5 py-4">AD SOYAD</th>
                                    <th className="px-5 py-4">TELEFON</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-slate-800/60">
                                {filteredManagers.map((manager, idx) => {
                                    const code = manager.storeCode ? String(manager.storeCode) : "-";
                                    return (
                                        <tr key={`manager-${idx}`} className="hover:bg-slate-800/40 transition-colors group">
                                            <td className="px-5 py-4 w-32">
                                                <span className="font-mono text-sm text-amber-400 font-bold bg-amber-500/10 px-2 py-1 rounded-md border border-amber-500/20">
                                                    {code}
                                                </span>
                                            </td>
                                            <td className="px-5 py-4 w-1/3">
                                                <span className="text-sm font-medium text-slate-300 flex items-center gap-2">
                                                    <Store className="w-4 h-4 text-amber-500" />
                                                    {manager.storeName}
                                                </span>
                                            </td>
                                            <td className="px-5 py-4">
                                                <div className="flex items-center gap-3">
                                                    <div className="w-8 h-8 rounded-full bg-slate-800 flex items-center justify-center text-slate-300 font-bold text-xs border border-slate-700 group-hover:bg-sky-900/50 group-hover:text-sky-300 group-hover:border-sky-500/30 transition-colors uppercase">
                                                        {manager.fullName.charAt(0)}
                                                    </div>
                                                    <div className="flex flex-col">
                                                        <span className="font-semibold text-white group-hover:text-sky-100 transition-colors">
                                                            {manager.fullName}
                                                        </span>
                                                    </div>
                                                </div>
                                            </td>
                                            <td className="px-5 py-4">
                                                <span className="text-sm font-medium text-emerald-400 flex items-center gap-2">
                                                    <Phone className="w-4 h-4" />
                                                    {manager.phone}
                                                </span>
                                            </td>
                                        </tr>
                                    )
                                })}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {/* Import Modal */}
            {isImportModalOpen && (
                <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
                    <div className="bg-slate-900 border border-slate-700 rounded-2xl shadow-2xl w-full max-w-3xl overflow-hidden flex flex-col">
                        <div className="p-5 border-b border-slate-800 flex justify-between items-center bg-slate-800/30">
                            <h2 className="text-xl font-bold text-white flex items-center gap-2">
                                <Upload className="w-5 h-5 text-emerald-500" />
                                Excel'den Müdür Listesi Aktar
                            </h2>
                            <button onClick={() => setIsImportModalOpen(false)} className="text-slate-400 hover:text-white transition-colors">
                                <X className="w-6 h-6" />
                            </button>
                        </div>

                        <div className="p-6 flex flex-col gap-4">
                            <p className="text-sm text-slate-300">
                                Excel'deki listenizi başlıklar hariç kopyalayıp aşağıdaki kutuya yapıştırın. Sistem otomatik olarak sadece <strong>Kod, Mağaza, İsim ve Telefon</strong> bilgilerini süzecektir:
                            </p>

                            <textarea
                                value={importText}
                                onChange={(e) => setImportText(e.target.value)}
                                placeholder="Örn:&#10;8131&#9;Samsun Yeşilyurt Giyim&#9;Elif Bayyurt&#9;0542 891 83 78&#10;...&#10;&#10;Verileri buraya yapıştırın (CTRL+V)..."
                                className="w-full h-[300px] bg-slate-950/50 border border-slate-700/50 rounded-xl p-4 text-slate-300 font-mono text-sm focus:outline-none focus:border-emerald-500/50 focus:ring-1 focus:ring-emerald-500/50 scrollbar-thin scrollbar-thumb-slate-700 resize-none whitespace-pre"
                            />

                            {importError && (
                                <div className="p-3 bg-red-500/10 border border-red-500/20 text-red-400 rounded-lg text-sm font-medium">
                                    {importError}
                                </div>
                            )}
                        </div>

                        <div className="p-5 border-t border-slate-800 bg-slate-800/30 flex justify-end gap-3">
                            <button
                                onClick={() => setIsImportModalOpen(false)}
                                className="px-5 py-2.5 rounded-xl font-semibold text-sm bg-slate-800 text-slate-300 hover:bg-slate-700 transition-colors"
                            >
                                İptal
                            </button>
                            <button
                                onClick={handleImportSubmit}
                                disabled={isImporting}
                                className="px-5 py-2.5 rounded-xl font-semibold text-sm bg-emerald-600 text-white hover:bg-emerald-500 transition-colors flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                {isImporting ? "Aktarılıyor..." : "İçe Aktar"}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default StoreManagersPage;
