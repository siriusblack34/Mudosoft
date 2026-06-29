import React, { useState, useCallback, useEffect } from "react";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import {
    Download, Loader2, CheckCircle, XCircle, Circle, AlertTriangle,
    Search, Monitor, ShoppingCart, Plus, Trash2, Wifi, Radar,
} from "lucide-react";

interface InstallStep {
    name: string;
    state: "pending" | "running" | "done" | "error" | "warn";
    detail: string;
}

interface InstallStatus {
    id: string;
    ipAddress: string;
    storeCode: string;
    phase: "pending" | "queued" | "running" | "done" | "warn" | "error";
    error?: string;
    steps: InstallStep[];
    startedAt: string;
    completedAt?: string;
}

interface InstallTarget {
    ip: string;
    storeCode: string;
    hostname?: string;
    deviceType?: string;
    status?: InstallStatus;
    polling?: boolean;
}

export default function RemoteInstallPage() {
    const [devices, setDevices] = useState<SqlDeviceWithStatus[]>([]);
    const [agentDeviceIps, setAgentDeviceIps] = useState<Set<string>>(new Set());
    const [loadingDevices, setLoadingDevices] = useState(true);
    const [search, setSearch] = useState("");
    const [typeFilter, setTypeFilter] = useState<"all" | "PC" | "POS" | "GEÇİCİ">("all");
    const [agentFilter, setAgentFilter] = useState<"noAgent" | "all" | "withAgent">("noAgent");
    const [targets, setTargets] = useState<InstallTarget[]>([]);
    const [installing, setInstalling] = useState(false);
    const [manualIp, setManualIp] = useState("");
    const [manualStore, setManualStore] = useState("");
    const [manualError, setManualError] = useState("");

    const parseIpv4 = (value: string): string | null => {
        const parts = value.trim().split(".");
        if (parts.length !== 4) return null;

        const normalized = parts.map(part => {
            if (!/^\d{1,3}$/.test(part)) return null;
            const octet = Number(part);
            if (octet < 0 || octet > 255) return null;
            return String(octet);
        });

        return normalized.every(Boolean) ? normalized.join(".") : null;
    };

    const normalizeIp = (value: string) => parseIpv4(value) ?? value.trim();
    const normalizeStoreCode = (value?: string) => (value ?? "").replace(/\D/g, "");

    // Cihaz listesini yükle — SQL Query endpoint (tüm mağaza envanteri)
    useEffect(() => {
        (async () => {
            try {
                const [sqlDevices, agentDevices] = await Promise.all([
                    apiClient.getSqlDevicesWithStatus(),
                    apiClient.getDevices(), // agent'lı cihazlar
                ]);
                setDevices(sqlDevices);
                // Agent kurulu IP'leri set'e al
                const ips = new Set(agentDevices.map((d: any) => d.ipAddress ? normalizeIp(d.ipAddress) : "").filter(Boolean));
                setAgentDeviceIps(ips);
            } catch (err) {
                console.error("Cihaz listesi yuklenemedi", err);
            } finally {
                setLoadingDevices(false);
            }
        })();
    }, []);

    const hasAgent = (d: SqlDeviceWithStatus) => agentDeviceIps.has(normalizeIp(d.calculatedIpAddress));

    const noAgentCount = devices.filter(d => !hasAgent(d)).length;
    const withAgentCount = devices.filter(d => hasAgent(d)).length;

    const filteredDevices = devices.filter(d => {
        const dt = d.deviceType?.toUpperCase();
        if (dt === "ROUTER" || dt?.startsWith("YAZICI")) return false;
        if (agentFilter === "noAgent" && hasAgent(d)) return false;
        if (agentFilter === "withAgent" && !hasAgent(d)) return false;
        if (typeFilter !== "all" && d.deviceType !== typeFilter) return false;
        if (search) {
            const q = search.toLowerCase();
            return d.deviceName.toLowerCase().includes(q)
                || d.calculatedIpAddress.includes(q)
                || d.storeCode.toString().includes(q)
                || d.storeName.toLowerCase().includes(q);
        }
        return true;
    });

    const isSelected = (ip: string) => targets.some(t => t.ip === normalizeIp(ip));

    const toggleDevice = (d: SqlDeviceWithStatus) => {
        const ip = d.calculatedIpAddress;
        if (isSelected(ip)) {
            setTargets(prev => prev.filter(t => t.ip !== ip));
        } else {
            setTargets(prev => [...prev, {
                ip,
                storeCode: d.storeCode.toString(),
                hostname: d.deviceName,
                deviceType: d.deviceType,
            }]);
        }
    };

    const selectAllFiltered = () => {
        const toAdd = filteredDevices.filter(d => !isSelected(d.calculatedIpAddress));
        setTargets(prev => [...prev, ...toAdd.map(d => ({
            ip: d.calculatedIpAddress,
            storeCode: d.storeCode.toString(),
            hostname: d.deviceName,
            deviceType: d.deviceType,
        }))]);
    };

    const clearSelection = () => {
        setTargets(prev => prev.filter(t => t.polling || t.status));
    };

    const addManualTarget = () => {
        const ip = parseIpv4(manualIp);
        if (!manualIp.trim()) return;
        if (!ip) {
            setManualError("Gecerli bir IPv4 adresi girin");
            return;
        }
        if (isSelected(ip)) {
            setManualError("Bu IP zaten secili");
            return;
        }

        const matchedDevice = devices.find(d => normalizeIp(d.calculatedIpAddress) === ip);
        setTargets(prev => [...prev, {
            ip,
            storeCode: normalizeStoreCode(manualStore) || matchedDevice?.storeCode.toString() || "",
            hostname: matchedDevice?.deviceName,
            deviceType: matchedDevice?.deviceType,
        }]);
        setManualIp("");
        setManualStore("");
        setManualError("");
    };

    const pollStatus = useCallback(async (ip: string) => {
        const poll = async () => {
            try {
                const status = await apiClient.get<InstallStatus>(
                    `/api/agent/remote-install/status?ip=${encodeURIComponent(ip)}`
                );
                const active = status.phase === "running" || status.phase === "queued";
                setTargets(prev => prev.map(t =>
                    t.ip === ip ? { ...t, status, polling: active } : t
                ));
                if (active) {
                    setTimeout(poll, status.phase === "queued" ? 2000 : 1500);
                }
            } catch (err: any) {
                const msg = err?.message || "Kurulum durumu okunamadi";
                setTargets(prev => prev.map(t =>
                    t.ip === ip ? {
                        ...t,
                        polling: false,
                        status: {
                            id: "",
                            ipAddress: ip,
                            storeCode: t.storeCode,
                            phase: "error",
                            error: msg,
                            steps: [],
                            startedAt: new Date().toISOString()
                        }
                    } : t
                ));
            }
        };
        poll();
    }, []);

    const startInstall = async (idx: number) => {
        const target = targets[idx];
        if (!target) return;
        const ip = normalizeIp(target.ip);
        if (!ip) return;
        setTargets(prev => prev.map(t =>
            t.ip === ip ? { ...t, status: undefined, polling: true } : t
        ));
        try {
            await apiClient.post("/api/agent/remote-install", {
                ipAddress: ip,
                storeCode: normalizeStoreCode(target.storeCode) || undefined,
            }, 300000);
            pollStatus(ip);
        } catch (err: any) {
            // POST timeout/abort durumunda backend kurulumu zaten arka planda baslatmis olabilir.
            // Status'u sorgulamayi dene; bulunursa polling ile takip et, yoksa gercek hatayi goster.
            const isAbort = err?.name === "AbortError" || /aborted/i.test(err?.message || "");
            try {
                const status = await apiClient.get<InstallStatus>(
                    `/api/agent/remote-install/status?ip=${encodeURIComponent(ip)}`
                );
                const active2 = status.phase === "running" || status.phase === "queued";
                setTargets(prev => prev.map(t =>
                    t.ip === ip ? { ...t, status, polling: active2 } : t
                ));
                if (active2) pollStatus(ip);
                return;
            } catch {
                // Status yoksa POST gercekten basarisiz olmus
            }
            const msg = isAbort
                ? "Sunucu yanit vermedi (zaman asimi). Kurulum baslatilmadi."
                : err?.response?.data?.error || err?.message || "Kurulum baslatilamadi";
            setTargets(prev => prev.map(t =>
                t.ip === ip ? {
                    ...t, polling: false,
                    status: {
                        id: "", ipAddress: ip, storeCode: target.storeCode,
                        phase: "error", error: msg, steps: [], startedAt: new Date().toISOString()
                    }
                } : t
            ));
        }
    };

    const startAll = async () => {
        setInstalling(true);
        for (let i = 0; i < targets.length; i++) {
            if (normalizeIp(targets[i].ip) && !targets[i].status) {
                await startInstall(i);
                await new Promise(r => setTimeout(r, 300));
            }
        }
        setInstalling(false);
    };

    const StepIcon = ({ state }: { state: string }) => {
        switch (state) {
            case "running": return <Loader2 className="w-4 h-4 text-blue-400 animate-spin" />;
            case "done":    return <CheckCircle className="w-4 h-4 text-emerald-400" />;
            case "warn":    return <AlertTriangle className="w-4 h-4 text-amber-400" />;
            case "error":   return <XCircle className="w-4 h-4 text-red-400" />;
            default:        return <Circle className="w-4 h-4 text-gray-600" />;
        }
    };

    const typeIcon = (type: string) => {
        if (type === "POS") return <ShoppingCart className="w-3.5 h-3.5 text-amber-400 shrink-0" />;
        return <Monitor className="w-3.5 h-3.5 text-blue-400 shrink-0" />;
    };

    const typeBadge = (type: string) => {
        const colors: Record<string, string> = {
            "PC": "bg-blue-500/10 text-blue-400 border-blue-500/20",
            "POS": "bg-amber-500/10 text-amber-400 border-amber-500/20",
            "GEÇİCİ": "bg-purple-500/10 text-purple-400 border-purple-500/20",
        };
        return colors[type] || "bg-gray-500/10 text-gray-400 border-gray-500/20";
    };

    return (
        <div className="p-6 max-w-6xl mx-auto space-y-6">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                    <div className="p-2 bg-violet-600/20 rounded-xl">
                        <Download className="w-5 h-5 text-violet-400" />
                    </div>
                    <div>
                        <h1 className="text-lg font-bold text-ms-text">Uzaktan Agent Kurulumu</h1>
                        <p className="text-xs text-ms-text-muted">Magaza PC ve kasalarina agent kur</p>
                    </div>
                </div>
                <div className="flex items-center gap-3 text-xs">
                    <div className="px-3 py-1.5 rounded-lg bg-white/5 border border-ms-border">
                        <span className="text-ms-text font-medium">{devices.length}</span>
                        <span className="text-ms-text-muted ml-1">Toplam</span>
                    </div>
                    <div className="px-3 py-1.5 rounded-lg bg-emerald-500/10 border border-emerald-500/20">
                        <span className="text-emerald-400 font-medium">{withAgentCount}</span>
                        <span className="text-emerald-400/60 ml-1">Agent'li</span>
                    </div>
                    <div className="px-3 py-1.5 rounded-lg bg-amber-500/10 border border-amber-500/20">
                        <span className="text-amber-400 font-medium">{noAgentCount}</span>
                        <span className="text-amber-400/60 ml-1">Agent'siz</span>
                    </div>
                </div>
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-5 gap-6">
                {/* Left: Device list (3 cols) */}
                <div className="lg:col-span-3 bg-ms-bg-soft border border-ms-border rounded-xl p-4 space-y-3">
                    <div className="flex items-center justify-between">
                        <span className="text-sm font-medium text-ms-text">Magaza Cihazlari</span>
                        <div className="flex items-center gap-2">
                            <button onClick={selectAllFiltered} className="text-[10px] text-violet-400 hover:underline">
                                Tumunu Sec ({filteredDevices.length})
                            </button>
                            <button onClick={clearSelection} className="text-[10px] text-gray-400 hover:underline">
                                Temizle
                            </button>
                        </div>
                    </div>

                    {/* Filters */}
                    <div className="flex items-center gap-2 flex-wrap">
                        <div className="relative flex-1 min-w-[180px]">
                            <Search className="w-3.5 h-3.5 text-gray-500 absolute left-2.5 top-1/2 -translate-y-1/2" />
                            <input
                                type="text"
                                placeholder="Ara... (hostname, IP, magaza)"
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                                className="w-full bg-ms-bg border border-ms-border rounded-lg pl-8 pr-3 py-1.5 text-xs text-ms-text placeholder:text-ms-text-muted/50 focus:outline-none focus:border-violet-500"
                            />
                        </div>
                        <div className="flex bg-ms-bg border border-ms-border rounded-lg overflow-hidden">
                            {([
                                { key: "noAgent" as const, label: "Agent'siz" },
                                { key: "all" as const, label: "Hepsi" },
                                { key: "withAgent" as const, label: "Agent'li" },
                            ]).map(f => (
                                <button
                                    key={f.key}
                                    onClick={() => setAgentFilter(f.key)}
                                    className={`px-2.5 py-1.5 text-[10px] font-medium transition-colors ${
                                        agentFilter === f.key
                                            ? "bg-violet-600 text-white"
                                            : "text-ms-text-muted hover:text-ms-text"
                                    }`}
                                >
                                    {f.label}
                                </button>
                            ))}
                        </div>
                        <div className="flex bg-ms-bg border border-ms-border rounded-lg overflow-hidden">
                            {(["all", "PC", "POS", "GEÇİCİ"] as const).map(t => (
                                <button
                                    key={t}
                                    onClick={() => setTypeFilter(t)}
                                    className={`px-2.5 py-1.5 text-[10px] font-medium transition-colors ${
                                        typeFilter === t
                                            ? "bg-violet-600 text-white"
                                            : "text-ms-text-muted hover:text-ms-text"
                                    }`}
                                >
                                    {t === "all" ? "Hepsi" : t}
                                </button>
                            ))}
                        </div>
                    </div>

                    {/* Device table */}
                    <div className="max-h-[500px] overflow-y-auto">
                        {loadingDevices ? (
                            <div className="flex items-center justify-center py-12 text-ms-text-muted">
                                <Loader2 className="w-4 h-4 animate-spin mr-2" /> Yukleniyor...
                            </div>
                        ) : filteredDevices.length === 0 ? (
                            <div className="text-center py-12 text-xs text-ms-text-muted">
                                Filtreye uyan cihaz bulunamadi
                            </div>
                        ) : (
                            <table className="w-full text-xs">
                                <thead className="sticky top-0 bg-ms-bg-soft z-10">
                                    <tr className="border-b border-ms-border text-[10px] text-ms-text-muted uppercase">
                                        <th className="text-left px-2 py-2 w-8"></th>
                                        <th className="text-left px-2 py-2">Magaza</th>
                                        <th className="text-left px-2 py-2">Cihaz</th>
                                        <th className="text-left px-2 py-2">IP</th>
                                        <th className="text-left px-2 py-2">Tip</th>
                                        <th className="text-left px-2 py-2">Durum</th>
                                        <th className="text-left px-2 py-2">Windows</th>
                                        <th className="text-left px-2 py-2">Agent</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {filteredDevices.map(device => {
                                        const selected = isSelected(device.calculatedIpAddress);
                                        const agent = hasAgent(device);
                                        return (
                                            <tr
                                                key={device.deviceId}
                                                onClick={() => toggleDevice(device)}
                                                className={`cursor-pointer border-b border-ms-border/30 transition-colors ${
                                                    selected ? "bg-violet-600/10" : "hover:bg-white/[0.02]"
                                                }`}
                                            >
                                                <td className="px-2 py-2">
                                                    <input type="checkbox" checked={selected} readOnly
                                                        className="w-3.5 h-3.5 rounded accent-violet-500" />
                                                </td>
                                                <td className="px-2 py-2">
                                                    <div>
                                                        <span className="text-ms-text font-medium">[{device.storeCode}]</span>
                                                        <span className="text-ms-text-muted ml-1 text-[10px]">{device.storeName}</span>
                                                    </div>
                                                </td>
                                                <td className="px-2 py-2">
                                                    <div className="flex items-center gap-1.5">
                                                        {typeIcon(device.deviceType)}
                                                        <span className="text-ms-text truncate max-w-[140px]">{device.deviceName}</span>
                                                    </div>
                                                </td>
                                                <td className="px-2 py-2 font-mono text-ms-text-muted">
                                                    {device.calculatedIpAddress}
                                                </td>
                                                <td className="px-2 py-2">
                                                    <span className={`text-[9px] px-1.5 py-0.5 rounded border ${typeBadge(device.deviceType)}`}>
                                                        {device.deviceType}
                                                    </span>
                                                </td>
                                                <td className="px-2 py-2">
                                                    <span className={`w-2 h-2 rounded-full inline-block ${
                                                        device.isOnline ? "bg-emerald-400" : "bg-gray-600"
                                                    }`} />
                                                </td>
                                                <td className="px-2 py-2">
                                                    {device.windowsVersion ? (
                                                        <span className={`text-[9px] px-1.5 py-0.5 rounded border font-semibold ${
                                                            device.windowsVersion.startsWith("Win11") ? "bg-violet-500/10 text-violet-300 border-violet-500/20" :
                                                            device.windowsVersion.startsWith("Win10") ? "bg-sky-500/10 text-sky-300 border-sky-500/20" :
                                                            device.windowsVersion.startsWith("Win 7") ? "bg-amber-500/10 text-amber-400 border-amber-500/20" :
                                                            device.windowsVersion.includes("Server") ? "bg-emerald-500/10 text-emerald-300 border-emerald-500/20" :
                                                            "bg-slate-500/10 text-slate-400 border-slate-500/20"
                                                        }`}>
                                                            {device.windowsVersion}
                                                        </span>
                                                    ) : (
                                                        <span className="text-[9px] text-gray-600">—</span>
                                                    )}
                                                </td>
                                                <td className="px-2 py-2">
                                                    {agent ? (
                                                        <span className="text-[9px] px-1.5 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                                                            Kurulu
                                                        </span>
                                                    ) : (
                                                        <span className="text-[9px] px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-400 border border-amber-500/20">
                                                            Yok
                                                        </span>
                                                    )}
                                                </td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        )}
                    </div>

                    {/* Manual add */}
                    <div className="border-t border-ms-border pt-3">
                        <span className="text-[10px] text-ms-text-muted uppercase tracking-wider">Manuel Ekle</span>
                        <div className="flex items-center gap-2 mt-1.5">
                            <Wifi className="w-3.5 h-3.5 text-gray-500 shrink-0" />
                            <input type="text" placeholder="IP Adresi" value={manualIp}
                                onChange={e => { setManualIp(e.target.value); setManualError(""); }}
                                onKeyDown={e => e.key === "Enter" && addManualTarget()}
                                className="flex-1 bg-ms-bg border border-ms-border rounded-lg px-2.5 py-1.5 text-xs text-ms-text placeholder:text-ms-text-muted/50 focus:outline-none focus:border-violet-500" />
                            <input type="text" placeholder="Magaza" value={manualStore}
                                onChange={e => { setManualStore(normalizeStoreCode(e.target.value)); setManualError(""); }}
                                onKeyDown={e => e.key === "Enter" && addManualTarget()}
                                className="w-20 bg-ms-bg border border-ms-border rounded-lg px-2.5 py-1.5 text-xs text-ms-text placeholder:text-ms-text-muted/50 focus:outline-none focus:border-violet-500" />
                            <button onClick={addManualTarget} disabled={!manualIp.trim()}
                                className="p-1.5 bg-violet-600 hover:bg-violet-500 disabled:opacity-40 text-white rounded-lg transition-colors">
                                <Plus className="w-3.5 h-3.5" />
                            </button>
                        </div>
                        {manualError && (
                            <p className="mt-1.5 pl-5 text-[10px] text-red-400">{manualError}</p>
                        )}
                    </div>
                </div>

                {/* Right: Selected targets (2 cols) */}
                <div className="lg:col-span-2 bg-ms-bg-soft border border-ms-border rounded-xl p-4 space-y-3">
                    <div className="flex items-center justify-between">
                        <span className="text-sm font-medium text-ms-text">
                            Secilen Hedefler ({targets.length})
                        </span>
                        {targets.filter(t => !t.status).length > 0 && (
                            <button onClick={startAll} disabled={installing}
                                className="flex items-center gap-1.5 px-3 py-1.5 bg-violet-600 hover:bg-violet-500 disabled:opacity-40 text-white text-xs font-medium rounded-lg transition-colors">
                                {installing ? (
                                    <><Loader2 className="w-3.5 h-3.5 animate-spin" /> Kuruluyor...</>
                                ) : (
                                    <><Download className="w-3.5 h-3.5" /> Kur ({targets.filter(t => !t.status).length})</>
                                )}
                            </button>
                        )}
                    </div>

                    {targets.length === 0 ? (
                        <div className="text-center py-16 text-xs text-ms-text-muted">
                            Soldan cihaz secin veya manuel IP ekleyin
                        </div>
                    ) : (
                        <div className="max-h-[550px] overflow-y-auto space-y-2 pr-1">
                            {targets.map((target, idx) => (
                                <div key={target.ip} className="space-y-2">
                                    <div className="flex items-center gap-2 px-2 py-1.5">
                                        {typeIcon(target.deviceType || "PC")}
                                        <div className="flex-1 min-w-0">
                                            <span className="text-xs font-medium text-ms-text">
                                                {target.hostname || target.ip}
                                            </span>
                                            {target.hostname && (
                                                <span className="text-[10px] text-ms-text-muted ml-2">{target.ip}</span>
                                            )}
                                            {target.storeCode && (
                                                <span className="text-[10px] text-ms-text-muted ml-2">M:{target.storeCode}</span>
                                            )}
                                        </div>
                                        {!target.polling && !target.status && (
                                            <>
                                                <button onClick={() => startInstall(idx)}
                                                    className="px-2.5 py-1 bg-violet-600 hover:bg-violet-500 text-white text-[10px] font-medium rounded-lg transition-colors">
                                                    Kur
                                                </button>
                                                <button onClick={() => setTargets(prev => prev.filter((_, i) => i !== idx))}
                                                    className="p-1 text-gray-500 hover:text-red-400 rounded transition-colors">
                                                    <Trash2 className="w-3 h-3" />
                                                </button>
                                            </>
                                        )}
                                        {target.status?.phase === "done" && <CheckCircle className="w-4 h-4 text-emerald-400 shrink-0" />}
                                        {target.status?.phase === "warn" && <AlertTriangle className="w-4 h-4 text-amber-400 shrink-0" />}
                                        {target.status?.phase === "error" && <XCircle className="w-4 h-4 text-red-400 shrink-0" />}
                                        {target.polling && <Loader2 className="w-4 h-4 text-blue-400 animate-spin shrink-0" />}
                                    </div>

                                    {target.status?.phase === "queued" && (
                                        <div className="ml-6 p-2.5 rounded-lg border bg-violet-500/5 border-violet-500/20 flex items-center gap-2">
                                            <Loader2 className="w-3.5 h-3.5 text-violet-400 animate-spin shrink-0" />
                                            <span className="text-xs text-violet-400">Kuyrukta bekliyor — sıra gelince başlayacak</span>
                                        </div>
                                    )}
                                    {target.status && target.status.phase !== "queued" && (
                                        <div className={`ml-6 p-3 rounded-lg border ${
                                            target.status.phase === "done"  ? "bg-emerald-500/5 border-emerald-500/20" :
                                            target.status.phase === "warn"  ? "bg-amber-500/5 border-amber-500/20" :
                                            target.status.phase === "error" ? "bg-red-500/5 border-red-500/20" :
                                            "bg-blue-500/5 border-blue-500/20"
                                        }`}>
                                            <div className="space-y-1.5">
                                                {target.status.steps.map((step, sIdx) => (
                                                    <div key={sIdx} className="flex items-center gap-2">
                                                        <StepIcon state={step.state} />
                                                        <span className={`text-xs ${
                                                            step.state === "done"  ? "text-emerald-400" :
                                                            step.state === "error" ? "text-red-400" :
                                                            step.state === "warn"  ? "text-amber-400" :
                                                            step.state === "running" ? "text-blue-400" :
                                                            "text-gray-500"
                                                        }`}>{step.name}</span>
                                                        {step.detail && (
                                                            <span className="text-[10px] text-ms-text-muted ml-1">— {step.detail}</span>
                                                        )}
                                                    </div>
                                                ))}
                                            </div>
                                            {target.status.error && (
                                                <p className="text-xs text-red-400 mt-2">{target.status.error}</p>
                                            )}
                                        </div>
                                    )}
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}
