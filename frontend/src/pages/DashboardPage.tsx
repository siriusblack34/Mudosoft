import React, { useEffect, useMemo, useState, useCallback } from "react";
import { Link } from "react-router-dom";
import {
    Activity,
    AlertTriangle,
    Building2,
    ChevronRight,
    Clock,
    Database,
    Globe,
    HardDrive,
    Laptop,
    Monitor,
    MonitorSmartphone,
    RefreshCw,
    Server,
    ShieldCheck,
    Wifi,
    WifiOff,
    Zap,
} from "lucide-react";
import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip } from "recharts";
import { useTheme } from "../contexts/ThemeContext";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import Modal from "../components/ui/Modal";

/* ─── Types ─── */
interface DashboardData {
    totalDevices: number;
    online: number;
    offline: number;
    healthy: number;
    warning: number;
    critical: number;
}

interface StoreSummary {
    code: number;
    name: string;
    totalPos: number;
    activePos: number;
    onlinePos: number;
    offlinePos: number;
    closedPos: number;
    since: string | null;
    reason: string | null;
    status: "stable" | "watch" | "critical" | "closed";
}

/* ─── Constants ─── */
const SQL_CACHE_KEY = "ms_sql_devices_cache_v4";
const EMPTY_DASHBOARD: DashboardData = { totalDevices: 0, online: 0, offline: 0, healthy: 0, warning: 0, critical: 0 };

/* ─── Helpers ─── */
function readSqlCache() {
    try {
        const raw = localStorage.getItem(SQL_CACHE_KEY);
        if (!raw) return null;
        const parsed = JSON.parse(raw) as { savedAt?: string; items?: SqlDeviceWithStatus[] } | SqlDeviceWithStatus[];
        let result: { savedAt: string; items: SqlDeviceWithStatus[] } | null = null;
        if (Array.isArray(parsed)) result = { savedAt: new Date().toISOString(), items: parsed };
        else if (Array.isArray(parsed.items)) result = { savedAt: parsed.savedAt ?? new Date().toISOString(), items: parsed.items };
        // 60 saniyeden eski cache'i kullanma — stale offline verisi göstermesin
        if (result?.savedAt) {
            const age = Date.now() - new Date(result.savedAt).getTime();
            if (age > 60_000) return null;
        }
        return result;
    } catch { /* ignore */ }
    return null;
}

function pct(value: number, total: number) {
    if (total <= 0) return 0;
    return Math.max(0, Math.min(100, Math.round((value / total) * 100)));
}

function fmtDuration(iso?: string | null) {
    if (!iso) return "--";
    const time = new Date(iso).getTime();
    if (Number.isNaN(time)) return "--";
    const diffMinutes = Math.max(0, Math.floor((Date.now() - time) / 60000));
    const hours = Math.floor(diffMinutes / 60);
    const days = Math.floor(hours / 24);
    if (days > 0) return `${days}g`;
    if (hours > 0) return `${hours}sa`;
    if (diffMinutes > 0) return `${diffMinutes}dk`;
    return "az once";
}

function fmtTime(date?: Date | null, withSeconds = false) {
    if (!date || Number.isNaN(date.getTime())) return "--";
    return date.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit", ...(withSeconds ? { second: "2-digit" } : {}) });
}

/* ─── Palette ─── */
function palette(isDark: boolean) {
    if (isDark) {
        return {
            page: "#060d16", card: "#0c1525", cardAlt: "#101c30", cardSoft: "#111d2b",
            glass: "rgba(12,21,37,0.80)", border: "rgba(148,163,184,0.10)", borderStrong: "rgba(148,163,184,0.16)",
            text: "#f1f5f9", muted: "#94a3b8", subtle: "#64748b", shadow: "rgba(0,0,0,0.40)", track: "rgba(148,163,184,0.10)",
            sky: "#38bdf8", skySoft: "rgba(56,189,248,0.12)", skyBorder: "rgba(56,189,248,0.20)", skyGlow: "rgba(56,189,248,0.06)",
            emerald: "#34d399", emeraldSoft: "rgba(52,211,153,0.12)", emeraldBorder: "rgba(52,211,153,0.20)", emeraldGlow: "rgba(52,211,153,0.06)",
            amber: "#fbbf24", amberSoft: "rgba(251,191,36,0.12)", amberBorder: "rgba(251,191,36,0.20)", amberGlow: "rgba(251,191,36,0.06)",
            rose: "#fb7185", roseSoft: "rgba(251,113,133,0.12)", roseBorder: "rgba(251,113,133,0.20)", roseGlow: "rgba(251,113,133,0.06)",
            violet: "#a78bfa", violetSoft: "rgba(167,139,250,0.12)", violetBorder: "rgba(167,139,250,0.20)",
            slate: "#475569",
        };
    }
    return {
        page: "#f0f4f8", card: "#ffffff", cardAlt: "#f8fafc", cardSoft: "#f1f5f9",
        glass: "rgba(255,255,255,0.85)", border: "rgba(15,23,42,0.07)", borderStrong: "rgba(15,23,42,0.12)",
        text: "#0f172a", muted: "#475569", subtle: "#94a3b8", shadow: "rgba(148,163,184,0.16)", track: "rgba(15,23,42,0.06)",
        sky: "#0284c7", skySoft: "rgba(2,132,199,0.08)", skyBorder: "rgba(2,132,199,0.16)", skyGlow: "rgba(2,132,199,0.04)",
        emerald: "#059669", emeraldSoft: "rgba(5,150,105,0.08)", emeraldBorder: "rgba(5,150,105,0.16)", emeraldGlow: "rgba(5,150,105,0.04)",
        amber: "#d97706", amberSoft: "rgba(217,119,6,0.08)", amberBorder: "rgba(217,119,6,0.16)", amberGlow: "rgba(217,119,6,0.04)",
        rose: "#e11d48", roseSoft: "rgba(225,29,72,0.08)", roseBorder: "rgba(225,29,72,0.16)", roseGlow: "rgba(225,29,72,0.04)",
        violet: "#7c3aed", violetSoft: "rgba(124,58,237,0.08)", violetBorder: "rgba(124,58,237,0.16)",
        slate: "#64748b",
    };
}

type Palette = ReturnType<typeof palette>;

/* ─── CSS animation keyframes (injected once) ─── */
const STYLE_ID = "ms-dashboard-anim";
if (typeof document !== "undefined" && !document.getElementById(STYLE_ID)) {
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = `
        @keyframes ms-pulse-ring { 0% { transform: scale(0.85); opacity: 1; } 100% { transform: scale(2.2); opacity: 0; } }
        @keyframes ms-fade-up { from { opacity: 0; transform: translateY(24px) scale(0.96); } to { opacity: 1; transform: translateY(0) scale(1); } }
        @keyframes ms-shimmer { 0% { transform: translateX(-100%); } 100% { transform: translateX(200%); } }
        @keyframes ms-gradient-shift { 0%,100% { background-position: 0% 50%; } 50% { background-position: 100% 50%; } }
        @keyframes ms-float { 0%,100% { transform: translateY(0px); } 50% { transform: translateY(-12px); } }
        @keyframes ms-ring-fill { from { stroke-dashoffset: var(--ring-circumference); } to { stroke-dashoffset: var(--ring-offset); } }
        @keyframes ms-glow-breathe { 0%,100% { opacity: 0.3; transform: scale(1); } 50% { opacity: 0.7; transform: scale(1.05); } }
        @keyframes ms-slide-in-right { from { opacity: 0; transform: translateX(30px); } to { opacity: 1; transform: translateX(0); } }
        @keyframes ms-number-pop { 0% { transform: scale(0.5); opacity: 0; } 60% { transform: scale(1.1); } 100% { transform: scale(1); opacity: 1; } }
        @keyframes ms-bar-grow { from { width: 0%; } }
        .ms-fade-up { animation: ms-fade-up 0.6s cubic-bezier(0.16,1,0.3,1) both; }
        .ms-shimmer-bar { position: relative; overflow: hidden; }
        .ms-shimmer-bar::after { content: ''; position: absolute; top: 0; left: 0; width: 40%; height: 100%; background: linear-gradient(90deg, transparent, rgba(255,255,255,0.25), transparent); animation: ms-shimmer 2s ease-in-out infinite; }
        .ms-glass-hover { transition: transform 0.3s cubic-bezier(0.16,1,0.3,1), box-shadow 0.3s ease, border-color 0.3s ease; }
        .ms-glass-hover:hover { transform: translateY(-3px); }
    `;
    document.head.appendChild(style);
}

