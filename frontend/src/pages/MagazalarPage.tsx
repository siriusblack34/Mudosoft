import React, { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { apiClient, SqlDeviceWithStatus, StoreManager } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import {
    Building2, RefreshCw, Search, Wifi, WifiOff, PauseCircle,
    Monitor, ShoppingCart, CheckCircle2, AlertCircle, XCircle,
    X, User, Phone, MapPin, ExternalLink, Router as RouterIcon,
} from "lucide-react";

// Gerçek mağaza listesinden çıkarılan kodlar:
// - 1: geçici cihazlar için pseudo-kod (Mecidiyeköy/Tuzla-Gecici-X)
// - 150: "Sancaktepe Giyim Test Magaza" — gerçek mağaza değil
const EXCLUDED_STORE_CODES = new Set<number>([1, 150]);

type StoreStatus = "allOnline" | "partial" | "allOffline" | "closed";

interface StoreAgg {
    storeCode: number;
    storeName: string;
    router: SqlDeviceWithStatus | null;
    pc: SqlDeviceWithStatus | null;
    kasalar: SqlDeviceWithStatus[];
    otherDevices: SqlDeviceWithStatus[];
    totalDevices: number;
    onlineDevices: number;
    offlineDevices: number;
    closedDevices: number;
    status: StoreStatus;
}

const normalizeType = (t?: string) => (t ?? "").toUpperCase();

const aggregateByStore = (devices: SqlDeviceWithStatus[]): StoreAgg[] => {
    const map = new Map<number, SqlDeviceWithStatus[]>();
    for (const d of devices) {
        if (EXCLUDED_STORE_CODES.has(d.storeCode)) continue;
        const arr = map.get(d.storeCode);
        if (arr) arr.push(d);
        else map.set(d.storeCode, [d]);
    }

    const result: StoreAgg[] = [];
    for (const [storeCode, list] of map) {
        const router = list.find(d => normalizeType(d.deviceType) === "ROUTER") ?? null;
        const pc = list.find(d => normalizeType(d.deviceType) === "PC") ?? null;
        const kasalar = list.filter(d => normalizeType(d.deviceType).startsWith("KASA"));
        const others = list.filter(d => {
            const t = normalizeType(d.deviceType);
            return t !== "ROUTER" && t !== "PC" && !t.startsWith("KASA");
        });

        const online = list.filter(d => d.isOnline && !d.isTemporarilyClosed).length;
        const closed = list.filter(d => d.isTemporarilyClosed).length;
        const offline = list.length - online - closed;

        let status: StoreStatus;
        if (closed === list.length) status = "closed";
        else if (offline === 0) status = "allOnline";
        else if (online === 0) status = "allOffline";
        else status = "partial";

        result.push({
            storeCode,
            storeName: list[0].storeName,
            router, pc, kasalar, otherDevices: others,
            totalDevices: list.length,
            onlineDevices: online,
            offlineDevices: offline,
            closedDevices: closed,
            status,
        });
    }
    return result.sort((a, b) => a.storeCode - b.storeCode);
};

const StatusDot: React.FC<{ device: SqlDeviceWithStatus | null; label: string }> = ({ device, label }) => {
    if (!device) return (
        <span className="inline-flex items-center gap-1 text-[11px] text-slate-600" title={`${label}: kayıt yok`}>
            <span className="h-2 w-2 rounded-full bg-slate-700" />{label}
        </span>
    );
    const color = device.isTemporarilyClosed ? "bg-slate-500"
        : device.isOnline ? "bg-emerald-400"
        : "bg-rose-500";
    const state = device.isTemporarilyClosed ? "kapalı" : device.isOnline ? "online" : "offline";
    return (
        <span className="inline-flex items-center gap-1 text-[11px] text-slate-300" title={`${label}: ${state} (${device.calculatedIpAddress})`}>
            <span className={`h-2 w-2 rounded-full ${color}`} />{label}
        </span>
    );
};

const MagazalarPage: React.FC = () => {
    const [devices, setDevices] = useState<SqlDeviceWithStatus[]>([]);
    const [managers, setManagers] = useState<StoreManager[]>([]);
    const [search, setSearch] = useState("");
    const [statusFilter, setStatusFilter] = useState<"all" | StoreStatus>("all");
    const [isLoading, setIsLoading] = useState(true);
    const [lastUpdated, setLastUpdated] = useState<Date>(new Date());
    const [selectedStoreCode, setSelectedStoreCode] = useState<number | null>(null);

    const load = useCallback(async (silent = false) => {
        try {
            if (!silent) setIsLoading(true);
            const data = await apiClient.getSqlDevicesWithStatus({ timeoutMs: 500, maxConcurrency: 40 });
            setDevices(data ?? []);
            setLastUpdated(new Date());
        } catch (err) { console.error("Mağaza load failed:", err); }
        finally { if (!silent) setIsLoading(false); }
    }, []);

    useEffect(() => {
        load();
        apiClient.getStoreManagers()
            .then(m => setManagers(m ?? []))
            .catch(err => console.error("StoreManager load failed:", err));
        const t = setInterval(() => load(true), 30000);
        return () => clearInterval(t);
    }, [load]);

    const managerByCode = useMemo(() => {
        const m = new Map<number, StoreManager>();
        for (const sm of managers) {
            const key = (sm.actualStoreCode ?? sm.storeCode);
            if (key) m.set(key, sm);
        }
        return m;
    }, [managers]);

    const stores = useMemo(() => aggregateByStore(devices), [devices]);

    const summary = useMemo(() => {
        const total = stores.length;
        const allOnline = stores.filter(s => s.status === "allOnline").length;
        const partial = stores.filter(s => s.status === "partial").length;
        const allOffline = stores.filter(s => s.status === "allOffline").length;
        const closed = stores.filter(s => s.status === "closed").length;
        return { total, allOnline, partial, allOffline, closed };
    }, [stores]);

    const filtered = useMemo(() => {
        let list = stores;
        const q = search.trim().toLowerCase();
        if (q) list = list.filter(s =>
            s.storeName.toLowerCase().includes(q) || String(s.storeCode).includes(q)
        );
        if (statusFilter !== "all") list = list.filter(s => s.status === statusFilter);
        return list;
    }, [stores, search, statusFilter]);

    const selectedStore = useMemo(
        () => selectedStoreCode == null ? null : stores.find(s => s.storeCode === selectedStoreCode) ?? null,
        [selectedStoreCode, stores],
    );

    if (isLoading) return <div className="flex h-[80vh] items-center justify-center"><Spinner size="lg" /></div>;

    return (
        <div className="p-6 h-[calc(100vh-2rem)] flex flex-col gap-5 max-w-[1920px] mx-auto w-full">
            {/* Header */}
            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="flex items-center gap-3">
                    <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-sky-500/10 text-sky-300">
                        <Building2 className="h-5 w-5" />
                    </div>
                    <div>
                        <h1 className="text-xl font-semibold text-white">Mağazalar</h1>
                        <p className="text-[12px] text-slate-500">
                            Toplam {summary.total} mağaza · son güncelleme {lastUpdated.toLocaleTimeString("tr-TR")}
                        </p>
                    </div>
                </div>
                <button
                    onClick={() => { setIsLoading(true); apiClient.getSqlDevicesWithStatus({ timeoutMs: 500, maxConcurrency: 40 }).then(d => { setDevices(d ?? []); setLastUpdated(new Date()); }).finally(() => setIsLoading(false)); }}
                    className="inline-flex items-center gap-2 rounded-lg border border-white/10 bg-white/[0.03] px-3 py-1.5 text-[12px] text-slate-300 hover:bg-white/[0.06] hover:text-white"
                >
                    <RefreshCw className="h-3.5 w-3.5" /> Yenile
                </button>
            </div>

            {/* Summary cards */}
            <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
                <SummaryCard icon={<Building2 className="h-4 w-4" />} label="Toplam Mağaza" value={summary.total} tone="slate" active={statusFilter === "all"} onClick={() => setStatusFilter("all")} />
                <SummaryCard icon={<CheckCircle2 className="h-4 w-4" />} label="Tamamı Online" value={summary.allOnline} tone="emerald" active={statusFilter === "allOnline"} onClick={() => setStatusFilter("allOnline")} />
                <SummaryCard icon={<AlertCircle className="h-4 w-4" />} label="Kısmi Offline" value={summary.partial} tone="amber" active={statusFilter === "partial"} onClick={() => setStatusFilter("partial")} />
                <SummaryCard icon={<XCircle className="h-4 w-4" />} label="Tamamı Offline" value={summary.allOffline} tone="rose" active={statusFilter === "allOffline"} onClick={() => setStatusFilter("allOffline")} />
                <SummaryCard icon={<PauseCircle className="h-4 w-4" />} label="Geçici Kapalı" value={summary.closed} tone="slate" active={statusFilter === "closed"} onClick={() => setStatusFilter("closed")} />
            </div>

            {/* Search */}
            <div className="relative max-w-sm">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-500" />
                <input
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Mağaza kodu veya adı..."
                    className="h-9 w-full rounded-lg border border-white/10 bg-white/[0.03] pl-9 pr-3 text-[13px] text-slate-200 placeholder:text-slate-600 focus:ring-1 focus:ring-sky-500/40"
                />
            </div>

            {/* Detay paneli */}
            {selectedStore && (
                <StoreDetailPanel
                    store={selectedStore}
                    manager={managerByCode.get(selectedStore.storeCode) ?? null}
                    onClose={() => setSelectedStoreCode(null)}
                    onChanged={() => load(true)}
                />
            )}

            {/* Table */}
            <div className="flex-1 overflow-auto rounded-lg border border-white/10 bg-slate-900/40">
                <table className="w-full text-[12px]">
                    <thead className="sticky top-0 bg-slate-900/80 backdrop-blur">
                        <tr className="text-left text-slate-400">
                            <th className="px-3 py-2 font-semibold">Kod</th>
                            <th className="px-3 py-2 font-semibold">Mağaza Adı</th>
                            <th className="px-3 py-2 font-semibold">Durum</th>
                            <th className="px-3 py-2 font-semibold">Router</th>
                            <th className="px-3 py-2 font-semibold">PC</th>
                            <th className="px-3 py-2 font-semibold">Kasalar</th>
                            <th className="px-3 py-2 font-semibold text-right">Toplam / Online</th>
                        </tr>
                    </thead>
                    <tbody>
                        {filtered.length === 0 ? (
                            <tr><td colSpan={7} className="px-3 py-6 text-center text-slate-500">Sonuç yok</td></tr>
                        ) : filtered.map(s => (
                            <tr
                                key={s.storeCode}
                                onClick={() => setSelectedStoreCode(s.storeCode)}
                                className={`border-t border-white/5 cursor-pointer hover:bg-white/[0.04] ${selectedStoreCode === s.storeCode ? "bg-sky-500/[0.08]" : ""}`}
                            >
                                <td className="px-3 py-2 font-mono text-slate-300">{s.storeCode}</td>
                                <td className="px-3 py-2 text-white">
                                    <span className="hover:text-sky-300">{s.storeName}</span>
                                </td>
                                <td className="px-3 py-2"><StatusBadge status={s.status} /></td>
                                <td className="px-3 py-2"><StatusDot device={s.router} label="Router" /></td>
                                <td className="px-3 py-2"><StatusDot device={s.pc} label="PC" /></td>
                                <td className="px-3 py-2">
                                    <div className="flex items-center gap-2">
                                        <ShoppingCart className="h-3 w-3 text-slate-500" />
                                        <span className="text-slate-400">{s.kasalar.filter(k => k.isOnline && !k.isTemporarilyClosed).length}/{s.kasalar.length}</span>
                                    </div>
                                </td>
                                <td className="px-3 py-2 text-right">
                                    <span className="font-mono text-slate-400">{s.onlineDevices}/{s.totalDevices}</span>
                                    {s.closedDevices > 0 && <span className="ml-1 text-[10px] text-slate-500">({s.closedDevices} kapalı)</span>}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    );
};

const SummaryCard: React.FC<{
    icon: React.ReactNode; label: string; value: number; tone: "emerald" | "amber" | "rose" | "slate"; active: boolean; onClick: () => void;
}> = ({ icon, label, value, tone, active, onClick }) => {
    const toneClass = {
        emerald: "text-emerald-300",
        amber: "text-amber-300",
        rose: "text-rose-300",
        slate: "text-slate-300",
    }[tone];
    return (
        <button
            onClick={onClick}
            className={`flex items-center justify-between rounded-lg border p-3 text-left transition-colors ${
                active ? "border-sky-400/40 bg-sky-500/[0.08]" : "border-white/10 bg-white/[0.02] hover:bg-white/[0.04]"
            }`}
        >
            <div>
                <div className="text-[11px] uppercase tracking-wider text-slate-500">{label}</div>
                <div className={`mt-1 text-2xl font-bold ${toneClass}`}>{value}</div>
            </div>
            <div className={`opacity-60 ${toneClass}`}>{icon}</div>
        </button>
    );
};

const StatusBadge: React.FC<{ status: StoreStatus }> = ({ status }) => {
    const cfg = {
        allOnline: { label: "Online", cls: "bg-emerald-500/15 text-emerald-300 border-emerald-500/30", icon: <Wifi className="h-3 w-3" /> },
        partial:   { label: "Kısmi",  cls: "bg-amber-500/15 text-amber-300 border-amber-500/30", icon: <AlertCircle className="h-3 w-3" /> },
        allOffline:{ label: "Offline", cls: "bg-rose-500/15 text-rose-300 border-rose-500/30", icon: <WifiOff className="h-3 w-3" /> },
        closed:    { label: "Kapalı", cls: "bg-slate-500/15 text-slate-300 border-slate-500/30", icon: <PauseCircle className="h-3 w-3" /> },
    }[status];
    return (
        <span className={`inline-flex items-center gap-1 rounded border px-1.5 py-0.5 text-[10px] font-medium ${cfg.cls}`}>
            {cfg.icon}{cfg.label}
        </span>
    );
};

const StoreDetailPanel: React.FC<{
    store: StoreAgg;
    manager: StoreManager | null;
    onClose: () => void;
    onChanged: () => void;
}> = ({ store, manager, onClose, onChanged }) => {
    const allDevices = useMemo(() => {
        const list: SqlDeviceWithStatus[] = [];
        if (store.router) list.push(store.router);
        if (store.pc) list.push(store.pc);
        list.push(...store.kasalar);
        list.push(...store.otherDevices);
        return list;
    }, [store]);

    const isClosed = store.status === "closed";
    const currentReason = allDevices.find(d => d.isTemporarilyClosed)?.temporaryCloseReason ?? "";
    const [busy, setBusy] = useState(false);
    const [reason, setReason] = useState("");

    const toggleStore = async (close: boolean) => {
        if (close && !reason.trim()) return;
        setBusy(true);
        try {
            await apiClient.setStoreTemporaryClose(store.storeCode, close, close ? reason.trim() : undefined);
            setReason("");
            onChanged();
        } catch (e: any) {
            alert("İşlem başarısız: " + (e?.message ?? "bilinmeyen hata"));
        } finally {
            setBusy(false);
        }
    };

    return (
        <div className="fixed inset-0 z-40" onClick={onClose}>
            <div className="absolute inset-0 bg-black/40" />
            <aside
                onClick={(e) => e.stopPropagation()}
                className="absolute right-0 top-0 h-full w-full max-w-md overflow-y-auto border-l border-white/10 bg-slate-950 shadow-2xl"
            >
                {/* Header */}
                <div className="sticky top-0 z-10 flex items-center justify-between border-b border-white/10 bg-slate-950/95 px-5 py-4 backdrop-blur">
                    <div>
                        <div className="text-[11px] font-mono text-slate-500">#{store.storeCode}</div>
                        <h2 className="text-lg font-semibold text-white">{store.storeName}</h2>
                        <div className="mt-1"><StatusBadge status={store.status} /></div>
                    </div>
                    <button
                        onClick={onClose}
                        className="rounded-lg p-1.5 text-slate-400 hover:bg-white/5 hover:text-white"
                    >
                        <X className="h-4 w-4" />
                    </button>
                </div>

                <div className="space-y-5 px-5 py-4">
                    {/* Mağaza durumu — pasife al / aktif et (tadilat/planlı kesinti) */}
                    <section>
                        <div className="mb-2 text-[11px] uppercase tracking-wider text-slate-500">Mağaza Durumu</div>
                        {isClosed ? (
                            <div className="space-y-2 rounded-lg border border-slate-500/30 bg-slate-500/[0.06] p-3">
                                <div className="flex items-center gap-2 text-[13px] font-medium text-slate-200">
                                    <PauseCircle className="h-4 w-4 text-slate-400" /> Mağaza pasif — taramaya girmiyor
                                </div>
                                {currentReason && (
                                    <div className="text-[12px] text-slate-400">Açıklama: <span className="text-slate-300">{currentReason}</span></div>
                                )}
                                <button
                                    onClick={() => toggleStore(false)}
                                    disabled={busy}
                                    className="w-full rounded-lg border border-emerald-500/30 bg-emerald-500/15 px-3 py-2 text-[13px] font-medium text-emerald-300 transition-colors hover:bg-emerald-500/25 disabled:opacity-50"
                                >
                                    {busy ? "İşleniyor…" : "Mağazayı Aktif Et"}
                                </button>
                            </div>
                        ) : (
                            <div className="space-y-2 rounded-lg border border-amber-500/25 bg-amber-500/[0.05] p-3">
                                <div className="text-[12px] text-slate-400">
                                    Tadilat / planlı kesinti için mağazayı pasife al. Tüm cihazlar <span className="text-slate-300">"Kapalı"</span> olur, taramaya girmez ve kesinti alarmı çalmaz.
                                </div>
                                <input
                                    value={reason}
                                    onChange={(e) => setReason(e.target.value)}
                                    placeholder="Açıklama (örn. tadilat — network kapalı)"
                                    className="h-9 w-full rounded-lg border border-white/10 bg-white/[0.03] px-3 text-[13px] text-slate-200 placeholder:text-slate-600 focus:ring-1 focus:ring-amber-500/40"
                                />
                                <button
                                    onClick={() => toggleStore(true)}
                                    disabled={busy || !reason.trim()}
                                    className="inline-flex w-full items-center justify-center gap-1.5 rounded-lg border border-rose-500/30 bg-rose-500/15 px-3 py-2 text-[13px] font-medium text-rose-300 transition-colors hover:bg-rose-500/25 disabled:opacity-50"
                                    title={!reason.trim() ? "Önce açıklama girin" : ""}
                                >
                                    <PauseCircle className="h-4 w-4" /> {busy ? "İşleniyor…" : "Mağazayı Pasife Al"}
                                </button>
                            </div>
                        )}
                    </section>

                    {/* Mağaza müdürü & adres */}
                    <section>
                        <div className="mb-2 text-[11px] uppercase tracking-wider text-slate-500">Mağaza Bilgileri</div>
                        <div className="space-y-2 rounded-lg border border-white/10 bg-white/[0.02] p-3 text-[13px]">
                            <InfoRow icon={<User className="h-3.5 w-3.5" />} label="Müdür" value={manager?.fullName ?? "—"} />
                            <InfoRow
                                icon={<Phone className="h-3.5 w-3.5" />}
                                label="Telefon"
                                value={manager?.phone ? (
                                    <a href={`tel:${manager.phone}`} className="text-sky-300 hover:underline">{manager.phone}</a>
                                ) : "—"}
                            />
                            <InfoRow icon={<MapPin className="h-3.5 w-3.5" />} label="Adres" value={manager?.address ?? "—"} multiline />
                        </div>
                    </section>

                    {/* Cihaz özeti */}
                    <section>
                        <div className="mb-2 flex items-center justify-between">
                            <div className="text-[11px] uppercase tracking-wider text-slate-500">Cihazlar ({store.totalDevices})</div>
                            <Link
                                to={`/devices?store=${store.storeCode}`}
                                className="inline-flex items-center gap-1 text-[11px] text-sky-300 hover:underline"
                            >
                                Agent sayfasında aç <ExternalLink className="h-3 w-3" />
                            </Link>
                        </div>

                        <div className="grid grid-cols-3 gap-2 text-center text-[11px]">
                            <MiniStat label="Online" value={store.onlineDevices} tone="emerald" />
                            <MiniStat label="Offline" value={store.offlineDevices} tone="rose" />
                            <MiniStat label="Kapalı" value={store.closedDevices} tone="slate" />
                        </div>

                        <div className="mt-3 space-y-1.5">
                            {allDevices.length === 0 ? (
                                <div className="rounded-lg border border-white/10 bg-white/[0.02] p-3 text-center text-[12px] text-slate-500">
                                    Cihaz kaydı yok
                                </div>
                            ) : allDevices.map(d => <DeviceRow key={d.deviceName + d.deviceType} device={d} />)}
                        </div>
                    </section>
                </div>
            </aside>
        </div>
    );
};

const InfoRow: React.FC<{ icon: React.ReactNode; label: string; value: React.ReactNode; multiline?: boolean }> = ({ icon, label, value, multiline }) => (
    <div className={`flex ${multiline ? "items-start" : "items-center"} gap-2`}>
        <span className="mt-0.5 text-slate-500">{icon}</span>
        <span className="w-16 shrink-0 text-[11px] uppercase tracking-wider text-slate-500">{label}</span>
        <span className={`flex-1 text-slate-200 ${multiline ? "" : "truncate"}`}>{value}</span>
    </div>
);

const MiniStat: React.FC<{ label: string; value: number; tone: "emerald" | "rose" | "slate" }> = ({ label, value, tone }) => {
    const cls = { emerald: "text-emerald-300", rose: "text-rose-300", slate: "text-slate-300" }[tone];
    return (
        <div className="rounded-lg border border-white/10 bg-white/[0.02] py-2">
            <div className={`text-lg font-bold ${cls}`}>{value}</div>
            <div className="text-[10px] uppercase tracking-wider text-slate-500">{label}</div>
        </div>
    );
};

const DeviceRow: React.FC<{ device: SqlDeviceWithStatus }> = ({ device }) => {
    const t = (device.deviceType ?? "").toUpperCase();
    const icon = t === "ROUTER" ? <RouterIcon className="h-3.5 w-3.5" />
        : t.startsWith("KASA") ? <ShoppingCart className="h-3.5 w-3.5" />
        : <Monitor className="h-3.5 w-3.5" />;
    const state = device.isTemporarilyClosed ? "closed"
        : device.isOnline ? "online" : "offline";
    const dot = state === "closed" ? "bg-slate-500"
        : state === "online" ? "bg-emerald-400"
        : "bg-rose-500";
    return (
        <div className="flex items-center gap-2 rounded-lg border border-white/10 bg-white/[0.02] px-3 py-1.5 text-[12px]">
            <span className={`h-2 w-2 shrink-0 rounded-full ${dot}`} />
            <span className="text-slate-500">{icon}</span>
            <span className="flex-1 truncate text-slate-200">{device.deviceName}</span>
            <span className="font-mono text-[11px] text-slate-500">{device.calculatedIpAddress || "—"}</span>
        </div>
    );
};

export default MagazalarPage;
