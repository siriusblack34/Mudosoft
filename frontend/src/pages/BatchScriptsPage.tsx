import React, { useCallback, useEffect, useMemo, useState } from "react";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import {
    AlertTriangle, CheckCircle, Circle, FileCode, Loader2, Monitor,
    Play, Plus, Search, ShoppingCart, Trash2, Upload, Wifi, XCircle,
} from "lucide-react";

type Phase = "Pending" | "Running" | "Done" | "Error";

interface TargetResult {
    key: string;
    mode: "Agent" | "Agentless";
    deviceId?: string | null;
    ipAddress?: string | null;
    hostname?: string | null;
    storeCode?: string | null;
    phase: Phase;
    output: string;
    error?: string | null;
    startedAtUtc?: string;
    completedAtUtc?: string | null;
    commandId?: string | null;
}

interface ExecutionStatus {
    id: string;
    fileName: string;
    createdBy: string;
    createdAtUtc: string;
    targets: TargetResult[];
}

interface SelectedTarget {
    ip: string;
    storeCode: string;
    hostname?: string;
    deviceType?: string;
    hasAgent: boolean;
    deviceId?: string;
}

const POLL_MS = 2000;

export default function BatchScriptsPage() {
    const [devices, setDevices] = useState<SqlDeviceWithStatus[]>([]);
    const [agentMap, setAgentMap] = useState<Map<string, string>>(new Map()); // ip -> deviceId
    const [loading, setLoading] = useState(true);
    const [search, setSearch] = useState("");
    const [agentFilter, setAgentFilter] = useState<"all" | "withAgent" | "noAgent">("all");
    const [typeFilter, setTypeFilter] = useState<"all" | "PC" | "POS" | "GEÇİCİ">("all");

    const [batFile, setBatFile] = useState<File | null>(null);
    const [batPreview, setBatPreview] = useState<string>("");
    const [batBase64, setBatBase64] = useState<string>("");

    const [targets, setTargets] = useState<SelectedTarget[]>([]);
    const [running, setRunning] = useState(false);
    const [execution, setExecution] = useState<ExecutionStatus | null>(null);
    const [error, setError] = useState<string>("");

    const [manualIp, setManualIp] = useState("");
    const [manualHost, setManualHost] = useState("");
    const [manualError, setManualError] = useState("");

    const parseIpv4 = (value: string): string | null => {
        const parts = value.trim().split(".");
        if (parts.length !== 4) return null;
        const ok = parts.map(p => /^\d{1,3}$/.test(p) && Number(p) >= 0 && Number(p) <= 255);
        return ok.every(Boolean) ? parts.join(".") : null;
    };
    const normalizeIp = (v: string) => parseIpv4(v) ?? v.trim();

    useEffect(() => {
        (async () => {
            try {
                const [sqlDevices, agentDevices] = await Promise.all([
                    apiClient.getSqlDevicesWithStatus(),
                    apiClient.getDevices(),
                ]);

                const storeIps = new Set(sqlDevices.map(d => normalizeIp(d.calculatedIpAddress)));
                const m = new Map<string, string>();
                const agentOnly: SqlDeviceWithStatus[] = [];
                for (const d of agentDevices as any[]) {
                    if (!d.ipAddress) continue;
                    const ip = normalizeIp(d.ipAddress);
                    m.set(ip, d.id);
                    if (!storeIps.has(ip)) {
                        agentOnly.push({
                            deviceId: d.id,
                            storeCode: d.storeCode ?? 0,
                            storeName: d.storeName || "(Agent envanteri)",
                            deviceType: d.type === "POS" ? "POS" : "PC",
                            deviceName: d.hostname || ip,
                            calculatedIpAddress: ip,
                            isOnline: !!d.online,
                            pingReachable: null,
                            sqlReachable: null,
                            lastSeen: d.lastSeen ?? null,
                            isTemporarilyClosed: !!d.isTemporarilyClosed,
                            temporaryCloseReason: d.temporaryCloseReason ?? null,
                        });
                    }
                }
                setDevices([...sqlDevices, ...agentOnly]);
                setAgentMap(m);
            } catch (e) {
                console.error("Cihaz listesi yuklenemedi", e);
            } finally {
                setLoading(false);
            }
        })();
    }, []);

    const hasAgent = useCallback((d: SqlDeviceWithStatus) =>
        agentMap.has(normalizeIp(d.calculatedIpAddress)), [agentMap]);

    const filteredDevices = useMemo(() => devices.filter(d => {
        if (agentFilter === "withAgent" && !hasAgent(d)) return false;
        if (agentFilter === "noAgent" && hasAgent(d)) return false;
        if (typeFilter !== "all" && d.deviceType !== typeFilter) return false;
        if (search) {
            const q = search.toLowerCase();
            return d.deviceName.toLowerCase().includes(q)
                || d.calculatedIpAddress.includes(q)
                || d.storeCode.toString().includes(q)
                || d.storeName.toLowerCase().includes(q);
        }
        return true;
    }), [devices, agentFilter, typeFilter, search, hasAgent]);

    const isSelected = (ip: string) => targets.some(t => t.ip === normalizeIp(ip));

    const toggleDevice = (d: SqlDeviceWithStatus) => {
        const ip = normalizeIp(d.calculatedIpAddress);
        if (isSelected(ip)) {
            setTargets(prev => prev.filter(t => t.ip !== ip));
        } else {
            setTargets(prev => [...prev, {
                ip,
                storeCode: d.storeCode.toString(),
                hostname: d.deviceName,
                deviceType: d.deviceType,
                hasAgent: agentMap.has(ip),
                deviceId: agentMap.get(ip),
            }]);
        }
    };

    const isSelectedKey = (key: string) => targets.some(t => t.ip === key);

    const addManualTarget = async () => {
        const ip = parseIpv4(manualIp);
        if (!manualIp.trim()) return;
        if (!ip) {
            setManualError("Gecerli bir IPv4 adresi girin");
            return;
        }
        if (isSelectedKey(ip)) {
            setManualError("Bu IP zaten secili");
            return;
        }

        // Agent map yerel olabilir; manuel eklemede her zaman canli kontrol et
        let liveAgentMap = agentMap;
        try {
            const fresh = await apiClient.getDevices();
            liveAgentMap = new Map<string, string>();
            for (const d of fresh as any[]) {
                if (d.ipAddress) liveAgentMap.set(normalizeIp(d.ipAddress), d.id);
            }
            setAgentMap(liveAgentMap);
        } catch {
            // canli sorgu basarisizsa cachedeki map kullanilir
        }

        const matched = devices.find(d => normalizeIp(d.calculatedIpAddress) === ip);
        const ag = liveAgentMap.has(ip);
        setTargets(prev => [...prev, {
            ip,
            storeCode: matched?.storeCode.toString() ?? "",
            hostname: manualHost.trim() || matched?.deviceName,
            deviceType: matched?.deviceType ?? "PC",
            hasAgent: ag,
            deviceId: liveAgentMap.get(ip),
        }]);
        setManualIp("");
        setManualHost("");
        setManualError("");
    };

    const handleFile = async (file: File) => {
        if (!file.name.toLowerCase().endsWith(".bat") && !file.name.toLowerCase().endsWith(".cmd")) {
            setError(".bat veya .cmd dosyasi secin");
            return;
        }
        if (file.size > 5 * 1024 * 1024) {
            setError("Dosya 5MB'tan kucuk olmali");
            return;
        }
        setError("");
        setBatFile(file);

        const buffer = await file.arrayBuffer();
        const bytes = new Uint8Array(buffer);
        let binary = "";
        for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
        setBatBase64(btoa(binary));

        // Onizleme: ilk 3KB
        const text = await file.slice(0, 3072).text();
        setBatPreview(text);
    };

    const onFileInput = (e: React.ChangeEvent<HTMLInputElement>) => {
        const f = e.target.files?.[0];
        if (f) handleFile(f);
    };

    const onDrop = (e: React.DragEvent) => {
        e.preventDefault();
        const f = e.dataTransfer.files?.[0];
        if (f) handleFile(f);
    };

    const startRun = async () => {
        if (!batFile || !batBase64) { setError("Once bir bat dosyasi sec"); return; }
        if (targets.length === 0) { setError("Once cihaz sec"); return; }

        setRunning(true);
        setError("");
        setExecution(null);

        try {
            const body = {
                fileName: batFile.name,
                contentBase64: batBase64,
                targets: targets.map(t => t.hasAgent && t.deviceId
                    ? { deviceId: t.deviceId }
                    : { ipAddress: t.ip, storeCode: t.storeCode, hostname: t.hostname }
                ),
            };
            const res = await apiClient.post<{ executionId: string }>("/api/batch/run", body, 60000);
            await pollStatus(res.executionId);
        } catch (e: any) {
            setError(e?.response?.data?.error || e?.message || "Calistirma basarisiz");
            setRunning(false);
        }
    };

    const pollStatus = async (executionId: string) => {
        const fetchOnce = async () => {
            const status = await apiClient.get<ExecutionStatus>(`/api/batch/status/${executionId}`);
            setExecution(status);
            const done = status.targets.every(t => t.phase === "Done" || t.phase === "Error");
            if (done) {
                setRunning(false);
            } else {
                setTimeout(fetchOnce, POLL_MS);
            }
        };
        try {
            await fetchOnce();
        } catch (e: any) {
            setError("Durum okunamadi: " + (e?.message || ""));
            setRunning(false);
        }
    };

    const PhaseIcon = ({ phase }: { phase: Phase }) => {
        switch (phase) {
            case "Running": return <Loader2 className="w-4 h-4 text-blue-400 animate-spin" />;
            case "Done":    return <CheckCircle className="w-4 h-4 text-emerald-400" />;
            case "Error":   return <XCircle className="w-4 h-4 text-red-400" />;
            default:        return <Circle className="w-4 h-4 text-gray-500" />;
        }
    };

    const typeIcon = (t?: string) => t === "POS"
        ? <ShoppingCart className="w-3.5 h-3.5 text-amber-400 shrink-0" />
        : <Monitor className="w-3.5 h-3.5 text-blue-400 shrink-0" />;

    const targetCounts = useMemo(() => ({
        agent: targets.filter(t => t.hasAgent).length,
        agentless: targets.filter(t => !t.hasAgent).length,
    }), [targets]);

    return (
        <div className="p-6 max-w-7xl mx-auto space-y-6">
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                    <div className="p-2 bg-amber-600/20 rounded-xl">
                        <FileCode className="w-5 h-5 text-amber-400" />
                    </div>
                    <div>
                        <h1 className="text-lg font-bold text-ms-text">Acil Durum Bat Calistirma</h1>
                        <p className="text-xs text-ms-text-muted">
                            Bat dosyasini agent'li veya agent'siz cihazlarda calistir (WMI + admin share)
                        </p>
                    </div>
                </div>
                <div className="flex items-center gap-2 text-xs">
                    <span className="px-3 py-1.5 rounded-lg bg-emerald-500/10 border border-emerald-500/20 text-emerald-400">
                        Agent: {targetCounts.agent}
                    </span>
                    <span className="px-3 py-1.5 rounded-lg bg-amber-500/10 border border-amber-500/20 text-amber-400">
                        Agent'siz: {targetCounts.agentless}
                    </span>
                </div>
            </div>

            {/* Bat secimi */}
            <div
                onDrop={onDrop}
                onDragOver={e => e.preventDefault()}
                className="bg-ms-bg-soft border border-dashed border-ms-border rounded-xl p-4"
            >
                {batFile ? (
                    <div className="flex items-center gap-3">
                        <FileCode className="w-5 h-5 text-amber-400 shrink-0" />
                        <div className="flex-1 min-w-0">
                            <div className="text-sm font-medium text-ms-text truncate">{batFile.name}</div>
                            <div className="text-[10px] text-ms-text-muted">{(batFile.size / 1024).toFixed(1)} KB</div>
                        </div>
                        <button
                            onClick={() => { setBatFile(null); setBatBase64(""); setBatPreview(""); }}
                            className="text-gray-500 hover:text-red-400 p-1"
                        >
                            <Trash2 className="w-4 h-4" />
                        </button>
                    </div>
                ) : (
                    <label className="flex items-center justify-center gap-2 cursor-pointer text-sm text-ms-text-muted py-4">
                        <Upload className="w-4 h-4" />
                        <span>.bat veya .cmd dosyasi sec / surukle</span>
                        <input type="file" accept=".bat,.cmd" className="hidden" onChange={onFileInput} />
                    </label>
                )}
                {batPreview && (
                    <pre className="mt-3 max-h-40 overflow-auto bg-black/40 border border-ms-border rounded-lg p-2 text-[11px] font-mono text-ms-text-muted whitespace-pre">
{batPreview}
                    </pre>
                )}
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-5 gap-6">
                {/* Sol: Cihaz secimi */}
                <div className="lg:col-span-3 bg-ms-bg-soft border border-ms-border rounded-xl p-4 space-y-3">
                    <div className="flex items-center gap-2 flex-wrap">
                        <div className="relative flex-1 min-w-[180px]">
                            <Search className="w-3.5 h-3.5 text-gray-500 absolute left-2.5 top-1/2 -translate-y-1/2" />
                            <input
                                type="text"
                                placeholder="Ara... (hostname, IP, magaza)"
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                                className="w-full bg-ms-bg border border-ms-border rounded-lg pl-8 pr-3 py-1.5 text-xs text-ms-text focus:outline-none focus:border-amber-500"
                            />
                        </div>
                        <div className="flex bg-ms-bg border border-ms-border rounded-lg overflow-hidden">
                            {(["all", "withAgent", "noAgent"] as const).map(f => (
                                <button key={f} onClick={() => setAgentFilter(f)}
                                    className={`px-2.5 py-1.5 text-[10px] font-medium ${agentFilter === f ? "bg-amber-600 text-white" : "text-ms-text-muted"}`}>
                                    {f === "all" ? "Hepsi" : f === "withAgent" ? "Agent'li" : "Agent'siz"}
                                </button>
                            ))}
                        </div>
                        <div className="flex bg-ms-bg border border-ms-border rounded-lg overflow-hidden">
                            {(["all", "PC", "POS", "GEÇİCİ"] as const).map(t => (
                                <button key={t} onClick={() => setTypeFilter(t)}
                                    className={`px-2.5 py-1.5 text-[10px] font-medium ${typeFilter === t ? "bg-amber-600 text-white" : "text-ms-text-muted"}`}>
                                    {t === "all" ? "Hepsi" : t}
                                </button>
                            ))}
                        </div>
                    </div>

                    <div className="max-h-[480px] overflow-y-auto">
                        {loading ? (
                            <div className="flex items-center justify-center py-12 text-ms-text-muted">
                                <Loader2 className="w-4 h-4 animate-spin mr-2" /> Yukleniyor...
                            </div>
                        ) : filteredDevices.length === 0 ? (
                            <div className="text-center py-12 text-xs text-ms-text-muted">Cihaz yok</div>
                        ) : (
                            <table className="w-full text-xs">
                                <thead className="sticky top-0 bg-ms-bg-soft z-10">
                                    <tr className="border-b border-ms-border text-[10px] text-ms-text-muted uppercase">
                                        <th className="text-left px-2 py-2 w-6"></th>
                                        <th className="text-left px-2 py-2">Magaza</th>
                                        <th className="text-left px-2 py-2">Cihaz</th>
                                        <th className="text-left px-2 py-2">IP</th>
                                        <th className="text-left px-2 py-2">Agent</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {filteredDevices.map(d => {
                                        const ip = normalizeIp(d.calculatedIpAddress);
                                        const sel = isSelected(ip);
                                        const ag = hasAgent(d);
                                        return (
                                            <tr key={d.deviceId} onClick={() => toggleDevice(d)}
                                                className={`cursor-pointer border-b border-ms-border/30 ${sel ? "bg-amber-600/10" : "hover:bg-white/[0.02]"}`}>
                                                <td className="px-2 py-2">
                                                    <input type="checkbox" checked={sel} readOnly className="w-3.5 h-3.5 accent-amber-500" />
                                                </td>
                                                <td className="px-2 py-2">
                                                    <span className="text-ms-text font-medium">[{d.storeCode}]</span>
                                                    <span className="text-ms-text-muted ml-1 text-[10px]">{d.storeName}</span>
                                                </td>
                                                <td className="px-2 py-2">
                                                    <div className="flex items-center gap-1.5">
                                                        {typeIcon(d.deviceType)}
                                                        <span className="text-ms-text truncate max-w-[140px]">{d.deviceName}</span>
                                                    </div>
                                                </td>
                                                <td className="px-2 py-2 font-mono text-ms-text-muted">{d.calculatedIpAddress}</td>
                                                <td className="px-2 py-2">
                                                    {ag ? (
                                                        <span className="text-[9px] px-1.5 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                                                            Agent
                                                        </span>
                                                    ) : (
                                                        <span className="text-[9px] px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-400 border border-amber-500/20">
                                                            WMI
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

                    {/* Manuel ekleme — listede olmayan cihazlar (merkez ofis laptoplari vb.) */}
                    <div className="border-t border-ms-border pt-3">
                        <div className="flex items-center justify-between mb-1.5">
                            <span className="text-[10px] text-ms-text-muted uppercase tracking-wider">Manuel Ekle (listede olmayan cihaz)</span>
                            <span className="text-[9px] text-ms-text-muted">Agent kuruluysa otomatik tespit edilir</span>
                        </div>
                        <div className="flex items-center gap-2">
                            <Wifi className="w-3.5 h-3.5 text-gray-500 shrink-0" />
                            <input type="text" placeholder="IP Adresi" value={manualIp}
                                onChange={e => { setManualIp(e.target.value); setManualError(""); }}
                                onKeyDown={e => e.key === "Enter" && addManualTarget()}
                                className="flex-1 bg-ms-bg border border-ms-border rounded-lg px-2.5 py-1.5 text-xs text-ms-text placeholder:text-ms-text-muted/50 focus:outline-none focus:border-amber-500" />
                            <input type="text" placeholder="Hostname (opsiyonel)" value={manualHost}
                                onChange={e => setManualHost(e.target.value)}
                                onKeyDown={e => e.key === "Enter" && addManualTarget()}
                                className="w-44 bg-ms-bg border border-ms-border rounded-lg px-2.5 py-1.5 text-xs text-ms-text placeholder:text-ms-text-muted/50 focus:outline-none focus:border-amber-500" />
                            <button onClick={addManualTarget} disabled={!manualIp.trim()}
                                className="p-1.5 bg-amber-600 hover:bg-amber-500 disabled:opacity-40 text-white rounded-lg transition-colors">
                                <Plus className="w-3.5 h-3.5" />
                            </button>
                        </div>
                        {manualError && (
                            <p className="mt-1.5 pl-5 text-[10px] text-red-400">{manualError}</p>
                        )}
                    </div>
                </div>

                {/* Sag: Hedefler ve calistirma */}
                <div className="lg:col-span-2 bg-ms-bg-soft border border-ms-border rounded-xl p-4 space-y-3">
                    <div className="flex items-center justify-between">
                        <span className="text-sm font-medium text-ms-text">Hedefler ({targets.length})</span>
                        <button
                            disabled={running || !batFile || targets.length === 0}
                            onClick={startRun}
                            className="flex items-center gap-1.5 px-3 py-1.5 bg-amber-600 hover:bg-amber-500 disabled:opacity-40 text-white text-xs font-medium rounded-lg"
                        >
                            {running ? (
                                <><Loader2 className="w-3.5 h-3.5 animate-spin" /> Calisiyor...</>
                            ) : (
                                <><Play className="w-3.5 h-3.5" /> Calistir</>
                            )}
                        </button>
                    </div>

                    {error && (
                        <div className="flex items-center gap-2 p-2 bg-red-500/10 border border-red-500/20 rounded text-xs text-red-400">
                            <AlertTriangle className="w-3.5 h-3.5 shrink-0" /> {error}
                        </div>
                    )}

                    {!execution && targets.length === 0 && (
                        <div className="text-center py-16 text-xs text-ms-text-muted">Soldan cihaz sec</div>
                    )}

                    {!execution && targets.length > 0 && (
                        <div className="max-h-[400px] overflow-y-auto space-y-1">
                            {targets.map((t, i) => (
                                <div key={t.ip} className="flex items-center gap-2 px-2 py-1.5 bg-ms-bg rounded-lg">
                                    {typeIcon(t.deviceType)}
                                    <div className="flex-1 min-w-0">
                                        <div className="text-xs text-ms-text truncate">{t.hostname || t.ip}</div>
                                        <div className="text-[10px] text-ms-text-muted">{t.ip} · M:{t.storeCode}</div>
                                    </div>
                                    <span className={`text-[9px] px-1.5 py-0.5 rounded border ${t.hasAgent ? "bg-emerald-500/10 text-emerald-400 border-emerald-500/20" : "bg-amber-500/10 text-amber-400 border-amber-500/20"}`}>
                                        {t.hasAgent ? "Agent" : "WMI"}
                                    </span>
                                    <button onClick={() => setTargets(prev => prev.filter((_, j) => j !== i))}
                                        className="text-gray-500 hover:text-red-400">
                                        <Trash2 className="w-3 h-3" />
                                    </button>
                                </div>
                            ))}
                        </div>
                    )}

                    {execution && (
                        <div className="max-h-[480px] overflow-y-auto space-y-2">
                            {execution.targets.map(t => (
                                <div key={t.key} className="border border-ms-border rounded-lg p-2 space-y-1.5 bg-ms-bg">
                                    <div className="flex items-center gap-2">
                                        <PhaseIcon phase={t.phase} />
                                        <div className="flex-1 min-w-0">
                                            <div className="text-xs text-ms-text truncate">
                                                {t.hostname || t.ipAddress || t.deviceId}
                                            </div>
                                            <div className="text-[10px] text-ms-text-muted">
                                                {t.ipAddress} · M:{t.storeCode} · {t.mode}
                                            </div>
                                        </div>
                                    </div>
                                    {t.error && (
                                        <div className="text-[10px] text-red-400">{t.error}</div>
                                    )}
                                    {t.output && (
                                        <pre className="bg-black/40 border border-ms-border rounded p-1.5 text-[10px] font-mono text-ms-text-muted max-h-32 overflow-auto whitespace-pre-wrap">
{t.output}
                                        </pre>
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