/* ─── Animated counter hook ─── */
function useAnimatedNumber(target: number, duration = 900) {
    const [value, setValue] = useState(0);
    const prevTarget = React.useRef(0);
    useEffect(() => {
        const from = prevTarget.current;
        prevTarget.current = target;
        if (target === from) return;
        const start = performance.now();
        const step = (now: number) => {
            const elapsed = now - start;
            const progress = Math.min(elapsed / duration, 1);
            const eased = 1 - Math.pow(1 - progress, 3);
            setValue(Math.round(from + (target - from) * eased));
            if (progress < 1) requestAnimationFrame(step);
        };
        requestAnimationFrame(step);
    }, [target, duration]);
    return value;
}

/* ═══════════════════════════════════════════
   MAIN COMPONENT
   ═══════════════════════════════════════════ */
const DashboardPage: React.FC = () => {
    const { isDark } = useTheme();
    const c = palette(isDark);
    const [data, setData] = useState<DashboardData | null>(null);
    const [sqlDevices, setSqlDevices] = useState<SqlDeviceWithStatus[]>(() => readSqlCache()?.items ?? []);
    const [loading, setLoading] = useState(true);
    const [sqlLoading, setSqlLoading] = useState(() => (readSqlCache()?.items.length ?? 0) === 0);
    const [dashboardUpdatedAt, setDashboardUpdatedAt] = useState<Date | null>(null);
    const [sqlUpdatedAt, setSqlUpdatedAt] = useState<Date | null>(() => {
        const cached = readSqlCache();
        return cached?.savedAt ? new Date(cached.savedAt) : null;
    });
    const [dashboardError, setDashboardError] = useState<string | null>(null);
    const [sqlError, setSqlError] = useState<string | null>(null);
    const [isDeviceDistributionOpen, setIsDeviceDistributionOpen] = useState(false);
    const [, setTick] = useState(0);

    useEffect(() => {
        const timer = window.setInterval(() => setTick((v) => v + 1), 1000);
        return () => window.clearInterval(timer);
    }, []);

    const loadDashboard = useCallback(async () => {
        setDashboardError(null);
        try {
            const result = await apiClient.getDashboard();
            setData({ totalDevices: result.totalDevices, online: result.online, offline: result.offline, healthy: result.healthy, warning: result.warning, critical: result.critical });
            setDashboardUpdatedAt(new Date());
        } catch (error) {
            setDashboardError(error instanceof Error ? error.message : "Agent ozeti alinamadi.");
        } finally {
            setLoading(false);
        }
    }, []);

    const loadSql = useCallback(async () => {
        setSqlLoading(true);
        setSqlError(null);
        try {
            const result = await apiClient.getSqlDevicesWithStatus({ timeoutMs: 3000 });
            const now = new Date();
            setSqlDevices(result);
            setSqlUpdatedAt(now);
            localStorage.setItem(SQL_CACHE_KEY, JSON.stringify({ savedAt: now.toISOString(), items: result }));
        } catch (error) {
            setSqlError(error instanceof Error ? error.message : "SQL matrisi alinamadi.");
        } finally {
            setSqlLoading(false);
        }
    }, []);

    useEffect(() => {
        void loadDashboard();
        void loadSql();
        const interval = window.setInterval(() => { void loadDashboard(); void loadSql(); }, 30000);
        return () => window.clearInterval(interval);
    }, [loadDashboard, loadSql]);

    /* ─── Derived data ─── */
    const summary = data ?? EMPTY_DASHBOARD;
    const pcs = sqlDevices.filter((d) => d.deviceType?.toUpperCase() === "PC");
    const posDevices = sqlDevices.filter((d) => d.deviceType?.toUpperCase().startsWith("KASA"));
    const activePos = posDevices.filter((d) => !d.isTemporarilyClosed);
    const closedPos = posDevices.filter((d) => d.isTemporarilyClosed).length;
    const activePcs = pcs.filter((d) => !d.isTemporarilyClosed);
    const closedPcs = pcs.filter((d) => d.isTemporarilyClosed).length;
    const pcOnline = pcs.filter((d) => d.isOnline).length;
    const posOnline = activePos.filter((d) => d.isOnline).length;
    const pcOffline = activePcs.filter((d) => !d.isOnline).length;
    const posOffline = activePos.length - posOnline;
    const sqlOnline = activePcs.filter((d) => d.isOnline).length + posOnline;
    const sqlTracked = activePcs.length + activePos.length;
    const agentPct = pct(summary.online, summary.totalDevices);
    const sqlPct = pct(sqlOnline, sqlTracked);
    const healthPct = pct(summary.healthy, summary.totalDevices);

    const stores = useMemo(() => {
        const map = new Map<number, Omit<StoreSummary, "status">>();
        for (const device of posDevices) {
            const key = device.storeCode ?? 0;
            const cur = map.get(key) ?? { code: key, name: device.storeName || `Magaza ${key}`, totalPos: 0, activePos: 0, onlinePos: 0, offlinePos: 0, closedPos: 0, since: null, reason: null };
            cur.totalPos += 1;
            if (device.isTemporarilyClosed) { cur.closedPos += 1; cur.reason = cur.reason ?? device.temporaryCloseReason ?? null; }
            else if (device.isOnline) { cur.activePos += 1; cur.onlinePos += 1; }
            else { cur.activePos += 1; cur.offlinePos += 1; if (!cur.since || (device.lastSeen && new Date(device.lastSeen) < new Date(cur.since))) cur.since = device.lastSeen ?? cur.since; }
            map.set(key, cur);
        }
        const priority = { stable: 1, closed: 2, watch: 3, critical: 4 } as const;
        return Array.from(map.values())
            .map((s) => ({ ...s, status: s.activePos === 0 && s.closedPos > 0 ? "closed" as const : s.activePos > 0 && s.onlinePos === 0 ? "critical" as const : s.offlinePos > 0 ? "watch" as const : "stable" as const }))
            .sort((a, b) => { if (priority[a.status] !== priority[b.status]) return priority[b.status] - priority[a.status]; if (a.offlinePos !== b.offlinePos) return b.offlinePos - a.offlinePos; return a.code - b.code; });
    }, [posDevices]);

    const criticalStores = stores.filter((s) => s.status === "critical");
    const watchStores = stores.filter((s) => s.status === "watch");
    const closedStores = stores.filter((s) => s.status === "closed");
    const stableStores = stores.filter((s) => s.status === "stable");
    const liveStoreCount = stores.filter((s) => s.activePos > 0 && s.onlinePos > 0).length;
    const activeStoreCount = stores.filter((s) => s.activePos > 0).length;
    const storePct = pct(liveStoreCount, activeStoreCount);
    const issueCount = criticalStores.length + watchStores.length;

    // Gecici kapali magazalar bilinçli karar — alert seviyesini yükseltmez
    const level = criticalStores.length > 0 || agentPct < 70 || sqlPct < 70 ? "critical"
        : watchStores.length > 0 || agentPct < 90 || sqlPct < 90 ? "watch"
        : "stable";

    // Kesinti listesi: once gercek sorunlar, sonra kapalilar
    const realIssues = stores.filter((s) => s.status === "critical" || s.status === "watch");
    const attention = [...realIssues, ...closedStores].slice(0, 6);
    const oldestIncident = stores.filter((s) => s.status === "critical" || s.status === "watch").map((s) => s.since).filter((s): s is string => Boolean(s)).sort((a, b) => new Date(a).getTime() - new Date(b).getTime())[0] ?? null;

    /* Offline cihaz listesi — lastSeen'e gore en yeni kapanan uste */
    const offlineDevices = useMemo(() => {
        return sqlDevices
            .filter((d) => !d.isOnline && !d.isTemporarilyClosed)
            .sort((a, b) => {
                const ta = a.lastSeen ? new Date(a.lastSeen).getTime() : 0;
                const tb = b.lastSeen ? new Date(b.lastSeen).getTime() : 0;
                return tb - ta; // en yeni kapanan uste
            });
    }, [sqlDevices]);

    /* Donut data */
    const totalOnline = pcOnline + posOnline;
    const totalOffline = pcOffline + posOffline;
    const totalActive = totalOnline + totalOffline;

    const deviceDonut = [
        { name: "PC", value: pcs.length, color: c.sky },
        { name: "POS", value: posDevices.length, color: c.emerald },
    ].filter((d) => d.value > 0);

    /* ─── Loading state ─── */
    if (loading && !data && sqlLoading && sqlDevices.length === 0) {
        return (
            <div className="flex min-h-[68vh] items-center justify-center">
                <div className="rounded-3xl border p-10 text-center ms-fade-up" style={{ background: c.card, borderColor: c.border, boxShadow: `0 24px 60px ${c.shadow}` }}>
                    <RefreshCw className="mx-auto h-8 w-8 animate-spin" style={{ color: c.sky }} />
                    <h2 className="mt-4 text-xl font-semibold" style={{ color: c.text }}>Kontrol paneli yukleniyor</h2>
                    <p className="mt-2 text-sm" style={{ color: c.muted }}>Veriler hazirlaniyor...</p>
                </div>
            </div>
        );
    }

    const statusColor = level === "critical" ? c.rose : level === "watch" ? c.amber : c.emerald;
    const statusGlow = level === "critical" ? c.roseGlow : level === "watch" ? c.amberGlow : c.emeraldGlow;
    const statusLabel = level === "critical" ? "Kritik" : level === "watch" ? "Izleme" : "Stabil";
    return (
        <div className="mx-auto max-w-[1600px] space-y-5 pb-8">
            {/* ═══ TOP STATUS BANNER ═══ */}
            <section
                className="relative overflow-hidden rounded-3xl border ms-fade-up"
                style={{ background: `linear-gradient(135deg, ${c.card} 0%, ${statusGlow} 50%, ${c.card} 100%)`, borderColor: c.borderStrong, boxShadow: `0 20px 50px ${c.shadow}` }}
            >
                {/* Animated gradient mesh background */}
                <div className="pointer-events-none absolute inset-0 opacity-30" style={{ background: `radial-gradient(ellipse at 20% 50%, ${statusColor}22 0%, transparent 50%), radial-gradient(ellipse at 80% 20%, ${c.sky}15 0%, transparent 50%), radial-gradient(ellipse at 50% 80%, ${c.violet}10 0%, transparent 50%)`, backgroundSize: "200% 200%", animation: "ms-gradient-shift 8s ease-in-out infinite" }} />
                {/* Decorative glow blob */}
                <div className="pointer-events-none absolute -right-20 -top-20 h-60 w-60 rounded-full opacity-30" style={{ background: `radial-gradient(circle, ${statusColor} 0%, transparent 70%)`, animation: "ms-float 8s ease-in-out infinite" }} />
                {/* Secondary floating orb */}
                <div className="pointer-events-none absolute -left-10 bottom-0 h-40 w-40 rounded-full opacity-15" style={{ background: `radial-gradient(circle, ${c.sky} 0%, transparent 70%)`, animation: "ms-float 10s ease-in-out infinite reverse" }} />

                <div className="relative flex flex-col gap-5 p-6 xl:flex-row xl:items-center xl:justify-between">
                    <div className="flex items-center gap-5">
                        {/* Animated pulse indicator */}
                        <div className="relative flex h-14 w-14 shrink-0 items-center justify-center">
                            {level !== "stable" && <div className="absolute inset-0 rounded-full" style={{ background: statusColor, opacity: 0.15, animation: "ms-pulse-ring 2s ease-out infinite" }} />}
                            <div className="flex h-10 w-10 items-center justify-center rounded-full" style={{ background: statusColor + "22" }}>
                                {level === "stable" ? <ShieldCheck className="h-5 w-5" style={{ color: statusColor }} /> : <AlertTriangle className="h-5 w-5" style={{ color: statusColor }} />}
                            </div>
                        </div>
                        <div>
                            <div className="flex items-center gap-3">
                                <h1 className="text-2xl font-bold tracking-tight" style={{ color: c.text }}>Kontrol Paneli</h1>
                                <span className="rounded-full border px-3 py-1 text-[11px] font-bold uppercase tracking-widest" style={{ color: statusColor, background: statusColor + "18", borderColor: statusColor + "40", boxShadow: `0 0 20px ${statusColor}20` }}>
                                    {statusLabel}
                                </span>
                            </div>
                            <p className="mt-1.5 text-sm" style={{ color: c.muted }}>
                                {criticalStores.length > 0 ? `${criticalStores.length} magazada tam kesinti — acil mudahale gerekiyor.`
                                    : watchStores.length > 0 ? `${watchStores.length} magazada parcali kayip goruldu.`
                                    : closedStores.length > 0 ? `Tum sistemler normal. ${closedStores.length} magaza planli kapali.`
                                    : "Tum sistemler normal calisiyor."}
                            </p>
                        </div>
                    </div>

                    <div className="flex flex-wrap items-center gap-3">
                        <TimePill c={c} label="Saat" value={fmtTime(new Date(), true)} />
                        <TimePill c={c} label="Agent" value={fmtTime(dashboardUpdatedAt)} status={dashboardError ? "error" : "ok"} />
                        <TimePill c={c} label="SQL" value={fmtTime(sqlUpdatedAt)} status={sqlError ? (sqlDevices.length > 0 ? "cache" : "error") : (sqlLoading ? "loading" : "ok")} />
                        <button
                            onClick={() => { void loadDashboard(); void loadSql(); }}
                            className="flex h-10 w-10 items-center justify-center rounded-xl border transition-all hover:scale-105 active:scale-95"
                            style={{ background: c.cardSoft, borderColor: c.borderStrong, color: c.text }}
                            title="Yenile"
                        >
                            <RefreshCw className={`h-4 w-4 ${(loading || sqlLoading) ? "animate-spin" : ""}`} />
                        </button>
                    </div>
                </div>
            </section>

            {/* ═══ ERROR NOTICES ═══ */}
            {(dashboardError || sqlError) && (
                <div className="grid gap-3 xl:grid-cols-2">
                    {dashboardError && <Notice c={c} title="Agent ozeti gec geldi" message={dashboardError} />}
                    {sqlError && <Notice c={c} title={sqlDevices.length > 0 ? "SQL matrisi cache gosteriyor" : "SQL matrisi bekliyor"} message={sqlError} />}
                </div>
            )}

            {/* ═══ KPI CARDS ═══ */}
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-[repeat(3,minmax(0,1fr))_1.45fr]">
                <KpiCard c={c} icon={<Wifi />} title="Agent" value={`${agentPct}%`} sub={`${summary.online}/${summary.totalDevices} online`} accent={c.sky} glow={c.skyGlow} delay={0} percent={agentPct} />
                <KpiCard c={c} icon={<Database />} title="SQL Matrisi" value={`${sqlPct}%`} sub={`${sqlOnline}/${sqlTracked} cevapliyor`} accent={c.emerald} glow={c.emeraldGlow} delay={1} percent={sqlPct} />
                <KpiCard c={c} icon={<Building2 />} title="Magazalar" value={`${liveStoreCount}/${activeStoreCount || stores.length}`} sub={`${stores.length} toplam magaza`} accent={c.amber} glow={c.amberGlow} delay={2} percent={storePct} />
                <DeviceDistributionSummaryCard
                    c={c}
                    delay={3}
                    pcs={pcs.length}
                    posDevices={posDevices.length}
                    totalOnline={totalOnline}
                    totalOffline={totalOffline}
                    closedDevices={closedPos + closedPcs}
                    pcOnline={pcOnline}
                    posOnline={posOnline}
                    closedPos={closedPos}
                    closedPcs={closedPcs}
                    onOpen={() => setIsDeviceDistributionOpen(true)}
                />
            </div>

            {/* ═══ MAIN GRID: Charts + Focus list ═══ */}
            <div className="grid gap-5 xl:grid-cols-[1fr_420px]">
                {/* Left column */}
                <div className="space-y-5">
                    {/* Connection status grid — full width */}
                    <GlassCard c={c}>
                        <div className="mb-4 flex items-center justify-between">
                            <div className="flex items-center gap-3">
                                <Globe className="h-4 w-4" style={{ color: c.sky }} />
                                <div>
                                    <h3 className="text-sm font-semibold" style={{ color: c.text }}>Anlik Baglanti Durumu</h3>
                                    <p className="text-xs" style={{ color: c.muted }}>{totalActive} aktif cihaz izleniyor</p>
                                </div>
                            </div>
                            <div className="flex items-center gap-4">
                                <div className="flex items-center gap-1.5"><span className="h-2 w-2 rounded-full" style={{ background: c.emerald }} /><span className="text-[10px]" style={{ color: c.muted }}>Online</span></div>
                                <div className="flex items-center gap-1.5"><span className="h-2 w-2 rounded-full" style={{ background: c.rose }} /><span className="text-[10px]" style={{ color: c.muted }}>Offline</span></div>
                                <div className="flex items-center gap-1.5"><span className="h-2 w-2 rounded-full" style={{ background: c.slate }} /><span className="text-[10px]" style={{ color: c.muted }}>Kapali</span></div>
                            </div>
                        </div>
                        {/* PC row */}
                        <div className="mb-3">
                            <div className="mb-2 flex items-center gap-2">
                                <Laptop className="h-3.5 w-3.5" style={{ color: c.sky }} />
                                <span className="text-xs font-semibold" style={{ color: c.text }}>PC</span>
                                <span className="text-[10px]" style={{ color: c.muted }}>({pcOnline}/{pcs.length} online)</span>
                            </div>
                            <div className="flex flex-wrap gap-1.5">
                                {pcs.length > 0 ? [...pcs].sort((a, b) => (a.storeCode ?? 0) - (b.storeCode ?? 0)).map((d) => (
                                    <ConnectionDot key={d.deviceId} c={c} device={d} />
                                )) : <span className="text-xs" style={{ color: c.muted }}>PC verisi yok</span>}
                            </div>
                        </div>
                        {/* POS row */}
                        <div>
                            <div className="mb-2 flex items-center gap-2">
                                <MonitorSmartphone className="h-3.5 w-3.5" style={{ color: c.emerald }} />
                                <span className="text-xs font-semibold" style={{ color: c.text }}>POS / Kasa</span>
                                <span className="text-[10px]" style={{ color: c.muted }}>({posOnline}/{activePos.length} online{closedPos > 0 ? `, ${closedPos} kapali` : ""})</span>
                            </div>
                            <div className="flex flex-wrap gap-1.5">
                                {posDevices.length > 0 ? [...posDevices].sort((a, b) => (a.storeCode ?? 0) - (b.storeCode ?? 0)).map((d) => (
                                    <ConnectionDot key={d.deviceId} c={c} device={d} />
                                )) : <span className="text-xs" style={{ color: c.muted }}>POS verisi yok</span>}
                            </div>
                        </div>
                    </GlassCard>

                    {/* Distribution & devices */}
                    <div className="grid gap-4 lg:grid-cols-2">
                        {/* Device distribution donut */}
                        <GlassCard c={c}>
                            <div className="mb-3 flex items-center justify-between">
                                <div>
                                    <h3 className="text-sm font-semibold" style={{ color: c.text }}>Cihaz Dagilimi</h3>
                                    <p className="text-xs" style={{ color: c.muted }}>{pcs.length + posDevices.length} toplam cihaz</p>
                                </div>
                                <HardDrive className="h-4 w-4" style={{ color: c.amber }} />
                            </div>
                            <div className="flex items-center gap-6">
                                {deviceDonut.length > 0 ? (
                                    <ResponsiveContainer width={140} height={140}>
                                        <PieChart>
                                            <Pie data={deviceDonut} cx="50%" cy="50%" innerRadius={38} outerRadius={60} paddingAngle={2} dataKey="value" strokeWidth={0} animationBegin={0} animationDuration={1200} animationEasing="ease-out">
                                                {deviceDonut.map((entry, i) => <Cell key={i} fill={entry.color} />)}
                                            </Pie>
                                            <Tooltip contentStyle={{ background: c.card, border: `1px solid ${c.border}`, borderRadius: 12, fontSize: 12, color: c.text }} />
                                        </PieChart>
                                    </ResponsiveContainer>
                                ) : (
                                    <div className="flex h-[140px] w-[140px] items-center justify-center text-sm" style={{ color: c.muted }}>--</div>
                                )}
                                <div className="flex-1 space-y-2">
                                    <DeviceStat c={c} icon={<Laptop className="h-3.5 w-3.5" />} label="PC" online={pcs.length} total={pcs.length} color={c.sky} showTotal />
                                    <DeviceStat c={c} icon={<MonitorSmartphone className="h-3.5 w-3.5" />} label="POS" online={posDevices.length} total={posDevices.length} color={c.emerald} showTotal />
                                </div>
                            </div>
                        </GlassCard>

                        {/* Progress bars */}
                        <GlassCard c={c}>
                            <div className="mb-3 flex items-center justify-between">
                                <h3 className="text-sm font-semibold" style={{ color: c.text }}>Kapsama Oranlari</h3>
                                <Activity className="h-4 w-4" style={{ color: c.emerald }} />
                            </div>
                            <div className="space-y-4">
                                <ProgressLine c={c} label="Agent" value={agentPct} detail={`${summary.online}/${summary.totalDevices}`} color={c.sky} />
                                <ProgressLine c={c} label="SQL" value={sqlPct} detail={`${sqlOnline}/${sqlTracked}`} color={c.emerald} />
                                <ProgressLine c={c} label="Magaza" value={storePct} detail={`${liveStoreCount}/${activeStoreCount}`} color={c.amber} />
                                <ProgressLine c={c} label="Saglik" value={healthPct} detail={`${summary.healthy}/${summary.totalDevices}`} color={c.violet} />
                            </div>
                        </GlassCard>
                    </div>

                </div>

                {/* ═══ RIGHT SIDEBAR ═══ */}
                <div className="space-y-5">
                    {/* Offline cihazlar — üstte */}
                    <GlassCard c={c}>
                        <div className="mb-3 flex items-center justify-between">
                            <div>
                                <h3 className="text-sm font-semibold" style={{ color: c.text }}>Offline Cihazlar</h3>
                                <p className="text-[10px]" style={{ color: c.muted }}>{offlineDevices.length} cihaz erisim disi</p>
                            </div>
                            {offlineDevices.length > 0 && <span className="flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-bold text-white" style={{ background: c.rose }}>{offlineDevices.length}</span>}
                        </div>
                        {offlineDevices.length === 0 ? (
                            <div className="rounded-xl border border-dashed px-4 py-8 text-center" style={{ borderColor: c.emeraldBorder, background: c.emeraldGlow }}>
                                <ShieldCheck className="mx-auto h-6 w-6" style={{ color: c.emerald }} />
                                <p className="mt-2 text-xs font-medium" style={{ color: c.text }}>Tum cihazlar online</p>
                            </div>
                        ) : (
                            <div className="space-y-1.5 max-h-[320px] overflow-auto scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent">
                                {offlineDevices.map((d) => (
                                    <OfflineRow key={d.deviceId} c={c} device={d} />
                                ))}
                            </div>
                        )}
                        {/* Ozet satiri */}
                        <div className="mt-3 flex gap-2">
                            <div className="flex-1 rounded-lg px-2.5 py-1.5 text-center" style={{ background: c.emeraldGlow }}>
                                <div className="text-sm font-bold" style={{ color: c.emerald }}>{totalOnline}</div>
                                <div className="text-[9px]" style={{ color: c.muted }}>Online</div>
                            </div>
                            <div className="flex-1 rounded-lg px-2.5 py-1.5 text-center" style={{ background: c.roseGlow }}>
                                <div className="text-sm font-bold" style={{ color: c.rose }}>{totalOffline}</div>
                                <div className="text-[9px]" style={{ color: c.muted }}>Offline</div>
                            </div>
                            {(closedPos > 0 || closedPcs > 0) && (
                                <div className="flex-1 rounded-lg px-2.5 py-1.5 text-center" style={{ background: c.track }}>
                                    <div className="text-sm font-bold" style={{ color: c.slate }}>{closedPos + closedPcs}</div>
                                    <div className="text-[9px]" style={{ color: c.muted }}>Kapali</div>
                                    <div className="flex justify-center gap-2 mt-0.5">
                                        {closedPcs > 0 && <span className="text-[8px]" style={{ color: c.muted }}>{closedPcs} PC</span>}
                                        {closedPos > 0 && <span className="text-[8px]" style={{ color: c.muted }}>{closedPos} Kasa</span>}
                                    </div>
                                </div>
                            )}
                        </div>
                    </GlassCard>

                    {/* Kesinti & Kapali magazalar — altta */}
                    <GlassCard c={c}>
                        <div className="mb-4 flex items-center justify-between">
                            <h3 className="text-sm font-semibold" style={{ color: c.text }}>Kesinti & Kapali Magazalar</h3>
                            {attention.length > 0 && <span className="flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-bold text-white" style={{ background: issueCount > 0 ? c.rose : c.sky }}>{attention.length}</span>}
                        </div>
                        {attention.length === 0 ? (
                            <div className="rounded-2xl border border-dashed px-5 py-10 text-center" style={{ borderColor: c.emeraldBorder, background: c.emeraldGlow }}>
                                <ShieldCheck className="mx-auto h-8 w-8" style={{ color: c.emerald }} />
                                <p className="mt-3 text-sm font-medium" style={{ color: c.text }}>Acik kesinti yok</p>
                                <p className="mt-1 text-xs" style={{ color: c.muted }}>Tum hatlar temiz gorunuyor.</p>
                            </div>
                        ) : (
                            <div className="space-y-2">
                                {attention.map((store, i) => (
                                    <StoreCard key={store.code} c={c} store={store} rank={i + 1} />
                                ))}
                            </div>
                        )}
                    </GlassCard>
                </div>
            </div>

            <Modal
                isOpen={isDeviceDistributionOpen}
                onClose={() => setIsDeviceDistributionOpen(false)}
                title="Cihaz Dagilimi"
                size="lg"
            >
                <DeviceDistributionModalContent
                    c={c}
                    pcs={pcs.length}
                    posDevices={posDevices.length}
                    totalOnline={totalOnline}
                    totalOffline={totalOffline}
                    closedDevices={closedPos + closedPcs}
                    pcOnline={pcOnline}
                    posOnline={posOnline}
                    pcOffline={pcOffline}
                    posOffline={posOffline}
                    closedPos={closedPos}
                    closedPcs={closedPcs}
                    deviceDonut={deviceDonut}
                />
            </Modal>
        </div>
    );
};

