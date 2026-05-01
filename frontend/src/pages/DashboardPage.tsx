import React, { useEffect, useMemo, useState, useCallback } from "react";
import { Link } from "react-router-dom";
import {
    Activity,
    AlertTriangle,
    Globe,
    Laptop,
    Monitor,
    MonitorSmartphone,
    RefreshCw,
    ShieldCheck,
    Wifi,
    WifiOff,
} from "lucide-react";
import AgendaTopicsPanel from "../components/dashboard/AgendaTopicsPanel";
import TurkeyStoreMap from "../components/dashboard/TurkeyStoreMap";
import { useTheme } from "../contexts/ThemeContext";
import { apiClient, SqlDeviceWithStatus, RouterClassification } from "../lib/apiClient";

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
const SQL_CACHE_KEY = "ms_sql_devices_cache_v5";
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
    return "az önce";
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
    const [mobileRouters, setMobileRouters] = useState<RouterClassification[]>([]);
    const [connectionView, setConnectionView] = useState<"grid" | "map">("grid");
    const [, setTick] = useState(0);

    // Süre gösterimi için 30 saniyede bir güncelle (her saniye değil — gereksiz re-render önlenir)
    useEffect(() => {
        const timer = window.setInterval(() => setTick((v) => v + 1), 30000);
        return () => window.clearInterval(timer);
    }, []);

    const loadDashboard = useCallback(async () => {
        setDashboardError(null);
        try {
            const result = await apiClient.getDashboard();
            setData({ totalDevices: result.totalDevices, online: result.online, offline: result.offline, healthy: result.healthy, warning: result.warning, critical: result.critical });
            setDashboardUpdatedAt(new Date());
        } catch (error) {
            setDashboardError(error instanceof Error ? error.message : "Agent özeti alınamadı.");
        } finally {
            setLoading(false);
        }
    }, []);

    const loadSql = useCallback(async () => {
        setSqlLoading(true);
        setSqlError(null);
        try {
            const result = await apiClient.getSqlDevicesWithStatus({ timeoutMs: 500 });
            const now = new Date();
            setSqlDevices(result);
            setSqlUpdatedAt(now);
            localStorage.setItem(SQL_CACHE_KEY, JSON.stringify({ savedAt: now.toISOString(), items: result }));
        } catch (error) {
            setSqlError(error instanceof Error ? error.message : "SQL matrisi alınamadı.");
        } finally {
            setSqlLoading(false);
        }
    }, []);

    const loadRouterDiag = useCallback(async () => {
        try {
            const res = await apiClient.getRouterDiagnostics(10);
            // Sadece mobil suphesi + kesin mobil + switchover gorunsun
            const flagged = res.routers.filter(r =>
                r.class === 'MobileSuspected' || r.class === 'MobileLikely' || r.switchoverDetected
            );
            setMobileRouters(flagged);
        } catch {
            // sessizce gormezden gel — widget gizlenir
        }
    }, []);

    useEffect(() => {
        void Promise.all([loadDashboard(), loadSql(), loadRouterDiag()]);
        const interval = window.setInterval(() => { void Promise.all([loadDashboard(), loadSql(), loadRouterDiag()]); }, 15000);
        return () => window.clearInterval(interval);
    }, [loadDashboard, loadSql, loadRouterDiag]);

    useEffect(() => {
        const handleRefresh = () => {
            void Promise.all([loadDashboard(), loadSql()]);
        };
        window.addEventListener("ms-dashboard-refresh", handleRefresh);
        return () => window.removeEventListener("ms-dashboard-refresh", handleRefresh);
    }, [loadDashboard, loadSql]);

    /* ─── Derived data (tek geçişte hesapla) ─── */
    const summary = data ?? EMPTY_DASHBOARD;

    const { pcs, posDevices, routers, geciciPcs, activePos, activePcs, activeRouters, activeGecici,
        closedPos, closedPcs, closedRouters, closedGecici,
        pcOnline, posOnline, routerOnline, geciciOnline,
        pcOffline, posOffline, routerOffline, geciciOffline,
        sqlOnline, sqlTracked } = useMemo(() => {
        const _pcs: SqlDeviceWithStatus[] = [];
        const _pos: SqlDeviceWithStatus[] = [];
        const _routers: SqlDeviceWithStatus[] = [];
        const _gecici: SqlDeviceWithStatus[] = [];
        const _activePos: SqlDeviceWithStatus[] = [];
        const _activePcs: SqlDeviceWithStatus[] = [];
        const _activeRouters: SqlDeviceWithStatus[] = [];
        const _activeGecici: SqlDeviceWithStatus[] = [];
        let _closedPos = 0, _closedPcs = 0, _closedRouters = 0, _closedGecici = 0;
        let _pcOn = 0, _posOn = 0, _routerOn = 0, _geciciOn = 0;

        for (const d of sqlDevices) {
            const type = d.deviceType?.toUpperCase();
            if (type === "PC") {
                _pcs.push(d);
                if (d.isTemporarilyClosed) _closedPcs++;
                else { _activePcs.push(d); if (d.isOnline) _pcOn++; }
            } else if (type?.startsWith("KASA")) {
                _pos.push(d);
                if (d.isTemporarilyClosed) _closedPos++;
                else { _activePos.push(d); if (d.isOnline) _posOn++; }
            } else if (type === "ROUTER") {
                _routers.push(d);
                if (d.isTemporarilyClosed) _closedRouters++;
                else { _activeRouters.push(d); if (d.isOnline) _routerOn++; }
            } else if (type === "GECICI") {
                _gecici.push(d);
                if (d.isTemporarilyClosed) _closedGecici++;
                else { _activeGecici.push(d); if (d.isOnline) _geciciOn++; }
            }
        }
        return {
            pcs: _pcs, posDevices: _pos, routers: _routers, geciciPcs: _gecici,
            activePos: _activePos, activePcs: _activePcs, activeRouters: _activeRouters, activeGecici: _activeGecici,
            closedPos: _closedPos, closedPcs: _closedPcs, closedRouters: _closedRouters, closedGecici: _closedGecici,
            pcOnline: _pcOn, posOnline: _posOn, routerOnline: _routerOn, geciciOnline: _geciciOn,
            pcOffline: _activePcs.length - _pcOn, posOffline: _activePos.length - _posOn,
            routerOffline: _activeRouters.length - _routerOn, geciciOffline: _activeGecici.length - _geciciOn,
            sqlOnline: _pcOn + _posOn + _geciciOn,
            sqlTracked: _activePcs.length + _activePos.length + _activeGecici.length,
        };
    }, [sqlDevices]);

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
            .filter((d) => !d.isOnline && !d.isTemporarilyClosed && d.deviceType?.toUpperCase() !== "GECICI")
            .sort((a, b) => {
                const ta = a.lastSeen ? new Date(a.lastSeen).getTime() : 0;
                const tb = b.lastSeen ? new Date(b.lastSeen).getTime() : 0;
                return tb - ta; // en yeni kapanan uste
            });
    }, [sqlDevices]);

    /* Donut data */
    const totalOnline = pcOnline + posOnline + routerOnline + geciciOnline;
    // Gecici PC'ler operasyonel offline sayisina dahil edilmez.
    const totalOffline = pcOffline + posOffline + routerOffline;
    const totalActive = totalOnline + totalOffline;

    /* ─── StatusBand event ─── */
    useEffect(() => {
        window.dispatchEvent(new CustomEvent("ms-dashboard-status", {
            detail: {
                enabled: true,
                message: "Kontrol Paneli verileri anlık olarak yenileniyor",
                agentTime: fmtTime(dashboardUpdatedAt),
                sqlTime: fmtTime(sqlUpdatedAt),
                agentState: dashboardError ? "error" : "ok",
                sqlState: sqlError ? (sqlDevices.length > 0 ? "cache" : "error") : (sqlLoading ? "loading" : "ok"),
                isRefreshing: loading || sqlLoading,
            },
        }));
        return () => {
            window.dispatchEvent(new CustomEvent("ms-dashboard-status", { detail: { enabled: false } }));
        };
    }, [dashboardUpdatedAt, sqlUpdatedAt, dashboardError, sqlError, sqlDevices.length, sqlLoading, loading]);

    /* ─── Loading state ─── */
    const isInitialLoad = (loading && !data) || (sqlLoading && sqlDevices.length === 0);
    if (isInitialLoad) {
        return (
            <div className="flex min-h-[68vh] items-center justify-center">
                <div className="rounded-3xl border p-10 text-center ms-fade-up" style={{ background: c.card, borderColor: c.border, boxShadow: `0 24px 60px ${c.shadow}` }}>
                    <div className="relative mx-auto h-16 w-16 mb-5">
                        <div className="absolute inset-0 rounded-full border-4 border-t-transparent animate-spin" style={{ borderColor: `${c.sky}30`, borderTopColor: c.sky }} />
                        <div className="absolute inset-2 rounded-full border-4 border-b-transparent animate-spin" style={{ borderColor: `${c.emerald}20`, borderBottomColor: c.emerald, animationDirection: 'reverse', animationDuration: '1.5s' }} />
                        <div className="absolute inset-0 flex items-center justify-center">
                            <div className="h-3 w-3 rounded-full animate-pulse" style={{ background: c.sky }} />
                        </div>
                    </div>
                    <h2 className="text-xl font-semibold" style={{ color: c.text }}>Kontrol Paneli</h2>
                    <p className="mt-2 text-sm" style={{ color: c.muted }}>Veriler hazırlanıyor...</p>
                    <div className="flex items-center justify-center gap-4 mt-5">
                        <div className="flex items-center gap-2 text-xs" style={{ color: data ? c.emerald : c.muted }}>
                            <div className={`h-1.5 w-1.5 rounded-full ${data ? '' : 'animate-pulse'}`} style={{ background: data ? c.emerald : c.muted }} />
                            Agent {data ? '✓' : '...'}
                        </div>
                        <div className="flex items-center gap-2 text-xs" style={{ color: sqlDevices.length > 0 ? c.emerald : c.muted }}>
                            <div className={`h-1.5 w-1.5 rounded-full ${sqlDevices.length > 0 ? '' : 'animate-pulse'}`} style={{ background: sqlDevices.length > 0 ? c.emerald : c.muted }} />
                            SQL Matrisi {sqlDevices.length > 0 ? '✓' : '...'}
                        </div>
                    </div>
                </div>
            </div>
        );
    }

    const statusColor = level === "critical" ? c.rose : level === "watch" ? c.amber : c.emerald;
    const statusLabel = level === "critical" ? "Kritik" : level === "watch" ? "Izleme" : "Stabil";

    return (
        <div className="mx-auto max-w-[1600px] space-y-5 pb-8">
            {/* ═══ SIMPLE SUMMARY ═══ */}
            <section
                className="rounded-2xl border px-5 py-4 ms-fade-up"
                style={{ background: c.card, borderColor: c.border, boxShadow: `0 8px 28px ${c.shadow}` }}
            >
                <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
                    <div className="min-w-0">
                        <div className="flex flex-wrap items-center gap-2">
                            <h1 className="text-xl font-bold tracking-tight" style={{ color: c.text }}>Kontrol Paneli</h1>
                            <span className="rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-widest" style={{ color: statusColor, background: statusColor + "12", borderColor: statusColor + "35" }}>
                                {statusLabel}
                            </span>
                        </div>
                        <p className="mt-1 text-sm" style={{ color: c.muted }}>
                            {criticalStores.length > 0 ? `${criticalStores.length} mağazada tam kesinti var.`
                                : watchStores.length > 0 ? `${watchStores.length} mağazada parçalı kayıp var.`
                                : closedStores.length > 0 ? `Temiz. ${closedStores.length} mağaza planlı kapalı.`
                                : "Temiz. Kritik aksiyon yok."}
                        </p>
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                        <SummaryPill c={c} label="Agent" value={`${agentPct}%`} color={c.sky} />
                        <SummaryPill c={c} label="SQL" value={`${sqlPct}%`} color={c.emerald} />
                        <SummaryPill c={c} label="Mağaza" value={`${storePct}%`} color={c.amber} />
                        <SummaryPill c={c} label="Offline" value={offlineDevices.length} color={offlineDevices.length > 0 ? c.rose : c.emerald} />
                        <div className="hidden h-5 w-px xl:block" style={{ background: c.border }} />
                        <div className="flex gap-2">
                            <Link
                                to="/offline-logs"
                                className="rounded-lg border px-3 py-2 text-xs font-semibold transition-colors hover:bg-white/[0.06]"
                                style={{ color: c.text, borderColor: c.border, background: c.cardSoft }}
                            >
                                Kesintiler
                            </Link>
                            <Link
                                to="/devices"
                                className="rounded-lg border px-3 py-2 text-xs font-semibold transition-colors hover:bg-white/[0.06]"
                                style={{ color: c.text, borderColor: c.border, background: c.cardSoft }}
                            >
                                Cihazlar
                            </Link>
                        </div>
                    </div>
                </div>
            </section>

            {/* ═══ ERROR NOTICES ═══ */}
            {(dashboardError || sqlError) && (
                <div className="grid gap-3 xl:grid-cols-2">
                    {dashboardError && <Notice c={c} title="Agent özeti geç geldi" message={dashboardError} />}
                    {sqlError && <Notice c={c} title={sqlDevices.length > 0 ? "SQL matrisi cache gösteriyor" : "SQL matrisi bekleniyor"} message={sqlError} />}
                </div>
            )}

            {/* ═══ MAIN GRID: Charts + Focus list ═══ */}
            <div className="grid gap-5 xl:grid-cols-[1fr_420px]">
                {/* Left column */}
                <div className="space-y-5">
                    {/* Connection status grid — full width */}
                    <GlassCard c={c}>
                        <div className="mb-4 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
                            <div className="flex items-center gap-3">
                                <Globe className="h-4 w-4" style={{ color: c.sky }} />
                                <div>
                                    <h3 className="text-sm font-semibold" style={{ color: c.text }}>Anlık Bağlantı Durumu</h3>
                                    <p className="text-xs" style={{ color: c.muted }}>{totalActive} aktif cihaz izleniyor</p>
                                </div>
                            </div>
                            <div className="flex flex-wrap items-center gap-3 lg:justify-end">
                                {/* Durum */}
                                <div className="flex items-center gap-1.5"><span className="h-2 w-2 rounded-full" style={{ background: c.emerald }} /><span className="text-[10px]" style={{ color: c.muted }}>Online</span></div>
                                <div className="flex items-center gap-1.5"><span className="h-2 w-2 rounded-full" style={{ background: c.rose }} /><span className="text-[10px]" style={{ color: c.muted }}>Offline</span></div>
                                <div className="flex items-center gap-1.5"><span className="h-2 w-2 rounded-full" style={{ background: c.slate }} /><span className="text-[10px]" style={{ color: c.muted }}>Kapalı</span></div>
                                {/* Tip ayrımı: şekil + renk */}
                                <span className="text-[10px]" style={{ color: c.border }}>|</span>
                                <div className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-full border" style={{ borderColor: c.amber + "60", background: c.amber + "25" }} /><span className="text-[10px]" style={{ color: c.muted }}>Router</span></div>
                                <div className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-[3px] border" style={{ borderColor: c.sky + "60", background: c.sky + "25" }} /><span className="text-[10px]" style={{ color: c.muted }}>PC</span></div>
                                <div className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-md border" style={{ borderColor: c.violet + "60", background: c.violet + "25" }} /><span className="text-[10px]" style={{ color: c.muted }}>Geçici</span></div>
                                <div className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-[1px] border" style={{ borderColor: c.emerald + "60", background: c.emerald + "25" }} /><span className="text-[10px]" style={{ color: c.muted }}>POS</span></div>
                                <div className="flex rounded-lg border p-1" style={{ background: c.cardSoft, borderColor: c.border }}>
                                    <button
                                        type="button"
                                        onClick={() => setConnectionView("grid")}
                                        className="rounded-md px-2.5 py-1 text-[10px] font-bold transition-colors"
                                        style={{ background: connectionView === "grid" ? c.skySoft : "transparent", color: connectionView === "grid" ? c.sky : c.muted }}
                                    >
                                        Matris
                                    </button>
                                    <button
                                        type="button"
                                        onClick={() => setConnectionView("map")}
                                        className="rounded-md px-2.5 py-1 text-[10px] font-bold transition-colors"
                                        style={{ background: connectionView === "map" ? c.skySoft : "transparent", color: connectionView === "map" ? c.sky : c.muted }}
                                    >
                                        Harita
                                    </button>
                                </div>
                            </div>
                        </div>
                        {connectionView === "map" ? (
                            <TurkeyStoreMap c={c} stores={stores} />
                        ) : (
                            <>
                        {/* Router row */}
                        <div className="mb-3">
                            <div className="mb-2 flex items-center gap-2">
                                <Wifi className="h-3.5 w-3.5" style={{ color: c.amber }} />
                                <span className="text-xs font-semibold" style={{ color: c.text }}>Router</span>
                                <span className="text-[10px]" style={{ color: c.muted }}>
                                    ({routerOnline}/{activeRouters.length} online{closedRouters > 0 ? `, ${closedRouters} kapalı` : ""})
                                </span>
                            </div>
                            <div className="flex flex-wrap gap-1.5">
                                {routers.length > 0 ? [...routers].sort((a, b) => (a.storeCode ?? 0) - (b.storeCode ?? 0)).map((d) => (
                                    <ConnectionDot key={d.deviceId} c={c} device={d} />
                                )) : <span className="text-xs" style={{ color: c.muted }}>Router verisi yok</span>}
                            </div>
                        </div>
                        {/* PC row */}
                        <div className="mb-3">
                            <div className="mb-2 flex items-center gap-2">
                                <Laptop className="h-3.5 w-3.5" style={{ color: c.sky }} />
                                <span className="text-xs font-semibold" style={{ color: c.text }}>PC</span>
                                <span className="text-[10px]" style={{ color: c.muted }}>
                                    ({pcOnline}/{activePcs.length} online{closedPcs > 0 ? `, ${closedPcs} kapalı` : ""})
                                </span>
                            </div>
                            <div className="flex flex-wrap gap-1.5">
                                {pcs.length > 0 ? [...pcs].sort((a, b) => (a.storeCode ?? 0) - (b.storeCode ?? 0)).map((d) => (
                                    <ConnectionDot key={d.deviceId} c={c} device={d} />
                                )) : <span className="text-xs" style={{ color: c.muted }}>PC verisi yok</span>}
                            </div>
                        </div>
                        {/* POS row */}
                        <div className={geciciPcs.length > 0 ? "mb-3" : ""}>
                            <div className="mb-2 flex items-center gap-2">
                                <MonitorSmartphone className="h-3.5 w-3.5" style={{ color: c.emerald }} />
                                <span className="text-xs font-semibold" style={{ color: c.text }}>POS / Kasa</span>
                                <span className="text-[10px]" style={{ color: c.muted }}>({posOnline}/{activePos.length} online{closedPos > 0 ? `, ${closedPos} kapalı` : ""})</span>
                            </div>
                            <div className="flex flex-wrap gap-1.5">
                                {posDevices.length > 0 ? [...posDevices].sort((a, b) => (a.storeCode ?? 0) - (b.storeCode ?? 0)).map((d) => (
                                    <ConnectionDot key={d.deviceId} c={c} device={d} />
                                )) : <span className="text-xs" style={{ color: c.muted }}>POS verisi yok</span>}
                            </div>
                        </div>
                        {/* Geçici PC row */}
                        {geciciPcs.length > 0 && (
                        <div>
                            <div className="mb-2 flex items-center gap-2">
                                <Monitor className="h-3.5 w-3.5" style={{ color: c.violet }} />
                                <span className="text-xs font-semibold" style={{ color: c.text }}>Geçici PC</span>
                                <span className="text-[10px]" style={{ color: c.muted }}>
                                    ({geciciOnline}/{activeGecici.length} online{closedGecici > 0 ? `, ${closedGecici} kapalı` : ""})
                                </span>
                            </div>
                            <div className="flex flex-wrap gap-1.5">
                                {[...geciciPcs].sort((a, b) => (a.storeCode ?? 0) - (b.storeCode ?? 0)).map((d) => (
                                    <ConnectionDot key={d.deviceId} c={c} device={d} />
                                ))}
                            </div>
                        </div>
                        )}
                            </>
                        )}
                    </GlassCard>

                    <GlassCard c={c}>
                        <div className="mb-3 flex items-center justify-between">
                            <h3 className="text-sm font-semibold" style={{ color: c.text }}>Kapsama Oranları</h3>
                            <Activity className="h-4 w-4" style={{ color: c.emerald }} />
                        </div>
                        <div className="space-y-4">
                            <ProgressLine c={c} label="Agent" value={agentPct} detail={`${summary.online}/${summary.totalDevices}`} color={c.sky} />
                            <ProgressLine c={c} label="SQL" value={sqlPct} detail={`${sqlOnline}/${sqlTracked}`} color={c.emerald} />
                            <ProgressLine c={c} label="Mağaza" value={storePct} detail={`${liveStoreCount}/${activeStoreCount}`} color={c.amber} />
                            <ProgressLine c={c} label="Sağlık" value={healthPct} detail={`${summary.healthy}/${summary.totalDevices}`} color={c.violet} />
                        </div>
                    </GlassCard>

                </div>

                {/* ═══ RIGHT SIDEBAR ═══ */}
                <div className="space-y-5">
                    {/* Mobil hatta gectigi tahmin edilen router'lar */}
                    {mobileRouters.length > 0 && (
                        <GlassCard c={c}>
                            <div className="mb-3 flex items-center justify-between">
                                <div>
                                    <h3 className="text-sm font-semibold flex items-center gap-2" style={{ color: c.text }}>
                                        <Wifi className="h-4 w-4" style={{ color: c.amber }} />
                                        Mobil Hatta Geçen Router'lar
                                    </h3>
                                    <p className="text-[10px]" style={{ color: c.muted }}>
                                        {mobileRouters.length} mağaza 4.5G yedek hat şüphesi
                                    </p>
                                </div>
                                <Link to="/ag-teshis?tab=router-hat" className="text-[10px] px-2 py-1 rounded border" style={{ borderColor: c.border, color: c.muted }}>
                                    Detay
                                </Link>
                            </div>
                            <div className="space-y-1.5 max-h-[260px] overflow-auto scrollbar-thin scrollbar-thumb-slate-700 scrollbar-track-transparent">
                                {mobileRouters.slice(0, 12).map(r => {
                                    const severe = r.class === 'MobileLikely';
                                    const bg = severe ? c.roseGlow : c.track;
                                    const dot = severe ? c.rose : c.amber;
                                    return (
                                        <Link key={r.deviceId} to={`/ag-teshis?tab=router-hat&store=${r.storeCode}`}
                                            className="flex items-center gap-2 rounded-lg px-2.5 py-1.5 hover:opacity-90"
                                            style={{ background: bg }}>
                                            <span className="w-1.5 h-1.5 rounded-full shrink-0" style={{ background: dot }} />
                                            <span className="text-[11px] font-medium truncate flex-1" style={{ color: c.text }}>
                                                [{r.storeCode}] {r.storeName}
                                            </span>
                                            {r.avgRttMs != null && (
                                                <span className="text-[10px] font-mono shrink-0" style={{ color: c.muted }}>
                                                    {r.avgRttMs}ms
                                                </span>
                                            )}
                                            {r.switchoverDetected && (
                                                <span className="text-[9px] font-bold px-1.5 py-0.5 rounded shrink-0" style={{ background: c.amber + '22', color: c.amber }}>
                                                    SWITCH
                                                </span>
                                            )}
                                        </Link>
                                    );
                                })}
                            </div>
                        </GlassCard>
                    )}

                    {/* Offline cihazlar — üstte */}
                    <GlassCard c={c}>
                        <div className="mb-3 flex items-center justify-between">
                            <div>
                                <h3 className="text-sm font-semibold" style={{ color: c.text }}>Çevrimdışı Cihazlar</h3>
                                <p className="text-[10px]" style={{ color: c.muted }}>{offlineDevices.length} cihaz erişim dışı</p>
                            </div>
                            {offlineDevices.length > 0 && <span className="flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-bold text-white" style={{ background: c.rose }}>{offlineDevices.length}</span>}
                        </div>
                        {offlineDevices.length === 0 ? (
                            <div className="rounded-xl border border-dashed px-4 py-8 text-center" style={{ borderColor: c.emeraldBorder, background: c.emeraldGlow }}>
                                <ShieldCheck className="mx-auto h-6 w-6" style={{ color: c.emerald }} />
                                <p className="mt-2 text-xs font-medium" style={{ color: c.text }}>Tüm cihazlar online</p>
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
                            {(closedPos > 0 || closedPcs > 0 || closedRouters > 0 || closedGecici > 0) && (
                                <div className="flex-1 rounded-lg px-2.5 py-1.5 text-center" style={{ background: c.track }}>
                                    <div className="text-sm font-bold" style={{ color: c.slate }}>{closedPos + closedPcs + closedRouters + closedGecici}</div>
                                    <div className="text-[9px]" style={{ color: c.muted }}>Kapalı</div>
                                    <div className="flex justify-center gap-2 mt-0.5">
                                        {closedRouters > 0 && <span className="text-[8px]" style={{ color: c.muted }}>{closedRouters} Router</span>}
                                        {closedPcs > 0 && <span className="text-[8px]" style={{ color: c.muted }}>{closedPcs} PC</span>}
                                        {closedGecici > 0 && <span className="text-[8px]" style={{ color: c.muted }}>{closedGecici} Geçici</span>}
                                        {closedPos > 0 && <span className="text-[8px]" style={{ color: c.muted }}>{closedPos} Kasa</span>}
                                    </div>
                                </div>
                            )}
                        </div>
                    </GlassCard>

                    {/* Kesinti & Kapalı magazalar — altta */}
                    <GlassCard c={c}>
                        <div className="mb-4 flex items-center justify-between">
                            <h3 className="text-sm font-semibold" style={{ color: c.text }}>Kesinti & Kapalı Mağazalar</h3>
                            {attention.length > 0 && <span className="flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-bold text-white" style={{ background: issueCount > 0 ? c.rose : c.sky }}>{attention.length}</span>}
                        </div>
                        {attention.length === 0 ? (
                            <div className="rounded-2xl border border-dashed px-5 py-10 text-center" style={{ borderColor: c.emeraldBorder, background: c.emeraldGlow }}>
                                <ShieldCheck className="mx-auto h-8 w-8" style={{ color: c.emerald }} />
                                <p className="mt-3 text-sm font-medium" style={{ color: c.text }}>Açık kesinti yok</p>
                                <p className="mt-1 text-xs" style={{ color: c.muted }}>Tüm hatlar temiz görünüyor.</p>
                            </div>
                        ) : (
                            <div className="space-y-2">
                                {attention.map((store, i) => (
                                    <StoreCard key={store.code} c={c} store={store} rank={i + 1} />
                                ))}
                            </div>
                        )}
                        <AgendaTopicsPanel c={c} />
                    </GlassCard>
                </div>
            </div>

        </div>
    );
};

/* ═══════════════════════════════════════════
   SUB-COMPONENTS
   ═══════════════════════════════════════════ */

const SummaryPill: React.FC<{ c: Palette; label: string; value: string | number; color: string }> = ({ c, label, value, color }) => (
    <div className="flex items-center gap-2 rounded-lg border px-3 py-2" style={{ background: c.cardSoft, borderColor: c.border }}>
        <span className="h-1.5 w-1.5 rounded-full" style={{ background: color }} />
        <span className="text-[11px] font-semibold" style={{ color: c.muted }}>{label}</span>
        <span className="text-xs font-bold tabular-nums" style={{ color }}>{value}</span>
    </div>
);

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
    const isRouter = type === "ROUTER";
    const isGecici = type === "GECICI";
    const label = isRouter ? "RTR" : isPC ? "PC" : isGecici ? "GEÇ" : type.replace("KASA-", "K");
    const isPingSqlDown = !isRouter && d.pingReachable === true && d.sqlReachable === false;
    const badgeBg = isPingSqlDown ? c.amberSoft : isRouter ? c.amberSoft : isPC ? c.skySoft : isGecici ? c.violetSoft : c.emeraldSoft;
    const badgeColor = isPingSqlDown ? c.amber : isRouter ? c.amber : isPC ? c.sky : isGecici ? c.violet : c.emerald;
    const mins = d.lastSeen ? Math.max(0, Math.floor((Date.now() - new Date(d.lastSeen).getTime()) / 60000)) : null;
    const dur = mins === null ? "?" : mins < 60 ? `${mins}dk` : mins < 1440 ? `${Math.floor(mins / 60)}sa ${mins % 60}dk` : `${Math.floor(mins / 1440)}g ${Math.floor((mins % 1440) / 60)}sa`;
    const urgent = mins !== null && mins >= 30;
    const dotColor = isPingSqlDown ? c.amber : c.rose;
    return (
        <div className="flex items-center gap-2 rounded-lg border px-2.5 py-2" style={{ background: c.cardSoft, borderColor: isPingSqlDown ? c.amberBorder : urgent ? c.roseBorder : c.border }}>
            <span className="h-2 w-2 shrink-0 rounded-full animate-pulse" style={{ background: dotColor }} />
            <div className="min-w-0 flex-1">
                <div className="flex items-center gap-1.5">
                    <span className="rounded px-1 py-0.5 text-[8px] font-bold" style={{ background: badgeBg, color: badgeColor }}>{label}</span>
                    <span className="text-[11px] font-semibold truncate" style={{ color: c.text }}>[{d.storeCode}] {d.storeName}</span>
                </div>
                <div className="flex items-center gap-2">
                    <span className="text-[10px] font-mono" style={{ color: c.subtle }}>{d.calculatedIpAddress}</span>
                    {!isRouter && d.pingReachable != null && (
                        <span className="text-[8px] font-bold" style={{ color: isPingSqlDown ? c.amber : c.rose }}>
                            {isPingSqlDown ? "SQL KAPALI" : "ERISIM YOK"}
                        </span>
                    )}
                </div>
            </div>
            <div className="shrink-0 text-right">
                <div className="text-xs font-bold" style={{ color: isPingSqlDown ? c.amber : urgent ? c.rose : c.amber }}>{dur}</div>
            </div>
        </div>
    );
};

