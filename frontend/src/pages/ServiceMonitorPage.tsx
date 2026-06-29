import React, { useEffect, useState, useCallback } from "react";
import { apiClient } from "../lib/apiClient";
import {
    Shield, RefreshCw, Play, Square, Plus, Trash2, Save,
    RotateCcw, Loader2, AlertTriangle, CheckCircle2, Clock,
    ChevronDown, ChevronUp, Activity, Settings, Monitor,
} from "lucide-react";
import type { StoreServiceIncident } from "../lib/apiClient";

// ── Types ────────────────────────────────────────────────────────────────────

interface ServiceDef {
    name: string;
    displayName: string;
}

interface MonitorConfig {
    enabled: boolean;
    intervalSeconds: number;
    confirmationThreshold: number;
    autoStartStoppedServices: boolean;
    maxConcurrency: number;
    wmiTimeoutSeconds: number;
    deviceTypes: string[];
    services: ServiceDef[];
}

interface ConfigResponse {
    config: MonitorConfig;
    lastScanAt: string | null;
    configFileExists: boolean;
}

type ControlState = "idle" | "loading" | "ok" | "error";

const DEVICE_TYPE_OPTIONS = ["PC", "Kasa-1", "Kasa-2", "Kasa-3", "GECICI"];

// ── Helpers ──────────────────────────────────────────────────────────────────

const fmtAge = (dt: string | null | undefined): string => {
    if (!dt) return "—";
    const m = Math.floor((Date.now() - new Date(dt).getTime()) / 60000);
    if (m < 1) return "Az önce";
    if (m < 60) return `${m} dk önce`;
    const h = Math.floor(m / 60);
    if (h < 24) return `${h} sa önce`;
    return `${Math.floor(h / 24)} gün önce`;
};

const severityColor = (s: string) =>
    s === "Critical" ? "text-rose-400 bg-rose-500/10 border-rose-500/30" :
    s === "Warning"  ? "text-amber-400 bg-amber-500/10 border-amber-500/30" :
                      "text-slate-400 bg-slate-500/10 border-slate-500/30";