/* ═══════════════════════════════════════════
   SUB-COMPONENTS
   ═══════════════════════════════════════════ */

function storeToneColor(status: StoreSummary["status"], c: Palette) {
    if (status === "critical") return c.rose;
    if (status === "watch") return c.amber;
    if (status === "closed") return c.sky;
    return c.emerald;
}

const OfflineRow: React.FC<{ c: Palette; device: SqlDeviceWithStatus }> = ({ c, device }) => {
    const d = device;
    const type = (d.deviceType || "").toUpperCase();
    const isPC = type === "PC";
    const label = isPC ? "PC" : type.replace("KASA-", "K");
    const mins = d.lastSeen ? Math.max(0, Math.floor((Date.now() - new Date(d.lastSeen).getTime()) / 60000)) : null;
    const dur = mins === null ? "?" : mins < 60 ? `${mins}dk` : mins < 1440 ? `${Math.floor(mins / 60)}sa ${mins % 60}dk` : `${Math.floor(mins / 1440)}g ${Math.floor((mins % 1440) / 60)}sa`;
    const urgent = mins !== null && mins >= 30;
    return (
        <div className="flex items-center gap-2 rounded-lg border px-2.5 py-2" style={{ background: c.cardSoft, borderColor: urgent ? c.roseBorder : c.border }}>
            <span className="h-2 w-2 shrink-0 rounded-full animate-pulse" style={{ background: c.rose }} />
            <div className="min-w-0 flex-1">
                <div className="flex items-center gap-1.5">
                    <span className="rounded px-1 py-0.5 text-[8px] font-bold" style={{ background: isPC ? c.skySoft : c.amberSoft, color: isPC ? c.sky : c.amber }}>{label}</span>
                    <span className="text-[11px] font-semibold truncate" style={{ color: c.text }}>[{d.storeCode}] {d.storeName}</span>
                </div>
                <div className="text-[10px] font-mono" style={{ color: c.subtle }}>{d.calculatedIpAddress}</div>
            </div>
            <div className="shrink-0 text-right">
                <div className="text-xs font-bold" style={{ color: urgent ? c.rose : c.amber }}>{dur}</div>
            </div>
        </div>
    );
};

