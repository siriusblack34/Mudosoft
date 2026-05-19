import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
    Boxes, Upload, RefreshCw, Search, AlertTriangle, ChevronLeft, ChevronRight,
    BarChart3, ListChecks, Database,
} from "lucide-react";
import {
    apiClient,
    InventoryAsset,
    InventorySummary,
    InventoryStats,
    InventoryFilterOptions,
    InventoryImportResult,
    StoreNameMapping,
    InventoryImportBatch,
} from "../lib/apiClient";

type Tab = "list" | "dashboard" | "import" | "mappings";

const InventoryPage: React.FC = () => {
    const [tab, setTab] = useState<Tab>("list");
    const [summary, setSummary] = useState<InventorySummary | null>(null);

    const refreshSummary = useCallback(async () => {
        try { setSummary(await apiClient.getInventorySummary()); } catch { /* ignore */ }
    }, []);

    useEffect(() => { refreshSummary(); }, [refreshSummary]);

    return (
        <div className="mx-auto max-w-[1800px] space-y-5 p-6">
            <div className="flex flex-col justify-between gap-4 xl:flex-row xl:items-end">
                <div>
                    <h1 className="flex items-center gap-3 text-2xl font-bold text-ms-text">
                        <Boxes className="h-6 w-6 text-violet-400" />
                        Envanter
                    </h1>
                    <p className="mt-1 text-sm text-ms-text-muted">
                        Tam envanter modülü — import, liste, dashboard ve mağaza eşleme.
                    </p>
                </div>
                <button onClick={refreshSummary} className="btn-secondary !px-3">
                    <RefreshCw className="h-4 w-4" /> Yenile
                </button>
            </div>

            {summary && <SummaryCards s={summary} />}

            <div className="border-b border-ms-border flex gap-1">
                {([
                    ["list", <ListChecks className="h-4 w-4" />, "Liste"],
                    ["dashboard", <BarChart3 className="h-4 w-4" />, "Dashboard"],
                    ["import", <Upload className="h-4 w-4" />, "Import"],
                    ["mappings", <Database className="h-4 w-4" />, "Mağaza Eşlemeleri"],
                ] as Array<[Tab, React.ReactNode, string]>).map(([t, icon, label]) => (
                    <button key={t} onClick={() => setTab(t)}
                        className={`flex items-center gap-2 px-4 py-2 text-sm border-b-2 -mb-px transition-colors ${tab === t
                            ? "border-violet-500 text-violet-400 font-medium"
                            : "border-transparent text-ms-text-muted hover:text-ms-text"}`}>
                        {icon} {label}
                    </button>
                ))}
            </div>

            {tab === "list" && <AssetListTab />}
            {tab === "dashboard" && <DashboardTab />}
            {tab === "import" && <ImportTab onImported={refreshSummary} />}
            {tab === "mappings" && <MappingsTab onChanged={refreshSummary} />}
        </div>
    );
};

// ── Summary cards ───────────────────────────────────────────────────────

const SummaryCards: React.FC<{ s: InventorySummary }> = ({ s }) => {
    const card = (label: string, value: number, color: string) => (
        <div className="card flex-1">
            <div className="text-xs text-ms-text-muted uppercase tracking-wider">{label}</div>
            <div className={`text-2xl font-bold mt-1 ${color}`}>{value.toLocaleString("tr-TR")}</div>
        </div>
    );
    return (
        <div>
            {card("Toplam Asset", s.totalAssets, "text-ms-text")}
        </div>
    );
};

// ── List tab ────────────────────────────────────────────────────────────

