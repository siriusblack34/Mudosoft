import React, { useEffect, useState, useCallback } from "react";
import { useSearchParams } from "react-router-dom";
import { apiClient, StoreDiagnostic, DiagnosticsResponse, StoreTimelineResponse } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import {
    Activity, AlertTriangle, ChevronRight, Clock, RefreshCw, Router, Shield,
    ShieldAlert, ShieldX, Signal, Wifi, WifiOff, Zap, X,
} from "lucide-react";
import RouterLineDiagnosticsTab from "./RouterLineDiagnosticsTab";

/* ─── helpers ─── */
const fmtTime = (iso: string) => {
    const d = new Date(iso);
    return d.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
};
const fmtDate = (iso: string) => {
    const d = new Date(iso);
    return d.toLocaleDateString("tr-TR", { day: "2-digit", month: "2-digit" }) + " " + fmtTime(iso);
};
const ago = (iso: string) => {
    const m = Math.floor((Date.now() - new Date(iso).getTime()) / 60000);
    if (m < 1) return "az once";
    if (m < 60) return `${m} dk once`;
    const h = Math.floor(m / 60);
    if (h < 24) return `${h} sa once`;
    return `${Math.floor(h / 24)} gun once`;
};

/* ─── severity config ─── */
const severityConfig = {
    Critical: {
        icon: <ShieldX className="w-5 h-5" />,
        bg: "from-rose-900/40 to-rose-900/10",
        border: "border-rose-500/30",
        text: "text-rose-400",
        badge: "bg-rose-500/20 text-rose-400 border-rose-500/30",
        glow: "shadow-[0_0_20px_rgba(225,29,72,0.15)]",
    },
    Warning: {
        icon: <ShieldAlert className="w-5 h-5" />,
        bg: "from-amber-900/40 to-amber-900/10",
        border: "border-amber-500/30",
        text: "text-amber-400",
        badge: "bg-amber-500/20 text-amber-400 border-amber-500/30",
        glow: "shadow-[0_0_20px_rgba(245,158,11,0.15)]",
    },
    Info: {
        icon: <Shield className="w-5 h-5" />,
        bg: "from-sky-900/40 to-sky-900/10",
        border: "border-sky-500/30",
        text: "text-sky-400",
        badge: "bg-sky-500/20 text-sky-400 border-sky-500/30",
        glow: "",
    },
};

const typeConfig: Record<string, { label: string; icon: React.ReactNode; hint: string }> = {
    FullOutage: { label: "Tam Kesinti", icon: <WifiOff className="w-4 h-4" />, hint: "ISP veya elektrik kesintisi" },
    InternalNetwork: { label: "Ic Ag Sorunu", icon: <Router className="w-4 h-4" />, hint: "Switch veya kablo arizasi" },
    RouterFlapping: { label: "Router Kararsiz", icon: <Zap className="w-4 h-4" />, hint: "Modem/hat sorunu" },
    DeviceFlapping: { label: "Cihaz Kararsiz", icon: <Activity className="w-4 h-4" />, hint: "Cihaz bazli sorun" },
    PartialOutage: { label: "Kismi Kesinti", icon: <AlertTriangle className="w-4 h-4" />, hint: "Bazi cihazlar offline" },
    StoreFlapping: { label: "Aralikli Kesinti", icon: <Zap className="w-4 h-4" />, hint: "Kararsiz internet" },
};

/* ═══════════════════════════════════════════
   MAIN PAGE
   ═══════════════════════════════════════════ */
type TabKey = 'outages' | 'router-hat';