const ConnectionDot: React.FC<{ c: Palette; device: SqlDeviceWithStatus }> = ({ c, device }) => {
    const isClosed = device.isTemporarilyClosed;
    const isOnline = device.isOnline;
    const color = isClosed ? c.slate : isOnline ? c.emerald : c.rose;
    const label = `${device.storeCode} / ${device.deviceName || device.deviceType}${isClosed ? " (kapali)" : isOnline ? " — online" : " — OFFLINE"}${device.lastSeen ? `\nSon: ${fmtDuration(device.lastSeen)}` : ""}`;
    return (
        <div
            className="group relative flex h-7 w-7 cursor-default items-center justify-center rounded-lg border text-[8px] font-bold transition-all duration-200 hover:scale-125 hover:z-10"
            style={{ background: color + "18", borderColor: color + "30", color, "--breathe-color": color + "40" } as React.CSSProperties}
            title={label}
        >
            {device.storeCode}
            {!isClosed && !isOnline && (
                <span className="absolute -right-0.5 -top-0.5 h-2 w-2 rounded-full" style={{ background: c.rose, animation: "ms-pulse-ring 2.5s ease-out infinite" }} />
            )}
            {isOnline && (
                <span className="absolute inset-0 rounded-lg opacity-0 transition-opacity duration-200 group-hover:opacity-100" style={{ boxShadow: `0 0 12px ${color}50` }} />
            )}
        </div>
    );
};