const AssetListTab: React.FC = () => {
    const [items, setItems] = useState<InventoryAsset[]>([]);
    const [total, setTotal] = useState(0);
    const [loading, setLoading] = useState(false);
    const [opts, setOpts] = useState<InventoryFilterOptions | null>(null);

    const [search, setSearch] = useState("");
    const [storeCode, setStoreCode] = useState<string>("");
    const [productType, setProductType] = useState<string>("");
    const [state, setState] = useState<string>("");
    const [unmatchedOnly, setUnmatchedOnly] = useState(false);
    const [page, setPage] = useState(1);
    const pageSize = 50;
    const [sortBy, setSortBy] = useState("assetName");
    const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");

    useEffect(() => {
        apiClient.getInventoryFilterOptions().then(setOpts).catch(() => { });
    }, []);

    const load = useCallback(async () => {
        setLoading(true);
        try {
            const res = await apiClient.getInventoryAssets({
                search: search || undefined,
                storeCode: storeCode ? parseInt(storeCode, 10) : undefined,
                productType: productType || undefined,
                state: state || undefined,
                unmatchedOnly: unmatchedOnly || undefined,
                sortBy, sortDir, page, pageSize,
            });
            setItems(res.items);
            setTotal(res.total);
        } finally { setLoading(false); }
    }, [search, storeCode, productType, state, unmatchedOnly, sortBy, sortDir, page]);

    useEffect(() => { load(); }, [load]);

    const searchTimer = useRef<number | null>(null);
    const onSearchChange = (v: string) => {
        setSearch(v);
        if (searchTimer.current) window.clearTimeout(searchTimer.current);
        searchTimer.current = window.setTimeout(() => setPage(1), 350);
    };

    const totalPages = Math.max(1, Math.ceil(total / pageSize));

    const toggleSort = (key: string) => {
        if (sortBy === key) setSortDir(sortDir === "asc" ? "desc" : "asc");
        else { setSortBy(key); setSortDir("asc"); }
    };

    const selectClass = "bg-ms-bg border border-ms-border text-ms-text rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-violet-500/50";

    return (
        <div className="space-y-3">
            <div className="card">
                <div className="flex flex-wrap gap-2 items-center">
                    <div className="relative">
                        <Search className="h-4 w-4 absolute left-3 top-1/2 -translate-y-1/2 text-ms-text-muted" />
                        <input className="!pl-9 w-72"
                            placeholder="Asset / ürün / seri / hostname…"
                            value={search} onChange={(e) => onSearchChange(e.target.value)} />
                    </div>
                    <select className={selectClass} value={storeCode}
                        onChange={(e) => { setStoreCode(e.target.value); setPage(1); }}>
                        <option value="">Tüm Mağazalar</option>
                        {opts?.stores.map(s => (
                            <option key={s.storeCode} value={s.storeCode}>
                                {s.storeCode} {s.storeName ? `— ${s.storeName}` : ""} ({s.count})
                            </option>
                        ))}
                    </select>
                    <select className={selectClass} value={productType}
                        onChange={(e) => { setProductType(e.target.value); setPage(1); }}>
                        <option value="">Tüm Ürün Tipleri</option>
                        {opts?.productTypes.map(p => <option key={p} value={p}>{p}</option>)}
                    </select>
                    <select className={selectClass} value={state}
                        onChange={(e) => { setState(e.target.value); setPage(1); }}>
                        <option value="">Tüm Durumlar</option>
                        {opts?.states.map(p => <option key={p} value={p}>{p}</option>)}
                    </select>
                    <label className="flex items-center gap-2 text-sm cursor-pointer text-ms-text">
                        <input type="checkbox" checked={unmatchedOnly}
                            onChange={(e) => { setUnmatchedOnly(e.target.checked); setPage(1); }}
                            className="accent-violet-500" />
                        Sadece eşleşmeyenler
                    </label>
                    <button onClick={load} className="btn-secondary !px-3 ml-auto">
                        <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} /> Yenile
                    </button>
                </div>
            </div>

            <div className="card !p-0 overflow-hidden">
                <div className="overflow-x-auto">
                    <table className="data-table">
                        <thead>
                            <tr>
                                {[
                                    ["assetName", "Asset"],
                                    ["storeCode", "Mağaza"],
                                    ["productType", "Ürün Tipi"],
                                    ["product", "Ürün"],
                                    ["acquisitionDate", "Alım Tarihi"],
                                    ["purchaseCost", "Maliyet"],
                                ].map(([key, label]) => (
                                    <th key={key} onClick={() => toggleSort(key)} className="cursor-pointer hover:text-ms-text">
                                        {label}{sortBy === key ? (sortDir === "asc" ? " ↑" : " ↓") : ""}
                                    </th>
                                ))}
                                <th>Seri</th>
                                <th>Durum</th>
                                <th>Fiziksel</th>
                            </tr>
                        </thead>
                        <tbody>
                            {loading && (
                                <tr><td colSpan={9} className="text-center !py-8 text-ms-text-muted">Yükleniyor…</td></tr>
                            )}
                            {!loading && items.length === 0 && (
                                <tr><td colSpan={9} className="text-center !py-8 text-ms-text-muted">Kayıt bulunamadı</td></tr>
                            )}
                            {!loading && items.map(a => (
                                <tr key={a.id} className="hover:bg-ms-border/30 transition-colors">
                                    <td className="font-mono text-xs">{a.assetName}</td>
                                    <td>
                                        {a.storeCode ?? <span className="text-orange-400 text-xs">eşleşmedi</span>}
                                        {a.storeNameRaw && <div className="text-xs text-ms-text-muted">{a.storeNameRaw}</div>}
                                    </td>
                                    <td>{a.productType || "—"}</td>
                                    <td>{a.product || "—"}</td>
                                    <td className="text-xs">
                                        {a.acquisitionDate ? new Date(a.acquisitionDate).toLocaleDateString("tr-TR") : "—"}
                                    </td>
                                    <td className="text-right">
                                        {a.purchaseCost != null ? `₺${a.purchaseCost.toLocaleString("tr-TR")}` : "—"}
                                    </td>
                                    <td className="font-mono text-xs">{a.orgSerialNumber || "—"}</td>
                                    <td className="text-xs">{a.assetState || "—"}</td>
                                    <td className="text-xs">{a.fizikselDurum || "—"}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>

                <div className="flex items-center justify-between px-4 py-3 border-t border-ms-border text-sm">
                    <div className="text-ms-text-muted">
                        Toplam <strong className="text-ms-text">{total.toLocaleString("tr-TR")}</strong> kayıt — Sayfa {page} / {totalPages}
                    </div>
                    <div className="flex gap-1">
                        <button disabled={page <= 1} onClick={() => setPage(p => p - 1)}
                            className="btn-secondary !px-2 !py-1 disabled:opacity-40 disabled:cursor-not-allowed">
                            <ChevronLeft className="h-4 w-4" />
                        </button>
                        <button disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}
                            className="btn-secondary !px-2 !py-1 disabled:opacity-40 disabled:cursor-not-allowed">
                            <ChevronRight className="h-4 w-4" />
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

// ── Dashboard tab ───────────────────────────────────────────────────────

const DashboardTab: React.FC = () => {
    const [stats, setStats] = useState<InventoryStats | null>(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        apiClient.getInventoryStats().then(setStats).finally(() => setLoading(false));
    }, []);

    if (loading) return <div className="text-ms-text-muted py-8 text-center">Yükleniyor…</div>;
    if (!stats) return <div className="text-ms-text-muted py-8 text-center">Veri yok</div>;

    const max = (arr: Array<{ count: number }>) => Math.max(1, ...arr.map(x => x.count));

    const Bar: React.FC<{ data: Array<{ name?: string; storeCode?: number; year?: number; count: number }>; title: string; color: string }> =
        ({ data, title, color }) => {
            const m = max(data);
            return (
                <div className="card">
                    <h3 className="font-semibold text-ms-text mb-3">{title}</h3>
                    <div className="space-y-1.5 max-h-96 overflow-y-auto">
                        {data.map((d, i) => {
                            const label = d.name ?? (d.storeCode != null ? `Mağaza ${d.storeCode}` : String(d.year));
                            return (
                                <div key={i} className="flex items-center gap-2 text-xs">
                                    <span className="w-32 truncate text-ms-text-muted" title={label}>{label}</span>
                                    <div className="flex-1 bg-ms-bg rounded h-5 relative overflow-hidden">
                                        <div className={`h-5 rounded ${color}`}
                                            style={{ width: `${(d.count / m) * 100}%` }} />
                                        <span className="absolute right-2 top-0.5 text-[11px] font-medium text-ms-text">
                                            {d.count.toLocaleString("tr-TR")}
                                        </span>
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                </div>
            );
        };

    return (
        <div className="space-y-4">
            <div className="card bg-gradient-to-r from-violet-500/10 to-indigo-500/10 border-violet-500/30">
                <div className="text-xs text-violet-300 uppercase tracking-wider">Toplam Satın Alma Maliyeti</div>
                <div className="text-2xl font-bold text-ms-text mt-1">
                    ₺{stats.totalPurchaseCost.toLocaleString("tr-TR", { maximumFractionDigits: 2 })}
                </div>
            </div>
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                <Bar data={stats.byProductType} title="Ürün Tipine Göre Dağılım" color="bg-violet-500" />
                <Bar data={stats.byState} title="Asset State Dağılımı" color="bg-emerald-500" />
                <Bar data={stats.byFizikselDurum} title="Fiziksel Durum" color="bg-indigo-500" />
                <Bar data={stats.byStoreTop20} title="En Çok Asset (Top 20 Mağaza)" color="bg-orange-500" />
                <Bar data={stats.acquisitionByYear} title="Yıllık Satın Alma Trendi" color="bg-teal-500" />
            </div>
        </div>
    );
};

// ── Import tab ──────────────────────────────────────────────────────────

const ImportTab: React.FC<{ onImported: () => void }> = ({ onImported }) => {
    const [file, setFile] = useState<File | null>(null);
    const [running, setRunning] = useState(false);
    const [result, setResult] = useState<InventoryImportResult | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [batches, setBatches] = useState<InventoryImportBatch[]>([]);

    const loadBatches = useCallback(() => {
        apiClient.getInventoryBatches().then(setBatches).catch(() => { });
    }, []);

    useEffect(() => { loadBatches(); }, [loadBatches]);

    const onUpload = async () => {
        if (!file) return;
        setRunning(true); setError(null); setResult(null);
        try {
            const res = await apiClient.importInventoryXlsx(file);
            setResult(res);
            loadBatches();
            onImported();
        } catch (e: any) {
            setError(e.message || "Import başarısız");
        } finally { setRunning(false); }
    };

    return (
        <div className="space-y-4">
            <div className="card">
                <h3 className="font-semibold text-ms-text mb-2 flex items-center gap-2">
                    <Upload className="h-5 w-5 text-violet-400" /> XLSX Yükle
                </h3>
                <p className="text-sm text-ms-text-muted mb-4">
                    <code className="px-1 py-0.5 bg-ms-bg rounded text-violet-300">.xlsx</code> envanter dosyasını yükleyin.
                    Asset Name'e göre upsert çalışır; mevcut kayıtlar güncellenir.
                </p>
                <div className="flex flex-wrap items-center gap-3">
                    <input type="file" accept=".xlsx"
                        onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                        className="text-sm text-ms-text file:mr-3 file:px-3 file:py-1.5 file:rounded file:border-0 file:bg-ms-border file:text-ms-text file:cursor-pointer hover:file:bg-violet-500/30" />
                    <button onClick={onUpload} disabled={!file || running} className="btn-primary disabled:opacity-50">
                        {running ? "İşleniyor…" : "Yükle"}
                    </button>
                </div>
                {running && <div className="text-sm text-ms-text-muted mt-3">Büyük dosyalar 1-2 dakika sürebilir.</div>}
                {error && (
                    <div className="mt-4 p-3 bg-rose-500/10 border border-rose-500/30 rounded-lg text-sm text-rose-300 flex items-start gap-2">
                        <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" /> {error}
                    </div>
                )}
                {result && (
                    <div className="mt-4 p-4 bg-emerald-500/10 border border-emerald-500/30 rounded-lg">
                        <div className="font-medium text-emerald-300 mb-2">Import tamam (Batch #{result.batchId})</div>
                        <div className="grid grid-cols-2 md:grid-cols-5 gap-3">
                            <Stat label="Toplam" value={result.totalRows} />
                            <Stat label="Eklendi" value={result.insertedCount} color="text-emerald-400" />
                            <Stat label="Güncellendi" value={result.updatedCount} color="text-violet-400" />
                            <Stat label="Atlandı" value={result.skippedCount} />
                            <Stat label="Mağaza Eşleşmedi" value={result.unmatchedStoreCount} color="text-orange-400" />
                        </div>
                        {result.unmatchedStoreNames.length > 0 && (
                            <div className="mt-3 text-xs text-orange-300">
                                <strong>Eşleşmeyen mağaza adları:</strong> {result.unmatchedStoreNames.slice(0, 10).join(", ")}
                                {result.unmatchedStoreNames.length > 10 && ` +${result.unmatchedStoreNames.length - 10} daha`}
                                <div className="mt-1 text-ms-text-muted">→ "Mağaza Eşlemeleri" sekmesinden manuel atayın.</div>
                            </div>
                        )}
                    </div>
                )}
            </div>

            <div className="card !p-0 overflow-hidden">
                <div className="px-4 py-3 border-b border-ms-border font-medium text-sm text-ms-text">
                    Son Importlar
                </div>
                <table className="data-table">
                    <thead>
                        <tr>
                            <th>Tarih</th>
                            <th>Dosya</th>
                            <th>Yapan</th>
                            <th className="!text-right">Toplam</th>
                            <th className="!text-right">Eklendi</th>
                            <th className="!text-right">Güncellendi</th>
                            <th className="!text-right">Eşleşmedi</th>
                            <th>Durum</th>
                        </tr>
                    </thead>
                    <tbody>
                        {batches.length === 0 && (
                            <tr><td colSpan={8} className="text-center !py-6 text-ms-text-muted">Henüz import yok</td></tr>
                        )}
                        {batches.map(b => (
                            <tr key={b.id}>
                                <td className="text-xs">{new Date(b.importedAt).toLocaleString("tr-TR")}</td>
                                <td className="text-xs">{b.fileName}</td>
                                <td className="text-xs">{b.importedBy || "—"}</td>
                                <td className="!text-right">{b.totalRows.toLocaleString("tr-TR")}</td>
                                <td className="!text-right text-emerald-400">{b.insertedCount.toLocaleString("tr-TR")}</td>
                                <td className="!text-right text-violet-400">{b.updatedCount.toLocaleString("tr-TR")}</td>
                                <td className="!text-right text-orange-400">{b.unmatchedStoreCount}</td>
                                <td>
                                    <span className={`px-2 py-0.5 rounded text-xs ${b.status === "Completed" ? "bg-emerald-500/20 text-emerald-300" :
                                        b.status === "Failed" ? "bg-rose-500/20 text-rose-300" :
                                            "bg-ms-border text-ms-text-muted"
                                        }`}>{b.status}</span>
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    );
};

const Stat: React.FC<{ label: string; value: number; color?: string }> = ({ label, value, color = "text-ms-text" }) => (
    <div>
        <div className="text-xs text-ms-text-muted">{label}</div>
        <div className={`font-bold text-lg ${color}`}>{value.toLocaleString("tr-TR")}</div>
    </div>
);

// ── Mappings tab ────────────────────────────────────────────────────────

type StoreOption = { storeCode: number; storeName: string | null };

const MappingsTab: React.FC<{ onChanged: () => void }> = ({ onChanged }) => {
    const [list, setList] = useState<StoreNameMapping[]>([]);
    const [stores, setStores] = useState<StoreOption[]>([]);
    const [loading, setLoading] = useState(true);
    const [edits, setEdits] = useState<Record<number, number | null>>({});

    const load = useCallback(async () => {
        setLoading(true);
        try {
            const [unmapped, allStores] = await Promise.all([
                apiClient.getInventoryUnmappedStores(),
                apiClient.getInventoryAllStores(),
            ]);
            setList(unmapped);
            setStores(allStores);
        } finally { setLoading(false); }
    }, []);

    useEffect(() => { load(); }, [load]);

    const save = async (m: StoreNameMapping) => {
        const code = edits[m.id] ?? null;
        try {
            const res = await apiClient.updateInventoryStoreMapping(m.id, code);
            alert(`Kaydedildi. ${res.affectedAssets} asset güncellendi.`);
            load();
            onChanged();
        } catch (e: any) {
            alert("Hata: " + e.message);
        }
    };

    const [rematching, setRematching] = useState(false);
    const rematch = async () => {
        setRematching(true);
        try {
            const res = await apiClient.rematchInventoryUnmapped();
            alert(`Otomatik eşleşme: ${res.updatedMappings} mağaza eşleşti, ${res.updatedAssets} asset güncellendi.`);
            load();
            onChanged();
        } catch (e: any) {
            alert("Hata: " + e.message);
        } finally { setRematching(false); }
    };

    return (
        <div className="card !p-0 overflow-hidden">
            <div className="px-4 py-3 border-b border-ms-border flex items-center justify-between">
                <div className="font-medium text-sm text-ms-text">Eşleşmeyen Mağaza Adları ({list.length})</div>
                <div className="flex items-center gap-3">
                    <button onClick={rematch} disabled={rematching || list.length === 0}
                        className="btn-primary !px-3 !py-1 text-xs disabled:opacity-50">
                        {rematching ? "Eşleştiriliyor…" : "Otomatik Yeniden Eşleştir"}
                    </button>
                    <button onClick={load} className="text-sm flex items-center gap-1 text-ms-text-muted hover:text-violet-400 transition-colors">
                        <RefreshCw className={`h-3.5 w-3.5 ${loading ? "animate-spin" : ""}`} /> Yenile
                    </button>
                </div>
            </div>
            {loading ? (
                <div className="py-8 text-center text-ms-text-muted">Yükleniyor…</div>
            ) : list.length === 0 ? (
                <div className="py-8 text-center text-ms-text-muted">Tüm mağazalar eşleşti.</div>
            ) : (
                <table className="data-table">
                    <thead>
                        <tr>
                            <th>Ham Ad</th>
                            <th className="w-96">Mağaza Seç</th>
                            <th className="w-32"></th>
                        </tr>
                    </thead>
                    <tbody>
                        {list.map(m => (
                            <tr key={m.id}>
                                <td>{m.rawName}</td>
                                <td>
                                    <StoreCombobox
                                        stores={stores}
                                        defaultQuery={m.rawName}
                                        value={edits[m.id] ?? null}
                                        onChange={(code) => setEdits(s => ({ ...s, [m.id]: code }))} />
                                </td>
                                <td>
                                    <button onClick={() => save(m)} className="btn-primary !px-3 !py-1 text-xs">
                                        Kaydet
                                    </button>
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            )}
        </div>
    );
};

// Searchable mağaza dropdown — kod veya isim ile arar.
const StoreCombobox: React.FC<{
    stores: StoreOption[];
    value: number | null;
    defaultQuery?: string;
    onChange: (code: number | null) => void;
}> = ({ stores, value, defaultQuery, onChange }) => {
    const [query, setQuery] = useState("");
    const [open, setOpen] = useState(false);
    const wrapRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        const onDoc = (e: MouseEvent) => {
            if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setOpen(false);
        };
        document.addEventListener("mousedown", onDoc);
        return () => document.removeEventListener("mousedown", onDoc);
    }, []);

    const selected = stores.find(s => s.storeCode === value);
    const q = (query || (open ? "" : "")).toLocaleLowerCase("tr-TR");
    const filtered = useMemo(() => {
        const needle = q.trim();
        if (!needle) return stores.slice(0, 100);
        return stores.filter(s => {
            const name = (s.storeName ?? "").toLocaleLowerCase("tr-TR");
            return String(s.storeCode).includes(needle) || name.includes(needle);
        }).slice(0, 100);
    }, [stores, q]);

    const display = selected
        ? `${selected.storeCode} — ${selected.storeName ?? ""}`
        : "";

    return (
        <div ref={wrapRef} className="relative w-full">
            <input
                type="text"
                value={open ? query : display}
                placeholder={defaultQuery ? `Ara: ${defaultQuery}` : "Mağaza ara (kod veya isim)…"}
                onFocus={() => { setOpen(true); setQuery(""); }}
                onChange={(e) => { setQuery(e.target.value); setOpen(true); }}
                className="w-full !py-1 !px-2 text-sm" />
            {selected && !open && (
                <button
                    onMouseDown={(e) => { e.preventDefault(); onChange(null); }}
                    className="absolute right-2 top-1/2 -translate-y-1/2 text-ms-text-muted hover:text-rose-400 text-xs">
                    ✕
                </button>
            )}
            {open && (
                <div className="absolute z-20 mt-1 w-full max-h-72 overflow-y-auto bg-ms-bg border border-ms-border rounded-lg shadow-lg">
                    {filtered.length === 0 ? (
                        <div className="px-3 py-2 text-sm text-ms-text-muted">Sonuç yok</div>
                    ) : filtered.map(s => (
                        <button
                            key={s.storeCode}
                            onMouseDown={(e) => {
                                e.preventDefault();
                                onChange(s.storeCode);
                                setOpen(false);
                                setQuery("");
                            }}
                            className={`w-full text-left px-3 py-1.5 text-sm hover:bg-violet-500/20 ${value === s.storeCode ? "bg-violet-500/10 text-violet-300" : "text-ms-text"}`}>
                            <span className="font-mono text-xs text-ms-text-muted mr-2">{s.storeCode}</span>
                            {s.storeName ?? "—"}
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
};

export default InventoryPage;