const NetworkDiagnosticsPage: React.FC = () => {
    const [searchParams, setSearchParams] = useSearchParams();
    const initialTab: TabKey = searchParams.get('tab') === 'router-hat' ? 'router-hat' : 'outages';
    const initialStoreParam = searchParams.get('store');
    const initialStoreCode = initialStoreParam ? parseInt(initialStoreParam, 10) : null;
    const [tab, setTab] = useState<TabKey>(initialTab);
    const [data, setData] = useState<DiagnosticsResponse | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
    const [selectedStore, setSelectedStore] = useState<number | null>(null);
    const [timeline, setTimeline] = useState<StoreTimelineResponse | null>(null);
    const [timelineLoading, setTimelineLoading] = useState(false);
    const [filter, setFilter] = useState<"all" | "Critical" | "Warning">("all");

    const changeTab = (t: TabKey) => {
        setTab(t);
        const next = new URLSearchParams(searchParams);
        if (t === 'router-hat') next.set('tab', 'router-hat');
        else next.delete('tab');
        setSearchParams(next, { replace: true });
    };

    const load = useCallback(async (silent = false) => {
        try {
            if (!silent) setLoading(true);
            setError(null);
            const result = await apiClient.getActiveDiagnostics(30, 4);
            setData(result);
            setLastUpdated(new Date());
        } catch (err) {
            setError(err instanceof Error ? err.message : "Veri alinamadi");
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => {
        if (tab !== 'outages') return;
        load();
        const t = setInterval(() => load(true), 30000);
        return () => clearInterval(t);
    }, [load, tab]);

    const openTimeline = async (storeCode: number) => {
        setSelectedStore(storeCode);
        setTimelineLoading(true);
        try {
            const result = await apiClient.getStoreTimeline(storeCode, 24);
            setTimeline(result);
        } catch { setTimeline(null); }
        finally { setTimelineLoading(false); }
    };

    if (tab === 'router-hat') {
        return (
            <div className="p-6 h-[calc(100vh-2rem)] flex flex-col gap-4 max-w-[1920px] mx-auto w-full">
                <div className="flex items-center justify-between">
                    <h1 className="text-2xl font-bold text-white flex items-center gap-3">
                        <Activity className="w-7 h-7 text-amber-500" />
                        Ag Teshis Sistemi
                    </h1>
                </div>
                <TabSwitcher tab={tab} onChange={changeTab} />
                <RouterLineDiagnosticsTab initialStoreCode={initialStoreCode} />
            </div>
        );
    }

    if (loading && !data) return <div className="flex h-[80vh] items-center justify-center"><Spinner size="lg" /></div>;

    const diagnostics = data?.diagnostics ?? [];
    const filtered = filter === "all" ? diagnostics : diagnostics.filter(d => d.severity === filter);
    const critCount = diagnostics.filter(d => d.severity === "Critical").length;
    const warnCount = diagnostics.filter(d => d.severity === "Warning").length;

    return (
        <div className="p-6 h-[calc(100vh-2rem)] flex flex-col gap-5 max-w-[1920px] mx-auto w-full">
            {/* Header */}
            <div className="flex items-center justify-between">
                <h1 className="text-2xl font-bold text-white flex items-center gap-3">
                    <Activity className="w-7 h-7 text-amber-500" />
                    Ag Teshis Sistemi
                </h1>
                <div className="flex items-center gap-3">
                    {lastUpdated && (
                        <span className="text-xs text-slate-400 font-mono bg-slate-900/40 px-3 py-1.5 rounded-lg border border-slate-700/50">
                            Son: {lastUpdated.toLocaleTimeString("tr-TR")}
                        </span>
                    )}
                    <button onClick={() => load()} className="p-2 bg-slate-800 hover:bg-slate-700 rounded-lg border border-slate-700/50 transition-colors" title="Yenile">
                        <RefreshCw className={`w-4 h-4 text-amber-500 ${loading ? "animate-spin" : ""}`} />
                    </button>
                </div>
            </div>

            {error && (
                <div className="bg-rose-500/10 border border-rose-500/30 rounded-xl p-4 text-sm text-rose-400">
                    {error}
                </div>
            )}

            <TabSwitcher tab={tab} onChange={changeTab} />

            {/* Summary cards */}
            <div className="grid grid-cols-4 gap-4">
                <SummaryCard label="Analiz Edilen" value={`${data?.summary.totalStores ?? 0}`} sub="magaza" color="sky" />
                <SummaryCard label="Toplam Sorun" value={`${data?.summary.issues ?? 0}`} sub="tespit" color={diagnostics.length > 0 ? "amber" : "emerald"} />
                <SummaryCard label="Kritik" value={`${critCount}`} sub="acil mudahale" color={critCount > 0 ? "rose" : "emerald"} />
                <SummaryCard label="Uyari" value={`${warnCount}`} sub="izleme" color={warnCount > 0 ? "amber" : "emerald"} />
            </div>

            {/* Filter bar */}
            <div className="flex gap-2 items-center">
                {(["all", "Critical", "Warning"] as const).map(f => (
                    <button key={f} onClick={() => setFilter(f)}
                        className={`px-3 py-1.5 rounded-lg text-xs font-bold transition-colors border ${filter === f
                            ? f === "Critical" ? "bg-rose-500/20 text-rose-400 border-rose-500/30"
                                : f === "Warning" ? "bg-amber-500/20 text-amber-400 border-amber-500/30"
                                    : "bg-slate-700/50 text-white border-slate-600/50"
                            : "text-slate-400 hover:text-white border-transparent hover:bg-slate-800"}`}>
                        {f === "all" ? `Hepsi (${diagnostics.length})` : f === "Critical" ? `Kritik (${critCount})` : `Uyari (${warnCount})`}
                    </button>
                ))}
            </div>

            {/* Main content */}
            <div className="flex-1 overflow-auto min-h-0 flex gap-5">
                {/* Left: Issues list */}
                <div className="flex-1 min-w-0 space-y-3 overflow-auto scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent pb-4">
                    {filtered.length === 0 ? (
                        <div className="flex flex-col items-center justify-center h-64 text-slate-500">
                            <Shield className="w-20 h-20 mb-4 text-emerald-500/30" />
                            <p className="text-lg font-bold text-emerald-400">Tum Sistemler Normal</p>
                            <p className="text-sm text-slate-400 mt-1">Tespit edilen ag sorunu yok.</p>
                        </div>
                    ) : (
                        filtered.map((d, i) => (
                            <DiagnosticCard
                                key={`${d.storeCode}-${d.type}-${i}`}
                                diagnostic={d}
                                isSelected={selectedStore === d.storeCode}
                                onClick={() => openTimeline(d.storeCode)}
                            />
                        ))
                    )}
                </div>

                {/* Right: Timeline panel */}
                {selectedStore !== null && (
                    <div className="w-[420px] shrink-0 bg-slate-900/60 border border-slate-700/50 rounded-2xl p-4 overflow-auto scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent">
                        <div className="flex items-center justify-between mb-4">
                            <h3 className="text-sm font-bold text-white flex items-center gap-2">
                                <Clock className="w-4 h-4 text-sky-500" />
                                Magaza {selectedStore} — Son 24 Saat
                            </h3>
                            <button onClick={() => { setSelectedStore(null); setTimeline(null); }}
                                className="p-1 text-slate-400 hover:text-white rounded-lg hover:bg-slate-800">
                                <X className="w-4 h-4" />
                            </button>
                        </div>
                        {timelineLoading ? (
                            <div className="flex items-center justify-center h-40"><Spinner size="md" /></div>
                        ) : timeline ? (
                            <TimelinePanel data={timeline} />
                        ) : (
                            <p className="text-sm text-slate-500 text-center py-10">Timeline yuklenemedi.</p>
                        )}
                    </div>
                )}
            </div>
        </div>
    );
};

/* ═══════════════════════════════════════════
   SUB-COMPONENTS
   ═══════════════════════════════════════════ */

const TabSwitcher: React.FC<{ tab: TabKey; onChange: (t: TabKey) => void }> = ({ tab, onChange }) => (
    <div className="flex gap-1 border-b border-slate-700/40">
        <TabButton active={tab === 'outages'} onClick={() => onChange('outages')} label="Kesinti & Kararsızlık" icon={<AlertTriangle className="w-4 h-4" />} />
        <TabButton active={tab === 'router-hat'} onClick={() => onChange('router-hat')} label="Router Hat Durumu" icon={<Signal className="w-4 h-4" />} />
    </div>
);

const TabButton: React.FC<{ active: boolean; onClick: () => void; label: string; icon: React.ReactNode }> = ({ active, onClick, label, icon }) => (
    <button onClick={onClick}
        className={`flex items-center gap-2 px-4 py-2 text-sm font-bold transition-colors border-b-2 -mb-px ${active
            ? 'border-amber-500 text-amber-400'
            : 'border-transparent text-slate-400 hover:text-white'}`}>
        {icon} {label}
    </button>
);


const SummaryCard: React.FC<{ label: string; value: string; sub: string; color: string }> = ({ label, value, sub, color }) => {
    const colors: Record<string, string> = {
        sky: "from-sky-900/30 to-sky-900/10 border-sky-500/20 text-sky-400",
        emerald: "from-emerald-900/30 to-emerald-900/10 border-emerald-500/20 text-emerald-400",
        amber: "from-amber-900/30 to-amber-900/10 border-amber-500/20 text-amber-400",
        rose: "from-rose-900/30 to-rose-900/10 border-rose-500/20 text-rose-400",
    };
    const c = colors[color] ?? colors.sky;
    const textClass = c.split(" ").find(s => s.startsWith("text-")) ?? "text-sky-400";
    return (
        <div className={`bg-gradient-to-br ${c} border rounded-2xl p-4`}>
            <div className="text-[10px] font-bold uppercase tracking-widest text-slate-500">{label}</div>
            <div className={`text-3xl font-black mt-1 ${textClass}`}>{value}</div>
            <div className="text-xs text-slate-400 mt-0.5">{sub}</div>
        </div>
    );
};

const DiagnosticCard: React.FC<{ diagnostic: StoreDiagnostic; isSelected: boolean; onClick: () => void }> = ({ diagnostic: d, isSelected, onClick }) => {
    const sev = severityConfig[d.severity] ?? severityConfig.Info;
    const tc = typeConfig[d.type] ?? { label: d.type, icon: <AlertTriangle className="w-4 h-4" />, hint: "" };

    return (
        <button
            onClick={onClick}
            className={`w-full text-left rounded-2xl border p-4 transition-all hover:-translate-y-0.5 hover:shadow-lg bg-gradient-to-br ${sev.bg} ${sev.border} ${sev.glow} ${isSelected ? "ring-2 ring-sky-500/50" : ""}`}
        >
            <div className="flex items-start gap-3">
                {/* Severity icon */}
                <div className={`p-2 rounded-xl ${sev.badge} border shrink-0`}>
                    {sev.icon}
                </div>

                <div className="flex-1 min-w-0">
                    {/* Header */}
                    <div className="flex items-center gap-2 mb-1">
                        <span className="text-sm font-bold text-white">[{d.storeCode}] {d.storeName}</span>
                        <span className={`px-2 py-0.5 rounded text-[9px] font-bold uppercase ${sev.badge} border`}>
                            {d.severity === "Critical" ? "KRİTİK" : "UYARI"}
                        </span>
                    </div>

                    {/* Type badge + title */}
                    <div className="flex items-center gap-2 mb-2">
                        <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold bg-slate-700/50 ${sev.text}`}>
                            {tc.icon} {tc.label}
                        </span>
                        {d.flappingCount > 0 && (
                            <span className="text-[10px] text-amber-400/80 font-mono">{d.flappingCount} gecis</span>
                        )}
                    </div>

                    {/* Message */}
                    <p className="text-xs text-slate-300 mb-2">{d.message}</p>

                    {/* Footer stats */}
                    <div className="flex items-center gap-4 text-[10px]">
                        <span className="flex items-center gap-1 text-slate-400">
                            <Wifi className={`w-3 h-3 ${d.routerOnline ? "text-emerald-400" : "text-rose-400"}`} />
                            Router {d.routerOnline ? "Online" : "Offline"}
                        </span>
                        <span className="text-emerald-400">{d.onlineDevices} online</span>
                        <span className="text-rose-400">{d.offlineDevices} offline</span>
                        {d.affectedDevices.length > 0 && (
                            <span className="text-slate-500 truncate">{d.affectedDevices.join(", ")}</span>
                        )}
                        <ChevronRight className="w-3 h-3 text-slate-600 ml-auto shrink-0" />
                    </div>
                </div>
            </div>
        </button>
    );
};

const TimelinePanel: React.FC<{ data: StoreTimelineResponse }> = ({ data }) => {
    const { summary, timeline } = data;

    return (
        <div className="space-y-4">
            {/* Summary */}
            <div className="grid grid-cols-3 gap-2">
                <MiniStat label="Toplam Olay" value={summary.totalEvents} color="text-white" />
                <MiniStat label="Router" value={summary.routerEvents} color="text-amber-400" />
                <MiniStat label="PC + Kasa" value={summary.pcEvents + summary.kasaEvents} color="text-sky-400" />
            </div>

            {summary.totalEvents === 0 ? (
                <div className="text-center py-8">
                    <Shield className="w-12 h-12 mx-auto text-emerald-500/30 mb-2" />
                    <p className="text-sm text-emerald-400 font-bold">Son 24 saatte durum degisikligi yok</p>
                    <p className="text-xs text-slate-500 mt-1">Bu magaza stabil gorunuyor.</p>
                </div>
            ) : (
                <div className="space-y-3">
                    {timeline.map(device => (
                        <DeviceTimeline key={device.deviceId} device={device} />
                    ))}
                </div>
            )}
        </div>
    );
};

const MiniStat: React.FC<{ label: string; value: number; color: string }> = ({ label, value, color }) => (
    <div className="bg-slate-800/50 rounded-xl p-2.5 text-center border border-slate-700/50">
        <div className={`text-lg font-black ${color}`}>{value}</div>
        <div className="text-[9px] text-slate-500 uppercase font-bold">{label}</div>
    </div>
);

const DeviceTimeline: React.FC<{ device: StoreTimelineResponse["timeline"][0] }> = ({ device }) => {
    const isRouter = device.deviceType.toUpperCase() === "ROUTER";
    const isPC = device.deviceType.toUpperCase() === "PC";
    const typeColor = isRouter ? "text-amber-400" : isPC ? "text-sky-400" : "text-emerald-400";
    const typeLabel = isRouter ? "Router" : isPC ? "PC" : device.deviceType;

    return (
        <div className="bg-slate-800/40 border border-slate-700/50 rounded-xl p-3">
            <div className="flex items-center justify-between mb-2">
                <div className="flex items-center gap-2">
                    <span className={`text-xs font-bold ${typeColor}`}>{typeLabel}</span>
                    <span className={`w-2 h-2 rounded-full ${device.lastStatus ? "bg-emerald-500" : "bg-rose-500"}`} />
                </div>
                <span className="text-[10px] text-slate-500 font-mono">{device.totalChanges} gecis</span>
            </div>
            {/* Timeline dots */}
            <div className="flex flex-wrap gap-1">
                {device.changes.map((c, i) => (
                    <div
                        key={i}
                        className={`h-5 px-1.5 rounded text-[8px] font-mono flex items-center gap-0.5 border ${c.isOnline
                            ? "bg-emerald-500/10 border-emerald-500/20 text-emerald-400"
                            : "bg-rose-500/10 border-rose-500/20 text-rose-400"
                            }`}
                        title={`${c.isOnline ? "ONLINE" : "OFFLINE"} — ${fmtDate(c.changedAt)}`}
                    >
                        {c.isOnline ? "▲" : "▼"} {fmtTime(c.changedAt)}
                    </div>
                ))}
            </div>
        </div>
    );
};

export default NetworkDiagnosticsPage;