const GlassCard: React.FC<{ c: Palette; children: React.ReactNode; className?: string; style?: React.CSSProperties }> = ({ c, children, className = "", style }) => (
    <div
        className={`rounded-2xl border p-5 ms-fade-up ms-glass-card ${className}`}
        style={{ background: c.card, borderColor: c.border, boxShadow: `0 8px 32px ${c.shadow}`, backdropFilter: "blur(16px)", ...style }}
    >
        {children}
    </div>
);

const TimePill: React.FC<{ c: Palette; label: string; value: string; status?: "ok" | "error" | "cache" | "loading" }> = ({ c, label, value, status }) => (
    <div className="flex items-center gap-2 rounded-xl border px-3 py-2" style={{ background: c.cardSoft, borderColor: c.border }}>
        {status && (
            <span
                className="h-1.5 w-1.5 rounded-full"
                style={{ background: status === "ok" ? c.emerald : status === "error" ? c.rose : status === "cache" ? c.amber : c.sky }}
            />
        )}
        <div>
            <div className="text-[9px] font-bold uppercase tracking-widest" style={{ color: c.subtle }}>{label}</div>
            <div className="text-xs font-semibold" style={{ color: c.text }}>{value}</div>
        </div>
    </div>
);

const AnimatedRing: React.FC<{ percent: number; color: string; size?: number; strokeWidth?: number; delay?: number }> = ({ percent, color, size = 80, strokeWidth = 6, delay = 0 }) => {
    const radius = (size - strokeWidth) / 2;
    const circumference = 2 * Math.PI * radius;
    const offset = circumference - (Math.min(percent, 100) / 100) * circumference;
    return (
        <svg width={size} height={size} className="shrink-0 -rotate-90 drop-shadow-lg" style={{ filter: `drop-shadow(0 0 8px ${color}40)` }}>
            <circle cx={size / 2} cy={size / 2} r={radius} fill="none" stroke={color + "18"} strokeWidth={strokeWidth} />
            <circle
                cx={size / 2} cy={size / 2} r={radius} fill="none" stroke={`url(#grad-${delay})`} strokeWidth={strokeWidth}
                strokeLinecap="round" strokeDasharray={circumference} strokeDashoffset={offset}
                style={{ "--ring-circumference": String(circumference), "--ring-offset": String(offset), animation: `ms-ring-fill 1.5s cubic-bezier(0.16,1,0.3,1) ${delay}ms both` } as React.CSSProperties}
            />
            <defs>
                <linearGradient id={`grad-${delay}`} x1="0%" y1="0%" x2="100%" y2="0%">
                    <stop offset="0%" stopColor={color} stopOpacity="0.6" />
                    <stop offset="100%" stopColor={color} stopOpacity="1" />
                </linearGradient>
            </defs>
        </svg>
    );
};

