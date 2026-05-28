import React, { useEffect, useState, useCallback } from "react";
import {
    ShoppingCart, Database, HardDrive, Server, CheckCircle,
    XCircle, AlertTriangle, RefreshCw, ChevronDown, ChevronRight,
    Clock, Package, FolderOpen,
} from "lucide-react";
import { apiClient } from "../lib/apiClient";

// ── Types ─────────────────────────────────────────────────────────────────

interface PosServiceStatus {
    serviceName: string;
    status: string;
}

interface GeniusPosData {
    collectedAt: string;
    services: PosServiceStatus[];
    jreHome?: string;
    posVersion?: string;
    sqlVersion?: string;
    dbConnectable: boolean;
    stockTransferErrorCount: number;
    exportErrLogCount: number;
    lastSuccessfulTransferAt?: string;
    seqFileCount: number;
    xmlFileCount: number;
    seqXmlTotalMB: number;
    posDataPath?: string;
}

interface DevicePosHealth {
    storeDeviceId: string;
    deviceName: string;
    deviceType: string;
    ipAddress: string;
    agentDeviceId?: string;
    online: boolean;
    lastSeen?: string;
    posData?: GeniusPosData;
    healthStatus: string;
}

interface StorePosHealth {
    storeCode: number;
    storeName?: string;
    devices: DevicePosHealth[];
}

// ── Helpers ───────────────────────────────────────────────────────────────

function statusColor(status: string): string {
    switch (status) {
        case "Healthy": return "text-emerald-400";
        case "Warning": return "text-amber-400";
        case "Critical": return "text-red-400";
        case "Offline": return "text-zinc-500";
        default: return "text-zinc-400";
    }
}

function statusBg(status: string): string {
    switch (status) {
        case "Healthy": return "bg-emerald-500/10 border-emerald-500/30";
        case "Warning": return "bg-amber-500/10 border-amber-500/30";
        case "Critical": return "bg-red-500/10 border-red-500/30";
        default: return "bg-zinc-800/40 border-zinc-700/30";
    }
}

function StatusPill({ status }: { status: string }) {
    const styles: Record<string, string> = {
        Healthy: "bg-emerald-500/20 text-emerald-400 border-emerald-500/30",
        Warning: "bg-amber-500/20 text-amber-400 border-amber-500/30",
        Critical: "bg-red-500/20 text-red-400 border-red-500/30",
        Offline: "bg-zinc-700/50 text-zinc-400 border-zinc-600/30",
        Unknown: "bg-zinc-700/50 text-zinc-400 border-zinc-600/30",
    };
    return (
        <span className={`px-2 py-0.5 text-xs font-medium rounded-full border ${styles[status] ?? styles.Unknown}`}>
            {status}
        </span>
    );
}

function timeAgo(iso?: string): string {
    if (!iso) return "—";
    const diff = Date.now() - new Date(iso).getTime();
    const m = Math.floor(diff / 60000);
    if (m < 1) return "az önce";
    if (m < 60) return `${m} dk önce`;
    const h = Math.floor(m / 60);
    if (h < 24) return `${h} sa önce`;
    return `${Math.floor(h / 24)} gün önce`;
}

function deviceTypeLabel(type: string): string {
    if (type === "PC") return "Sunucu";
    if (type.startsWith("Kasa")) return type.replace("Kasa-", "Kasa ");
    return type;
}

// ── Main Page ─────────────────────────────────────────────────────────────