const ConnectionDot: React.FC<{ c: Palette; device: SqlDeviceWithStatus }> = ({ c, device }) => {
    const isClosed = device.isTemporarilyClosed;
    const isOnline = device.isOnline;
    const type = (device.deviceType || "").toUpperCase();
    const isRouter = type === "ROUTER";
    const isPC = type === "PC";
    const isGecici = type === "GECICI";
    // Ping OK ama SQL kapali → cihaz acik, SQL servis sorunu (sari)
    const isPingOnlySqlDown = !isRouter && !isClosed && !isOnline
        && device.pingReachable === true && device.sqlReachable === false;
    // Router: amber accent, PC: sky accent, Gecici: violet accent, POS: emerald accent
    const typeAccent = isRouter ? c.amber : isPC ? c.sky : isGecici ? c.violet : c.emerald;
    const color = isClosed ? c.slate : isOnline ? typeAccent : isPingOnlySqlDown ? c.amber : c.rose;
    const typeName = isRouter ? "Router" : isPC ? "PC" : isGecici ? "Geçici PC" : device.deviceName || device.deviceType;
    const statusText = isClosed ? " (kapalı)" : isOnline ? " — online"
        : isPingOnlySqlDown ? " — SQL KAPALI (cihaz acik)" : " — OFFLINE";
    const pingStr = device.pingReachable === true ? "OK" : device.pingReachable === false ? "FAIL" : "—";
    const sqlStr = device.sqlReachable === true ? "OK" : device.sqlReachable === false ? "FAIL" : "—";
    const geciciLabel = isGecici ? `${device.deviceName || device.deviceId}` : "";
    const label = `${device.storeCode} / ${typeName}${geciciLabel ? ` (${geciciLabel})` : ""}${statusText}${device.lastSeen ? `\nSon: ${fmtDuration(device.lastSeen)}` : ""}\n${device.calculatedIpAddress}${!isRouter ? `\nPing: ${pingStr} | SQL: ${sqlStr}` : ""}`;
    // Router: circle, PC: rounded square, Gecici: rounded-md (hexagon-ish), POS: square
    const shapeClass = isRouter
        ? "rounded-full"
        : isPC
        ? "rounded-lg"
        : isGecici
        ? "rounded-md"
        : "rounded-sm";
    return (
        <div
            className={`group relative flex h-7 w-7 cursor-default items-center justify-center ${isGecici ? "border-2 border-dashed" : "border"} text-[8px] font-bold transition-all duration-200 hover:scale-125 hover:z-10 ${shapeClass}`}
            style={{ background: color + "18", borderColor: color + "30", color, "--breathe-color": color + "40" } as React.CSSProperties}
            title={label}
        >
            {isGecici
                ? (device.calculatedIpAddress || "").split(".").pop()
                : device.storeCode}
            {!isClosed && !isOnline && (
                <span className="absolute -right-0.5 -top-0.5 h-2 w-2 rounded-full" style={{ background: isPingOnlySqlDown ? c.amber : c.rose, animation: "ms-pulse-ring 2.5s ease-out infinite" }} />
            )}
            {isOnline && (
                <span className={`absolute inset-0 ${shapeClass} opacity-0 transition-opacity duration-200 group-hover:opacity-100`} style={{ boxShadow: `0 0 12px ${color}50` }} />
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

const StoreCard: React.FC<{ c: Palette; store: StoreSummary; rank: number }> = ({ c, store, rank }) => {
    const color = storeToneColor(store.status, c);
    const statusLabel = store.status === "critical" ? "Kritik" : store.status === "watch" ? "Izleme" : store.status === "closed" ? "Kapalı" : "Stabil";
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
                        {store.status === "closed" ? (store.reason || "Planlı kapalı") : `${store.onlinePos}/${store.activePos} POS`}
                    </span>
                    {store.since && <span className="text-[10px] font-semibold" style={{ color }}>{fmtDuration(store.since)}</span>}
                </div>
            </div>
        </div>
    );
};

export default DashboardPage;