const KpiCard: React.FC<{ c: Palette; icon: React.ReactNode; title: string; value: string; sub: string; accent: string; glow: string; delay: number; percent?: number }> = ({ c, icon, title, value, sub, accent, glow, delay, percent }) => {
    const numericValue = parseInt(value) || 0;
    const animatedVal = useAnimatedNumber(numericValue, 1200);
    const displayValue = value.includes("%") ? `${animatedVal}%` : value.includes("/") ? value : `${animatedVal}`;

    return (
        <div
            className="group relative overflow-hidden rounded-2xl border p-5 ms-fade-up ms-glass-hover"
            style={{ background: c.card, borderColor: c.border, boxShadow: `0 8px 32px ${c.shadow}`, animationDelay: `${delay * 120}ms` }}
        >
            {/* Animated gradient background */}
            <div
                className="pointer-events-none absolute inset-0 opacity-0 transition-opacity duration-500 group-hover:opacity-100"
                style={{ background: `linear-gradient(135deg, ${accent}08 0%, ${accent}15 50%, transparent 100%)` }}
            />
            {/* Breathing glow orb */}
            <div
                className="pointer-events-none absolute -right-6 -top-6 h-28 w-28 rounded-full"
                style={{ background: `radial-gradient(circle, ${accent}25 0%, transparent 70%)`, animation: `ms-glow-breathe 4s ease-in-out infinite`, animationDelay: `${delay * 300}ms` }}
            />
            <div className="relative flex items-center gap-4">
                {/* Animated ring with icon center */}
                {percent !== undefined ? (
                    <div className="relative shrink-0">
                        <AnimatedRing percent={percent} color={accent} delay={delay * 120 + 400} />
                        <div className="absolute inset-0 flex flex-col items-center justify-center">
                            <span className="text-lg font-black" style={{ color: accent, animation: `ms-number-pop 0.6s cubic-bezier(0.16,1,0.3,1) ${delay * 120 + 600}ms both` }}>{Math.min(percent, 100)}</span>
                            <span className="text-[8px] font-bold uppercase" style={{ color: accent + "90" }}>%</span>
                        </div>
                    </div>
                ) : (
                    <div className="relative shrink-0 flex h-[80px] w-[80px] items-center justify-center">
                        <div className="flex h-14 w-14 items-center justify-center rounded-2xl" style={{ background: `linear-gradient(135deg, ${accent}20, ${accent}35)`, boxShadow: `0 4px 20px ${accent}25` }}>
                            {React.cloneElement(icon as React.ReactElement, { className: "h-6 w-6", style: { color: accent } })}
                        </div>
                        <div className="absolute inset-0 rounded-full" style={{ border: `2px solid ${accent}15` }} />
                    </div>
                )}
                <div className="min-w-0 flex-1">
                    <div className="text-[10px] font-bold uppercase tracking-widest" style={{ color: c.subtle }}>{title}</div>
                    <div className="mt-1 text-3xl font-black tracking-tight" style={{ color: c.text }}>{displayValue}</div>
                    <div className="mt-1 text-xs" style={{ color: c.muted }}>{sub}</div>
                </div>
                {/* Accent icon top-right */}
                <div className="absolute right-0 top-0 p-1" style={{ color: accent + "30" }}>
                    {React.cloneElement(icon as React.ReactElement, { className: "h-8 w-8" })}
                </div>
            </div>
        </div>
    );
};