export default function GeniusPosHealthPage() {
    const [stores, setStores] = useState<StorePosHealth[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [expandedStores, setExpandedStores] = useState<Set<number>>(new Set());
    const [expandedDevices, setExpandedDevices] = useState<Set<string>>(new Set());
    const [filter, setFilter] = useState<"all" | "critical" | "warning">("all");

    const load = useCallback(async () => {
        setLoading(true);
        setError(null);
        try {
            const res = await apiClient.get<StorePosHealth[]>("/api/genius-pos/stores");
            setStores(res);
            // Auto-expand stores with issues
            const withIssues = new Set(
                res.filter(s => s.devices.some(d => d.healthStatus === "Critical" || d.healthStatus === "Warning"))
                    .map(s => s.storeCode)
            );
            setExpandedStores(withIssues);
        } catch (e: unknown) {
            setError(e instanceof Error ? e.message : "Yüklenemedi");
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => { load(); }, [load]);

    const toggleStore = (code: number) =>
        setExpandedStores(prev => {
            const next = new Set(prev);
            next.has(code) ? next.delete(code) : next.add(code);
            return next;
        });

    const toggleDevice = (id: string) =>
        setExpandedDevices(prev => {
            const next = new Set(prev);
            next.has(id) ? next.delete(id) : next.add(id);
            return next;
        });

    const filtered = stores.filter(s => {
        if (filter === "all") return true;
        return s.devices.some(d => d.healthStatus === (filter === "critical" ? "Critical" : "Warning"));
    });

    const totalCritical = stores.reduce((acc, s) => acc + s.devices.filter(d => d.healthStatus === "Critical").length, 0);
    const totalWarning = stores.reduce((acc, s) => acc + s.devices.filter(d => d.healthStatus === "Warning").length, 0);
    const totalHealthy = stores.reduce((acc, s) => acc + s.devices.filter(d => d.healthStatus === "Healthy").length, 0);

    return (
        <div className="p-6 space-y-5">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                    <ShoppingCart className="w-6 h-6 text-blue-400" />
                    <div>
                        <h1 className="text-xl font-bold text-ms-text">Genius POS Sağlık</h1>
                        <p className="text-xs text-zinc-500">Mağaza / kasa bazlı POS durumu</p>
                    </div>
                </div>
                <button onClick={load} disabled={loading}
                    className="flex items-center gap-2 px-3 py-1.5 bg-blue-600/20 text-blue-400 hover:bg-blue-600/30 rounded-lg text-sm transition-colors">
                    <RefreshCw className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
                    Yenile
                </button>
            </div>

            {/* Summary cards */}
            {!loading && (
                <div className="grid grid-cols-3 gap-3">
                    <div className="bg-red-500/10 border border-red-500/30 rounded-xl p-3 text-center">
                        <p className="text-2xl font-bold text-red-400">{totalCritical}</p>
                        <p className="text-xs text-zinc-500 mt-0.5">Kritik</p>
                    </div>
                    <div className="bg-amber-500/10 border border-amber-500/30 rounded-xl p-3 text-center">
                        <p className="text-2xl font-bold text-amber-400">{totalWarning}</p>
                        <p className="text-xs text-zinc-500 mt-0.5">Uyarı</p>
                    </div>
                    <div className="bg-emerald-500/10 border border-emerald-500/30 rounded-xl p-3 text-center">
                        <p className="text-2xl font-bold text-emerald-400">{totalHealthy}</p>
                        <p className="text-xs text-zinc-500 mt-0.5">Sağlıklı</p>
                    </div>
                </div>
            )}

            {/* Filter */}
            <div className="flex gap-1 bg-zinc-900 rounded-lg p-1 w-fit">
                {(["all", "critical", "warning"] as const).map(f => (
                    <button key={f} onClick={() => setFilter(f)}
                        className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${filter === f ? "bg-zinc-700 text-white" : "text-zinc-400 hover:text-ms-text"}`}>
                        {f === "all" ? "Tümü" : f === "critical" ? "Kritik" : "Uyarı"}
                    </button>
                ))}
            </div>

            {loading ? (
                <div className="flex items-center justify-center py-20">
                    <RefreshCw className="w-7 h-7 animate-spin text-blue-400" />
                </div>
            ) : error ? (
                <div className="text-center py-10 text-red-400">{error}</div>
            ) : (
                <div className="space-y-2">
                    {filtered.map(store => {
                        const isOpen = expandedStores.has(store.storeCode);
                        const worstStatus = store.devices.some(d => d.healthStatus === "Critical") ? "Critical"
                            : store.devices.some(d => d.healthStatus === "Warning") ? "Warning"
                            : store.devices.every(d => d.healthStatus === "Healthy") ? "Healthy"
                            : "Unknown";

                        return (
                            <div key={store.storeCode} className={`rounded-xl border overflow-hidden ${statusBg(worstStatus)}`}>
                                {/* Store header */}
                                <button
                                    className="w-full flex items-center justify-between p-3 hover:bg-white/5 transition-colors"
                                    onClick={() => toggleStore(store.storeCode)}>
                                    <div className="flex items-center gap-3">
                                        {isOpen ? <ChevronDown className="w-4 h-4 text-zinc-400" /> : <ChevronRight className="w-4 h-4 text-zinc-400" />}
                                        <span className="font-medium text-ms-text text-sm">
                                            {store.storeCode} — {store.storeName ?? `Mağaza ${store.storeCode}`}
                                        </span>
                                        <StatusPill status={worstStatus} />
                                    </div>
                                    <div className="flex items-center gap-3 text-xs text-zinc-500">
                                        <span>{store.devices.length} cihaz</span>
                                        {store.devices.filter(d => d.healthStatus === "Critical").length > 0 && (
                                            <span className="text-red-400 font-medium">
                                                {store.devices.filter(d => d.healthStatus === "Critical").length} kritik
                                            </span>
                                        )}
                                    </div>
                                </button>

                                {/* Device list */}
                                {isOpen && (
                                    <div className="border-t border-white/10 divide-y divide-white/5">
                                        {store.devices.map(device => (
                                            <DeviceRow
                                                key={device.storeDeviceId}
                                                device={device}
                                                expanded={expandedDevices.has(device.storeDeviceId)}
                                                onToggle={() => toggleDevice(device.storeDeviceId)}
                                            />
                                        ))}
                                    </div>
                                )}
                            </div>
                        );
                    })}
                    {filtered.length === 0 && (
                        <p className="text-center text-zinc-500 py-10">Eşleşen mağaza bulunamadı</p>
                    )}
                </div>
            )}
        </div>
    );
}

// ── Device Row ────────────────────────────────────────────────────────────

function DeviceRow({ device, expanded, onToggle }: { device: DevicePosHealth; expanded: boolean; onToggle: () => void }) {
    const pos = device.posData;

    return (
        <div>
            <button className="w-full flex items-center gap-3 px-4 py-2.5 hover:bg-white/5 transition-colors text-left"
                onClick={onToggle}>
                <div className="w-5 shrink-0">
                    {expanded ? <ChevronDown className="w-3.5 h-3.5 text-zinc-500" /> : <ChevronRight className="w-3.5 h-3.5 text-zinc-500" />}
                </div>

                {/* Device type icon */}
                {device.deviceType === "PC"
                    ? <Server className="w-4 h-4 text-zinc-400 shrink-0" />
                    : <ShoppingCart className="w-4 h-4 text-zinc-400 shrink-0" />}

                <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                        <span className="text-sm text-ms-text font-medium">{deviceTypeLabel(device.deviceType)}</span>
                        <span className="text-xs text-zinc-500">{device.ipAddress}</span>
                        <StatusPill status={device.healthStatus} />
                    </div>
                </div>

                {/* Quick metrics */}
                {pos && (
                    <div className="hidden sm:flex items-center gap-4 text-xs shrink-0">
                        <QuickMetric
                            icon={<Database className="w-3 h-3" />}
                            label="DB"
                            ok={pos.dbConnectable}
                        />
                        <QuickMetric
                            icon={<Package className="w-3 h-3" />}
                            label={`Hata: ${pos.stockTransferErrorCount}`}
                            ok={pos.stockTransferErrorCount === 0}
                        />
                        <QuickMetric
                            icon={<FolderOpen className="w-3 h-3" />}
                            label={`${pos.seqFileCount + pos.xmlFileCount} dosya`}
                            ok={pos.seqFileCount + pos.xmlFileCount < 200}
                        />
                    </div>
                )}

                <span className="text-xs text-zinc-600 shrink-0 ml-2">{timeAgo(device.lastSeen)}</span>
            </button>

            {/* Expanded detail */}
            {expanded && (
                <div className="px-12 pb-4 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                    {!pos ? (
                        <p className="text-xs text-zinc-500 col-span-full">
                            {device.online ? "GeniusPos collector verisi bekleniyor..." : "Cihaz çevrimdışı — veri yok"}
                        </p>
                    ) : (
                        <>
                            {/* Services */}
                            <InfoCard title="POS Servisleri" icon={<Server className="w-4 h-4 text-blue-400" />}>
                                {pos.services.length === 0
                                    ? <p className="text-xs text-zinc-500">Servis bulunamadı</p>
                                    : pos.services.map((s, i) => (
                                        <Row key={i} label={s.serviceName}
                                            value={s.status}
                                            valueColor={s.status === "Running" ? "text-emerald-400" : "text-red-400"} />
                                    ))}
                            </InfoCard>

                            {/* DB Info */}
                            <InfoCard title="Veritabanı" icon={<Database className="w-4 h-4 text-violet-400" />}>
                                <Row label="Bağlantı" value={pos.dbConnectable ? "OK" : "Başarısız"}
                                    valueColor={pos.dbConnectable ? "text-emerald-400" : "text-red-400"} />
                                {pos.sqlVersion && <Row label="SQL" value={pos.sqlVersion.substring(0, 30)} />}
                                <Row label="Transfer Hata" value={pos.stockTransferErrorCount.toString()}
                                    valueColor={pos.stockTransferErrorCount > 0 ? "text-amber-400" : "text-emerald-400"} />
                                <Row label="Export Hata" value={pos.exportErrLogCount.toString()}
                                    valueColor={pos.exportErrLogCount > 0 ? "text-amber-400" : "text-emerald-400"} />
                                {pos.lastSuccessfulTransferAt && (
                                    <Row label="Son Aktarım" value={timeAgo(pos.lastSuccessfulTransferAt)} />
                                )}
                            </InfoCard>

                            {/* Disk */}
                            <InfoCard title="Disk Birikimi" icon={<HardDrive className="w-4 h-4 text-orange-400" />}>
                                <Row label="SEQ Dosya" value={pos.seqFileCount.toString()}
                                    valueColor={pos.seqFileCount > 500 ? "text-amber-400" : "text-zinc-300"} />
                                <Row label="XML Dosya" value={pos.xmlFileCount.toString()}
                                    valueColor={pos.xmlFileCount > 500 ? "text-amber-400" : "text-zinc-300"} />
                                <Row label="Toplam" value={`${pos.seqXmlTotalMB} MB`}
                                    valueColor={pos.seqXmlTotalMB > 500 ? "text-amber-400" : "text-zinc-300"} />
                                {pos.posDataPath && <Row label="Konum" value={pos.posDataPath} />}
                            </InfoCard>

                            {/* Version */}
                            <InfoCard title="Versiyon" icon={<Clock className="w-4 h-4 text-teal-400" />}>
                                {pos.posVersion && <Row label="POS" value={pos.posVersion} />}
                                {pos.jreHome && <Row label="JRE" value={pos.jreHome} />}
                                <Row label="Veri" value={timeAgo(pos.collectedAt)} />
                            </InfoCard>
                        </>
                    )}
                </div>
            )}
        </div>
    );
}

function InfoCard({ title, icon, children }: { title: string; icon: React.ReactNode; children: React.ReactNode }) {
    return (
        <div className="bg-zinc-900/50 border border-zinc-800 rounded-lg p-3">
            <div className="flex items-center gap-2 mb-2">
                {icon}
                <span className="text-xs font-semibold text-zinc-300">{title}</span>
            </div>
            <div className="space-y-1">{children}</div>
        </div>
    );
}

function Row({ label, value, valueColor = "text-zinc-300" }: { label: string; value: string; valueColor?: string }) {
    return (
        <div className="flex items-center justify-between text-xs">
            <span className="text-zinc-500">{label}</span>
            <span className={`font-medium truncate max-w-[140px] ${valueColor}`}>{value}</span>
        </div>
    );
}

function QuickMetric({ icon, label, ok }: { icon: React.ReactNode; label: string; ok: boolean }) {
    return (
        <div className={`flex items-center gap-1 ${ok ? "text-emerald-400" : "text-red-400"}`}>
            {icon}
            <span>{label}</span>
        </div>
    );
}