// ═══════════════════════════════════════════════════════════════════════════════
// PAGE
// ═══════════════════════════════════════════════════════════════════════════════
export default function ServiceMonitorPage() {
    const [cfg, setCfg] = useState<MonitorConfig | null>(null);
    const [draft, setDraft] = useState<MonitorConfig | null>(null);
    const [lastScanAt, setLastScanAt] = useState<string | null>(null);
    const [configFileExists, setConfigFileExists] = useState(false);

    const [incidents, setIncidents] = useState<StoreServiceIncident[]>([]);
    const [loadingCfg, setLoadingCfg] = useState(true);
    const [loadingIncidents, setLoadingIncidents] = useState(true);
    const [savingCfg, setSavingCfg] = useState(false);
    const [triggering, setTriggering] = useState(false);

    const [controlState, setControlState] = useState<Record<string, ControlState>>({});

    const [configOpen, setConfigOpen] = useState(true);
    const [newSvcName, setNewSvcName] = useState("");
    const [newSvcDisplay, setNewSvcDisplay] = useState("");

    // ── Load ─────────────────────────────────────────────────────────────────

    const loadConfig = useCallback(async () => {
        try {
            setLoadingCfg(true);
            const res = await apiClient.get<ConfigResponse>("/api/service-monitor/config");
            setCfg(res.config);
            setDraft(JSON.parse(JSON.stringify(res.config)));
            setLastScanAt(res.lastScanAt);
            setConfigFileExists(res.configFileExists);
        } catch (err) { console.error(err); }
        finally { setLoadingCfg(false); }
    }, []);

    const loadIncidents = useCallback(async () => {
        try {
            setLoadingIncidents(true);
            const res = await apiClient.getActiveServiceIncidents();
            setIncidents(res);
        } catch (err) { console.error(err); }
        finally { setLoadingIncidents(false); }
    }, []);

    useEffect(() => { loadConfig(); loadIncidents(); }, []);

    // ── Config edits ─────────────────────────────────────────────────────────

    const isDirty = JSON.stringify(cfg) !== JSON.stringify(draft);

    const toggleDeviceType = (dt: string) => {
        if (!draft) return;
        const types = draft.deviceTypes.includes(dt)
            ? draft.deviceTypes.filter(t => t !== dt)
            : [...draft.deviceTypes, dt];
        setDraft({ ...draft, deviceTypes: types });
    };

    const addService = () => {
        if (!draft || !newSvcName.trim()) return;
        setDraft({
            ...draft,
            services: [...draft.services, { name: newSvcName.trim(), displayName: newSvcDisplay.trim() || newSvcName.trim() }]
        });
        setNewSvcName(""); setNewSvcDisplay("");
    };

    const removeService = (idx: number) => {
        if (!draft) return;
        setDraft({ ...draft, services: draft.services.filter((_, i) => i !== idx) });
    };

    const saveConfig = async () => {
        if (!draft) return;
        setSavingCfg(true);
        try {
            await apiClient.put("/api/service-monitor/config", draft);
            setCfg(JSON.parse(JSON.stringify(draft)));
            setConfigFileExists(true);
        } catch (err) { console.error(err); }
        finally { setSavingCfg(false); }
    };

    const resetConfig = async () => {
        if (!window.confirm("Varsayılan ayarlara sıfırlanacak. Emin misiniz?")) return;
        await apiClient.delete("/api/service-monitor/config");
        setConfigFileExists(false);
        await loadConfig();
    };

    // ── Trigger scan ─────────────────────────────────────────────────────────

    const triggerScan = async () => {
        setTriggering(true);
        try {
            await apiClient.post("/api/service-monitor/trigger");
            setTimeout(() => { loadIncidents(); loadConfig(); }, 5000);
            setTimeout(() => { loadIncidents(); loadConfig(); setTriggering(false); }, 15000);
        } catch { setTriggering(false); }
    };

    // ── Remote service control ────────────────────────────────────────────────

    const controlService = async (incident: StoreServiceIncident, action: "start" | "stop") => {
        const key = `${incident.ipAddress}-${incident.serviceName}-${action}`;
        setControlState(prev => ({ ...prev, [key]: "loading" }));
        try {
            const res = await apiClient.post<{ success: boolean; output: string }>(
                "/api/service-monitor/control",
                { ipAddress: incident.ipAddress, serviceName: incident.serviceName, action },
                30_000
            );
            setControlState(prev => ({ ...prev, [key]: res.success ? "ok" : "error" }));
            if (res.success) setTimeout(() => loadIncidents(), 3000);
        } catch {
            setControlState(prev => ({ ...prev, [key]: "error" }));
        }
        setTimeout(() => setControlState(prev => { const n = { ...prev }; delete n[key]; return n; }), 8000);
    };

    // ─────────────────────────────────────────────────────────────────────────

    return (
        <div className="p-6 space-y-6 max-w-5xl mx-auto">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                    <div className="p-2.5 bg-violet-500/15 rounded-xl border border-violet-500/20">
                        <Shield className="w-6 h-6 text-violet-400" />
                    </div>
                    <div>
                        <h1 className="text-xl font-bold text-white">Servis Monitörü</h1>
                        <p className="text-xs text-slate-400">
                            Mağaza PC'lerindeki kritik servisleri otomatik izler ve başlatır
                            {lastScanAt && <span className="ml-2 text-slate-600">· Son tarama: {fmtAge(lastScanAt)}</span>}
                        </p>
                    </div>
                </div>
                <div className="flex items-center gap-2">
                    <button onClick={() => { loadConfig(); loadIncidents(); }}
                        className="p-2 rounded-lg text-slate-400 hover:text-white hover:bg-white/5 transition-colors" title="Yenile">
                        <RefreshCw className="w-4 h-4" />
                    </button>
                    <button
                        onClick={triggerScan}
                        disabled={triggering}
                        className="flex items-center gap-2 px-4 py-2 bg-violet-600 hover:bg-violet-500 disabled:opacity-50 text-white text-sm font-medium rounded-xl transition-colors"
                    >
                        {triggering ? <Loader2 className="w-4 h-4 animate-spin" /> : <Activity className="w-4 h-4" />}
                        {triggering ? "Taranıyor…" : "Şimdi Tara"}
                    </button>
                </div>
            </div>

            {/* Active incidents */}
            <div className="glass-card border-white/5 rounded-2xl overflow-hidden">
                <div className="flex items-center justify-between px-5 py-4 border-b border-white/5">
                    <div className="flex items-center gap-2">
                        <AlertTriangle className="w-4 h-4 text-rose-400" />
                        <span className="font-semibold text-white text-sm">Aktif Kesintiler</span>
                        {incidents.length > 0 && (
                            <span className="px-2 py-0.5 text-[11px] font-bold bg-rose-500/20 text-rose-400 rounded-full">{incidents.length}</span>
                        )}
                    </div>
                    <button onClick={loadIncidents} className="text-xs text-slate-500 hover:text-slate-300">
                        <RefreshCw className="w-3.5 h-3.5" />
                    </button>
                </div>

                {loadingIncidents ? (
                    <div className="flex items-center justify-center py-12"><Loader2 className="w-6 h-6 text-violet-400 animate-spin" /></div>
                ) : incidents.length === 0 ? (
                    <div className="flex flex-col items-center justify-center py-12 text-slate-500">
                        <CheckCircle2 className="w-10 h-10 mb-3 text-emerald-600/40" />
                        <p>Aktif kesinti yok — tüm servisler çalışıyor</p>
                    </div>
                ) : (
                    <div className="divide-y divide-white/5">
                        {incidents.map(inc => {
                            const startKey = `${inc.ipAddress}-${inc.serviceName}-start`;
                            const startState = controlState[startKey] ?? "idle";
                            return (
                                <div key={inc.id} className="grid grid-cols-[1fr_auto] gap-4 px-5 py-4 hover:bg-white/[0.02]">
                                    <div className="space-y-1 min-w-0">
                                        <div className="flex items-center gap-2 flex-wrap">
                                            <span className={`text-[10px] font-bold px-1.5 py-0.5 rounded border ${severityColor(inc.severity)}`}>
                                                {inc.severity}
                                            </span>
                                            <span className="text-sm font-medium text-white">{inc.storeName}</span>
                                            <span className="text-xs text-slate-500 font-mono">{inc.deviceName} · {inc.ipAddress}</span>
                                        </div>
                                        <div className="flex items-center gap-2 text-xs text-slate-400">
                                            <span className="font-mono text-rose-300">{inc.displayName || inc.serviceName}</span>
                                            <span className="text-slate-600">·</span>
                                            <span>{inc.status}</span>
                                            {inc.consecutiveFailures > 1 && (
                                                <span className="text-slate-600">· {inc.consecutiveFailures}× ardışık</span>
                                            )}
                                            <span className="text-slate-600">·</span>
                                            <Clock className="w-3 h-3" />
                                            <span>{fmtAge(inc.firstDetectedAt)}</span>
                                        </div>
                                        {inc.message && (
                                            <p className="text-[11px] text-slate-500 font-mono truncate" title={inc.message}>{inc.message}</p>
                                        )}
                                    </div>
                                    <div className="flex items-center gap-1.5 shrink-0">
                                        <button
                                            onClick={() => controlService(inc, "start")}
                                            disabled={startState === "loading"}
                                            title="Servisi başlat"
                                            className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-lg transition-all ${
                                                startState === "ok" ? "bg-emerald-500/20 text-emerald-400 cursor-default" :
                                                startState === "error" ? "bg-rose-500/20 text-rose-400" :
                                                startState === "loading" ? "bg-white/5 text-slate-500 cursor-wait" :
                                                "bg-emerald-500/10 text-emerald-400 hover:bg-emerald-500/20"
                                            }`}
                                        >
                                            {startState === "loading" ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> :
                                             startState === "ok" ? <CheckCircle2 className="w-3.5 h-3.5" /> :
                                             <Play className="w-3.5 h-3.5" />}
                                            {startState === "loading" ? "Başlatılıyor…" : startState === "ok" ? "Başlatıldı" : "Başlat"}
                                        </button>
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                )}
            </div>

            {/* Config panel */}
            <div className="glass-card border-white/5 rounded-2xl overflow-hidden">
                <button
                    onClick={() => setConfigOpen(o => !o)}
                    className="w-full flex items-center justify-between px-5 py-4 hover:bg-white/[0.02] transition-colors"
                >
                    <div className="flex items-center gap-2">
                        <Settings className="w-4 h-4 text-slate-400" />
                        <span className="font-semibold text-white text-sm">Monitör Ayarları</span>
                        {configFileExists && (
                            <span className="text-[10px] px-1.5 py-0.5 rounded bg-violet-500/15 text-violet-400 border border-violet-500/20">Özelleştirilmiş</span>
                        )}
                        {isDirty && (
                            <span className="text-[10px] px-1.5 py-0.5 rounded bg-amber-500/15 text-amber-400 border border-amber-500/20">Kaydedilmemiş değişiklik</span>
                        )}
                    </div>
                    {configOpen ? <ChevronUp className="w-4 h-4 text-slate-500" /> : <ChevronDown className="w-4 h-4 text-slate-500" />}
                </button>

                {configOpen && draft && (
                    <div className="px-5 pb-5 space-y-5 border-t border-white/5 pt-4">
                        {/* Row 1: Enable + AutoStart */}
                        <div className="grid grid-cols-2 gap-4">
                            <Toggle
                                label="Monitör Aktif"
                                description="Servisleri periyodik olarak izle"
                                value={draft.enabled}
                                onChange={v => setDraft({ ...draft, enabled: v })}
                            />
                            <Toggle
                                label="Otomatik Başlat"
                                description="Duran servisleri otomatik yeniden başlat"
                                value={draft.autoStartStoppedServices}
                                onChange={v => setDraft({ ...draft, autoStartStoppedServices: v })}
                            />
                        </div>

                        {/* Row 2: Numbers */}
                        <div className="grid grid-cols-3 gap-4">
                            <NumField label="Kontrol Aralığı (sn)" min={30} max={3600} step={30}
                                value={draft.intervalSeconds}
                                onChange={v => setDraft({ ...draft, intervalSeconds: v })}
                                hint={`${Math.round(draft.intervalSeconds / 60)} dakika`}
                            />
                            <NumField label="Onay Eşiği" min={1} max={10} step={1}
                                value={draft.confirmationThreshold}
                                onChange={v => setDraft({ ...draft, confirmationThreshold: v })}
                                hint="Ardışık başarısızlık sayısı"
                            />
                            <NumField label="Eş Zamanlılık" min={1} max={20} step={1}
                                value={draft.maxConcurrency}
                                onChange={v => setDraft({ ...draft, maxConcurrency: v })}
                                hint="Paralel WMI sorgusu"
                            />
                        </div>

                        {/* Row 3: Device types */}
                        <div>
                            <div className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-2 flex items-center gap-2">
                                <Monitor className="w-3.5 h-3.5" /> İzlenen Cihaz Tipleri
                            </div>
                            <div className="flex gap-2 flex-wrap">
                                {DEVICE_TYPE_OPTIONS.map(dt => (
                                    <button key={dt} onClick={() => toggleDeviceType(dt)}
                                        className={`px-3 py-1.5 text-xs font-semibold rounded-lg border transition-all ${
                                            draft.deviceTypes.includes(dt)
                                                ? "bg-violet-500/20 text-violet-300 border-violet-500/30"
                                                : "text-slate-500 border-slate-700/50 hover:border-slate-500 hover:text-slate-300"
                                        }`}>
                                        {dt}
                                    </button>
                                ))}
                            </div>
                        </div>

                        {/* Row 4: Services */}
                        <div>
                            <div className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-2">İzlenen Servisler</div>
                            <div className="space-y-1 mb-3">
                                {draft.services.map((svc, idx) => (
                                    <div key={idx} className="flex items-center gap-3 px-3 py-2 bg-slate-800/40 rounded-lg border border-white/5">
                                        <div className="flex-1 min-w-0">
                                            <span className="text-xs font-mono text-slate-200">{svc.name}</span>
                                            {svc.displayName !== svc.name && (
                                                <span className="text-[11px] text-slate-500 ml-2">{svc.displayName}</span>
                                            )}
                                        </div>
                                        <button onClick={() => removeService(idx)}
                                            className="p-1 text-slate-600 hover:text-rose-400 rounded transition-colors">
                                            <Trash2 className="w-3.5 h-3.5" />
                                        </button>
                                    </div>
                                ))}
                            </div>
                            {/* Add service */}
                            <div className="flex gap-2">
                                <input
                                    type="text" placeholder="Servis adı (örn: MSSQL$SQLEXPRESS)"
                                    value={newSvcName} onChange={e => setNewSvcName(e.target.value)}
                                    onKeyDown={e => e.key === "Enter" && addService()}
                                    className="flex-1 px-3 py-2 bg-slate-800/60 border border-slate-700/50 rounded-lg text-xs text-white placeholder-slate-600 focus:outline-none focus:border-violet-500/50"
                                />
                                <input
                                    type="text" placeholder="Görünen ad (opsiyonel)"
                                    value={newSvcDisplay} onChange={e => setNewSvcDisplay(e.target.value)}
                                    onKeyDown={e => e.key === "Enter" && addService()}
                                    className="w-48 px-3 py-2 bg-slate-800/60 border border-slate-700/50 rounded-lg text-xs text-white placeholder-slate-600 focus:outline-none focus:border-violet-500/50"
                                />
                                <button onClick={addService} disabled={!newSvcName.trim()}
                                    className="p-2 bg-violet-600 hover:bg-violet-500 disabled:opacity-40 text-white rounded-lg transition-colors">
                                    <Plus className="w-4 h-4" />
                                </button>
                            </div>
                        </div>

                        {/* Save / Reset */}
                        <div className="flex items-center gap-3 pt-2 border-t border-white/5">
                            <button onClick={saveConfig} disabled={!isDirty || savingCfg}
                                className="flex items-center gap-2 px-4 py-2 bg-violet-600 hover:bg-violet-500 disabled:opacity-40 text-white text-sm font-medium rounded-xl transition-colors">
                                {savingCfg ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
                                Kaydet
                            </button>
                            {configFileExists && (
                                <button onClick={resetConfig}
                                    className="flex items-center gap-2 px-4 py-2 text-slate-400 hover:text-white hover:bg-white/5 text-sm rounded-xl transition-colors">
                                    <RotateCcw className="w-4 h-4" /> Varsayılanlara Sıfırla
                                </button>
                            )}
                            <span className="text-xs text-slate-600 ml-auto">
                                Değişiklikler kaydedilince bir sonraki döngüde devreye girer
                            </span>
                        </div>
                    </div>
                )}
                {loadingCfg && (
                    <div className="flex items-center justify-center py-8 border-t border-white/5">
                        <Loader2 className="w-5 h-5 text-violet-400 animate-spin" />
                    </div>
                )}
            </div>
        </div>
    );
}

// ── Sub-components ────────────────────────────────────────────────────────────

const Toggle: React.FC<{ label: string; description: string; value: boolean; onChange: (v: boolean) => void }> = ({ label, description, value, onChange }) => (
    <div className="flex items-center justify-between p-4 bg-slate-800/40 rounded-xl border border-white/5">
        <div>
            <div className="text-sm font-medium text-white">{label}</div>
            <div className="text-[11px] text-slate-500 mt-0.5">{description}</div>
        </div>
        <button
            onClick={() => onChange(!value)}
            role="switch"
            aria-checked={value}
            className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer items-center rounded-full border-2 border-transparent transition-colors duration-200 focus:outline-none ${value ? "bg-violet-600" : "bg-slate-600"}`}
        >
            <span className={`inline-block h-5 w-5 rounded-full bg-white shadow-md ring-0 transition-transform duration-200 ${value ? "translate-x-5" : "translate-x-0"}`} />
        </button>
    </div>
);

const NumField: React.FC<{ label: string; hint: string; value: number; min: number; max: number; step: number; onChange: (v: number) => void }> = ({ label, hint, value, min, max, step, onChange }) => (
    <div className="space-y-1">
        <label className="text-[11px] text-slate-400 font-semibold uppercase tracking-wider">{label}</label>
        <input type="number" min={min} max={max} step={step} value={value}
            onChange={e => onChange(Math.max(min, Math.min(max, parseInt(e.target.value) || min)))}
            className="w-full px-3 py-2 bg-slate-800/60 border border-slate-700/50 rounded-lg text-sm text-white focus:outline-none focus:border-violet-500/50"
        />
        <div className="text-[10px] text-slate-600">{hint}</div>
    </div>
);