const DeviceDistributionSummaryCard: React.FC<{
    c: Palette;
    delay: number;
    pcs: number;
    posDevices: number;
    totalOnline: number;
    totalOffline: number;
    closedDevices: number;
    pcOnline: number;
    posOnline: number;
    closedPos: number;
    closedPcs: number;
    onOpen: () => void;
}> = ({ c, delay, pcs, posDevices, totalOnline, totalOffline, closedDevices, pcOnline, posOnline, closedPos, closedPcs, onOpen }) => {
    const totalDevices = pcs + posDevices;
    const onlinePct = pct(totalOnline, Math.max(totalDevices - closedDevices, 0));

    return (
        <button
            type="button"
            onClick={onOpen}
            className="sm:col-span-2 xl:col-span-1 text-left focus:outline-none"
        >
            <GlassCard
                c={c}
                className="h-full border-none p-0"
                style={{ animationDelay: `${delay * 120}ms`, background: `linear-gradient(145deg, ${c.card} 0%, ${c.cardSoft} 100%)`, boxShadow: `0 14px 36px ${c.shadow}` }}
            >
                <div className="flex h-full flex-col gap-4 p-5">
                    <div>
                        <div className="flex items-center justify-between gap-3">
                            <div>
                                <h3 className="text-sm font-semibold" style={{ color: c.text }}>Cihaz Dagilimi</h3>
                                <p className="text-xs" style={{ color: c.muted }}>{totalDevices} cihaz tek bakista</p>
                            </div>
                            <div className="flex h-10 w-10 items-center justify-center rounded-2xl" style={{ background: `linear-gradient(135deg, ${c.amberSoft}, ${c.skySoft})`, color: c.text }}>
                                <HardDrive className="h-4 w-4" />
                            </div>
                        </div>
                    </div>

                    <div className="grid grid-cols-3 gap-2">
                        <CompactMetric c={c} label="Online" value={`${onlinePct}%`} hint={`${totalOnline} cihaz`} color={c.emerald} />
                        <CompactMetric c={c} label="Offline" value={`${totalOffline}`} hint="erisim disi" color={c.rose} />
                        <CompactMetric c={c} label="Kapali" value={`${closedDevices}`} hint="beklemede" color={c.slate} />
                    </div>

                    <div className="space-y-2">
                        <MiniDistributionRow c={c} label="PC" value={pcs} online={pcOnline} closed={closedPcs} color={c.sky} />
                        <MiniDistributionRow c={c} label="POS" value={posDevices} online={posOnline} closed={closedPos} color={c.emerald} />
                    </div>

                    <div className="flex items-center justify-between rounded-2xl border px-3 py-2.5" style={{ background: c.cardAlt, borderColor: c.border }}>
                        <div>
                            <div className="text-[10px] font-bold uppercase tracking-widest" style={{ color: c.subtle }}>Detay gorunumu</div>
                            <div className="text-xs font-medium" style={{ color: c.text }}>Dagilim ve durum ozetini ac</div>
                        </div>
                        <ChevronRight className="h-4 w-4" style={{ color: c.subtle }} />
                    </div>
                </div>
            </GlassCard>
        </button>
    );
};

const DeviceDistributionModalContent: React.FC<{
    c: Palette;
    pcs: number;
    posDevices: number;
    totalOnline: number;
    totalOffline: number;
    closedDevices: number;
    pcOnline: number;
    posOnline: number;
    pcOffline: number;
    posOffline: number;
    closedPos: number;
    closedPcs: number;
    deviceDonut: Array<{ name: string; value: number; color: string }>;
}> = ({ c, pcs, posDevices, totalOnline, totalOffline, closedDevices, pcOnline, posOnline, pcOffline, posOffline, closedPos, closedPcs, deviceDonut }) => {
    const totalDevices = pcs + posDevices;
    const segments = [
        { label: "PC", total: pcs, online: pcOnline, offline: pcOffline, closed: closedPcs, color: c.sky },
        { label: "POS", total: posDevices, online: posOnline, offline: posOffline, closed: closedPos, color: c.emerald },
    ];

    return (
        <div className="space-y-5">
            <div className="grid gap-3 sm:grid-cols-4">
                <CompactMetric c={c} label="Toplam" value={`${totalDevices}`} hint="cihaz" color={c.text} />
                <CompactMetric c={c} label="Online" value={`${totalOnline}`} hint="aktif" color={c.emerald} />
                <CompactMetric c={c} label="Offline" value={`${totalOffline}`} hint="erisim disi" color={c.rose} />
                <CompactMetric c={c} label="Kapali" value={`${closedDevices}`} hint="beklemede" color={c.slate} />
            </div>

            <div className="grid gap-5 lg:grid-cols-[240px_1fr]">
                <div className="rounded-2xl border p-4" style={{ background: `linear-gradient(180deg, ${c.cardAlt}, ${c.cardSoft})`, borderColor: c.border }}>
                    <div className="mb-3 flex items-center justify-between">
                        <div>
                            <div className="text-sm font-semibold" style={{ color: c.text }}>Tip Dagilimi</div>
                            <div className="text-xs" style={{ color: c.muted }}>PC ve POS orani</div>
                        </div>
                        <HardDrive className="h-4 w-4" style={{ color: c.amber }} />
                    </div>
                    {deviceDonut.length > 0 ? (
                        <div className="mx-auto h-[180px] w-[180px]">
                            <ResponsiveContainer width="100%" height="100%">
                                <PieChart>
                                    <Pie data={deviceDonut} cx="50%" cy="50%" innerRadius={48} outerRadius={74} paddingAngle={3} dataKey="value" strokeWidth={0}>
                                        {deviceDonut.map((entry, i) => <Cell key={i} fill={entry.color} />)}
                                    </Pie>
                                    <Tooltip contentStyle={{ background: c.card, border: `1px solid ${c.border}`, borderRadius: 12, fontSize: 12, color: c.text }} />
                                </PieChart>
                            </ResponsiveContainer>
                        </div>
                    ) : (
                        <div className="flex h-[180px] items-center justify-center text-sm" style={{ color: c.muted }}>Veri yok</div>
                    )}
                    <div className="mt-3 flex flex-wrap gap-2">
                        {deviceDonut.map((entry) => (
                            <MiniTag key={entry.name} c={c} label={`${entry.name} ${entry.value}`} color={entry.color} />
                        ))}
                    </div>
                </div>

                <div className="space-y-3">
                    {segments.map((segment) => (
                        <div key={segment.label} className="rounded-2xl border p-4" style={{ background: c.cardAlt, borderColor: c.border }}>
                            <div className="mb-3 flex items-center justify-between">
                                <div>
                                    <div className="text-sm font-semibold" style={{ color: c.text }}>{segment.label}</div>
                                    <div className="text-xs" style={{ color: c.muted }}>{segment.total} cihaz</div>
                                </div>
                                <span className="rounded-full px-2.5 py-1 text-[10px] font-bold uppercase" style={{ background: segment.color + "18", color: segment.color }}>
                                    {pct(segment.online, Math.max(segment.total - segment.closed, 0))}% online
                                </span>
                            </div>

                            <div className="mb-3 flex h-2.5 overflow-hidden rounded-full" style={{ background: c.track }}>
                                {segment.online > 0 && <div style={{ width: `${(segment.online / Math.max(segment.total, 1)) * 100}%`, background: segment.color }} />}
                                {segment.offline > 0 && <div style={{ width: `${(segment.offline / Math.max(segment.total, 1)) * 100}%`, background: c.rose }} />}
                                {segment.closed > 0 && <div style={{ width: `${(segment.closed / Math.max(segment.total, 1)) * 100}%`, background: c.slate }} />}
                            </div>

                            <div className="grid grid-cols-3 gap-2">
                                <CompactMetric c={c} label="Online" value={`${segment.online}`} hint="bagli" color={segment.color} />
                                <CompactMetric c={c} label="Offline" value={`${segment.offline}`} hint="sorunlu" color={c.rose} />
                                <CompactMetric c={c} label="Kapali" value={`${segment.closed}`} hint="beklemede" color={c.slate} />
                            </div>
                        </div>
                    ))}
                </div>
            </div>
        </div>
    );
};

const CompactMetric: React.FC<{ c: Palette; label: string; value: string; hint: string; color: string }> = ({ c, label, value, hint, color }) => (
    <div className="rounded-2xl border px-3 py-3" style={{ background: `linear-gradient(135deg, ${c.cardSoft}, ${color === c.text ? c.cardSoft : color + "08"})`, borderColor: c.border }}>
        <div className="text-[9px] font-bold uppercase tracking-widest" style={{ color: c.subtle }}>{label}</div>
        <div className="mt-1 text-xl font-black tabular-nums" style={{ color }}>{value}</div>
        <div className="text-[10px]" style={{ color: c.muted }}>{hint}</div>
    </div>
);

const MiniDistributionRow: React.FC<{ c: Palette; label: string; value: number; online: number; closed: number; color: string }> = ({ c, label, value, online, closed, color }) => {
    const offline = Math.max(value - online - closed, 0);
    return (
        <div className="rounded-2xl border px-3 py-3" style={{ background: c.cardAlt, borderColor: c.border }}>
            <div className="mb-2 flex items-center justify-between">
                <span className="text-xs font-semibold" style={{ color: c.text }}>{label}</span>
                <span className="text-[10px] font-bold uppercase" style={{ color }}>{value} cihaz</span>
            </div>
            <div className="flex h-2 overflow-hidden rounded-full" style={{ background: c.track }}>
                {online > 0 && <div style={{ width: `${(online / Math.max(value, 1)) * 100}%`, background: color }} />}
                {offline > 0 && <div style={{ width: `${(offline / Math.max(value, 1)) * 100}%`, background: c.rose }} />}
                {closed > 0 && <div style={{ width: `${(closed / Math.max(value, 1)) * 100}%`, background: c.slate }} />}
            </div>
            <div className="mt-2 flex items-center justify-between text-[10px]" style={{ color: c.muted }}>
                <span>{online} online</span>
                <span>{offline} offline</span>
                <span>{closed} kapali</span>
            </div>
        </div>
    );
};

const Notice: React.FC<{ c: Palette; title: string; message: string }> = ({ c, title, message }) => (
    <div className="flex items-start gap-3 rounded-2xl border p-4" style={{ background: c.roseSoft, borderColor: c.roseBorder }}>
        <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" style={{ color: c.rose }} />
        <div>
            <div className="text-sm font-semibold" style={{ color: c.text }}>{title}</div>
            <div className="mt-1 text-xs" style={{ color: c.muted }}>{message}</div>
        </div>
    </div>
);

const ProgressLine: React.FC<{ c: Palette; label: string; value: number; detail: string; color: string }> = ({ c, label, value, detail, color }) => {
    const animatedVal = useAnimatedNumber(value, 1200);
    return (
        <div>
            <div className="mb-2 flex items-center justify-between">
                <div className="flex items-center gap-2">
                    <span className="h-2 w-2 rounded-full" style={{ background: color, boxShadow: `0 0 6px ${color}60` }} />
                    <span className="text-xs font-semibold" style={{ color: c.text }}>{label}</span>
                </div>
                <div className="flex items-center gap-2">
                    <span className="text-[10px]" style={{ color: c.muted }}>{detail}</span>
                    <span className="min-w-[36px] text-right text-sm font-black tabular-nums" style={{ color }}>{animatedVal}%</span>
                </div>
            </div>
            <div className="h-3 overflow-hidden rounded-full" style={{ background: c.track }}>
                <div
                    className="h-full rounded-full ms-shimmer-bar"
                    style={{ width: `${value}%`, background: `linear-gradient(90deg, ${color}80, ${color})`, boxShadow: `0 0 12px ${color}30`, animation: `ms-bar-grow 1.2s cubic-bezier(0.16,1,0.3,1) both` }}
                />
            </div>
        </div>
    );
};

const DeviceStat: React.FC<{ c: Palette; icon: React.ReactNode; label: string; online: number; total: number; color: string; isClosed?: boolean; showTotal?: boolean }> = ({ c, icon, label, online, total, color, isClosed, showTotal }) => {
    const animatedVal = useAnimatedNumber(total, 1200);
    return (
        <div className="flex items-center gap-3 rounded-xl border px-3 py-2 ms-fade-up" style={{ background: c.cardSoft, borderColor: c.border }}>
            <div style={{ color }}>{icon}</div>
            <div className="flex-1">
                <div className="text-xs font-semibold" style={{ color: c.text }}>{label}</div>
                <div className="text-[10px] font-semibold tabular-nums" style={{ color: showTotal ? color : c.muted }}>
                    {showTotal ? `${animatedVal} cihaz` : isClosed ? `${total} beklemede` : `${online}/${total} online`}
                </div>
            </div>
        </div>
    );
};

const StoreCard: React.FC<{ c: Palette; store: StoreSummary; rank: number }> = ({ c, store, rank }) => {
    const color = storeToneColor(store.status, c);
    const statusLabel = store.status === "critical" ? "Kritik" : store.status === "watch" ? "Izleme" : store.status === "closed" ? "Kapali" : "Stabil";
    const isCritical = store.status === "critical";
    return (
        <div
            className="flex items-center gap-3 rounded-xl border p-3 transition-all duration-300 ms-glass-hover"
            style={{ background: isCritical ? color + "08" : c.cardSoft, borderColor: isCritical ? color + "30" : c.border, boxShadow: isCritical ? `0 0 20px ${color}10` : "none" }}
        >
            <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-[11px] font-bold" style={{ background: `linear-gradient(135deg, ${color}20, ${color}35)`, color, boxShadow: `0 2px 8px ${color}20` }}>
                {rank}
            </div>
            <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                    <span className="text-xs font-bold" style={{ color: c.text }}>{store.code}</span>
                    <span className="truncate text-xs" style={{ color: c.muted }}>{store.name}</span>
                </div>
                <div className="mt-1 flex items-center gap-2">
                    <span className="rounded-full px-2 py-0.5 text-[9px] font-bold uppercase" style={{ background: color + "20", color, boxShadow: isCritical ? `0 0 8px ${color}25` : "none" }}>{statusLabel}</span>
                    <span className="text-[10px]" style={{ color: c.subtle }}>
                        {store.status === "closed" ? (store.reason || "Planli kapali") : `${store.onlinePos}/${store.activePos} POS`}
                    </span>
                    {store.since && <span className="text-[10px] font-semibold" style={{ color }}>{fmtDuration(store.since)}</span>}
                </div>
            </div>
        </div>
    );
};

const NavLink: React.FC<{ c: Palette; to: string; icon: React.ReactNode; label: string; accent: string }> = ({ c, to, icon, label, accent }) => (
    <Link to={to} className="group flex items-center gap-3 rounded-xl border px-4 py-2.5 transition-all duration-200 hover:scale-[1.02]" style={{ background: c.cardSoft, borderColor: c.border }}>
        <div className="flex h-8 w-8 items-center justify-center rounded-lg transition-all duration-200 group-hover:scale-110" style={{ background: accent + "15", color: accent }}>{icon}</div>
        <span className="flex-1 text-sm font-medium" style={{ color: c.text }}>{label}</span>
        <ChevronRight className="h-3.5 w-3.5 transition-transform duration-200 group-hover:translate-x-1" style={{ color: c.subtle }} />
    </Link>
);

const MiniTag: React.FC<{ c: Palette; label: string; color: string }> = ({ c, label, color }) => (
    <span className="rounded-full px-2 py-0.5 text-[10px] font-semibold" style={{ background: color + "18", color }}>{label}</span>
);

const QuickStat: React.FC<{ c: Palette; label: string; value: string; color: string }> = ({ c, label, value, color }) => (
    <div className="rounded-xl border px-3 py-3" style={{ background: `linear-gradient(135deg, ${c.cardSoft}, ${color}06)`, borderColor: c.border }}>
        <div className="text-[9px] font-bold uppercase tracking-widest" style={{ color: c.subtle }}>{label}</div>
        <div className="mt-1.5 text-2xl font-black tabular-nums" style={{ color, textShadow: `0 0 20px ${color}20` }}>{value}</div>
    </div>
);

export default DashboardPage;
