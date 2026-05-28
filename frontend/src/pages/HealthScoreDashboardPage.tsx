import React, { useEffect, useState, useCallback } from "react";
import { Link } from "react-router-dom";
import {
    Activity, AlertTriangle, CheckCircle, XCircle,
    TrendingUp, TrendingDown, Minus, RefreshCw, Shield,
    Server, Building2, BarChart3,
} from "lucide-react";
import { apiClient } from "../lib/apiClient";

// ── Types ─────────────────────────────────────────────────────────────────

interface HealthDeduction {
    category: string;
    reason: string;
    points: number;
}

interface DeviceHealthBreakdown {
    deviceId: string;
    hostname: string;
    ipAddress: string;
    storeCode: number;
    storeName?: string;
    score: number;
    status: string;
    online: boolean;
    lastSeen?: string;
    cpuPercent: number;
    ramPercent: number;
    diskPercent: number;
    deductions: HealthDeduction[];
    previousScore: number;
    trendDirection: string;
}

interface StoreHealthAverage {
    storeCode: number;
    storeName?: string;
    averageScore: number;
    deviceCount: number;
    criticalCount: number;
    status: string;
}

interface HealthScoreSummary {
    totalDevices: number;
    healthy: number;
    warning: number;
    risky: number;
    critical: number;
    offline: number;
    averageScore: number;
    bottom10: DeviceHealthBreakdown[];
    storeAverages: StoreHealthAverage[];
}

// ── Score Visual Helpers ──────────────────────────────────────────────────

function scoreColor(score: number): string {
    if (score >= 80) return "text-emerald-400";
    if (score >= 60) return "text-amber-400";
    if (score >= 40) return "text-orange-400";
    return "text-red-400";
}

function scoreBg(score: number): string {
    if (score >= 80) return "bg-emerald-500/10 border-emerald-500/30";
    if (score >= 60) return "bg-amber-500/10 border-amber-500/30";
    if (score >= 40) return "bg-orange-500/10 border-orange-500/30";
    return "bg-red-500/10 border-red-500/30";
}

function ScoreRing({ score, size = 56 }: { score: number; size?: number }) {
    const radius = size / 2 - 6;
    const circumference = 2 * Math.PI * radius;
    const offset = circumference * (1 - score / 100);
    const color = score >= 80 ? "#34d399" : score >= 60 ? "#fbbf24" : score >= 40 ? "#fb923c" : "#f87171";
    return (
        <svg width={size} height={size} className="shrink-0 -rotate-90">
            <circle cx={size / 2} cy={size / 2} r={radius} fill="none" stroke="#1f2937" strokeWidth={5} />
            <circle cx={size / 2} cy={size / 2} r={radius} fill="none" stroke={color} strokeWidth={5}
                strokeDasharray={circumference} strokeDashoffset={offset}
                strokeLinecap="round" style={{ transition: "stroke-dashoffset 0.5s ease" }} />
            <text x="50%" y="50%" dominantBaseline="middle" textAnchor="middle"
                fill={color} fontSize={size / 4} fontWeight="bold"
                style={{ transform: "rotate(90deg)", transformOrigin: "center" }}>
                {score}
            </text>
        </svg>
    );
}

function TrendIcon({ direction }: { direction: string }) {
    if (direction === "Up") return <TrendingUp className="w-3.5 h-3.5 text-emerald-400" />;
    if (direction === "Down") return <TrendingDown className="w-3.5 h-3.5 text-red-400" />;
    return <Minus className="w-3.5 h-3.5 text-zinc-500" />;
}

function StatusBadge({ status }: { status: string }) {
    const styles: Record<string, string> = {
        Healthy: "bg-emerald-500/20 text-emerald-400 border-emerald-500/30",
        Warning: "bg-amber-500/20 text-amber-400 border-amber-500/30",
        Risky: "bg-orange-500/20 text-orange-400 border-orange-500/30",
        Critical: "bg-red-500/20 text-red-400 border-red-500/30",
        Offline: "bg-zinc-700/50 text-zinc-400 border-zinc-600/30",
    };
    return (
        <span className={`px-2 py-0.5 text-xs font-medium rounded-full border ${styles[status] ?? styles.Offline}`}>
            {status}
        </span>
    );
}

function timeAgo(iso?: string): string {
    if (!iso) return "—";
    const diff = Date.now() - new Date(iso).getTime();
    const m = Math.floor(diff / 60000);
    if (m < 1) return "az önce";
    if (m < 60) return `${m} dk`;
    const h = Math.floor(m / 60);
    if (h < 24) return `${h} saat`;
    return `${Math.floor(h / 24)} gün`;
}

// ── Main Page ─────────────────────────────────────────────────────────────

export default function HealthScoreDashboardPage() {
    const [data, setData] = useState<HealthScoreSummary | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [tab, setTab] = useState<"overview" | "stores" | "bottom10">("overview");
    const [expandedDevice, setExpandedDevice] = useState<string | null>(null);

    const load = useCallback(async () => {
        setLoading(true);
        setError(null);
        try {
            const res = await apiClient.get<HealthScoreSummary>("/api/health-score/summary");
            setData(res);
        } catch (e: unknown) {
            setError(e instanceof Error ? e.message : "Yüklenemedi");
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => { load(); }, [load]);
    // Auto-refresh every 2 minutes
    useEffect(() => {
        const t = setInterval(load, 120_000);
        return () => clearInterval(t);
    }, [load]);

    if (error) return (
        <div className="p-6 flex items-center justify-center">
            <div className="bg-red-500/10 border border-red-500/30 rounded-xl p-6 text-center max-w-sm">
                <XCircle className="w-8 h-8 text-red-400 mx-auto mb-2" />
                <p className="text-red-300 text-sm">{error}</p>
                <button onClick={load} className="mt-3 px-4 py-1.5 bg-red-600 hover:bg-red-500 text-white text-sm rounded-lg">
                    Tekrar Dene
                </button>
            </div>
        </div>
    );

    return (
        <div className="p-6 space-y-6">

            {/* Header */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                    <Activity className="w-6 h-6 text-violet-400" />
                    <div>
                        <h1 className="text-xl font-bold text-ms-text">Agent Health Score</h1>
                        <p className="text-xs text-zinc-500">Tüm cihazların anlık sağlık puanları</p>
                    </div>
                </div>
                <button onClick={load} disabled={loading}
                    className="flex items-center gap-2 px-3 py-1.5 bg-violet-600/20 text-violet-400 hover:bg-violet-600/30 rounded-lg text-sm transition-colors">
                    <RefreshCw className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
                    {loading ? "Hesaplanıyor..." : "Yenile"}
                </button>
            </div>

            {loading && !data ? (
                <div className="flex items-center justify-center py-20">
                    <div className="text-center space-y-3">
                        <RefreshCw className="w-8 h-8 animate-spin text-violet-400 mx-auto" />
                        <p className="text-zinc-500 text-sm">Sağlık skorları hesaplanıyor...</p>
                    </div>
                </div>
            ) : data ? (
                <>
                    {/* Stat Cards */}
                    <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-3">
                        <StatCard label="Ortalama Skor" value={`${data.averageScore}`}
                            icon={<BarChart3 className="w-4 h-4" />}
                            color={scoreColor(data.averageScore)} />
                        <StatCard label="Sağlıklı" value={data.healthy.toString()}
                            icon={<CheckCircle className="w-4 h-4" />} color="text-emerald-400" />
                        <StatCard label="Uyarı" value={data.warning.toString()}
                            icon={<AlertTriangle className="w-4 h-4" />} color="text-amber-400" />
                        <StatCard label="Riskli" value={data.risky.toString()}
                            icon={<AlertTriangle className="w-4 h-4" />} color="text-orange-400" />
                        <StatCard label="Kritik" value={data.critical.toString()}
                            icon={<XCircle className="w-4 h-4" />} color="text-red-400" />
                        <StatCard label="Çevrimdışı" value={data.offline.toString()}
                            icon={<Server className="w-4 h-4" />} color="text-zinc-400" />
                    </div>

                    {/* Tabs */}
                    <div className="flex gap-1 bg-zinc-900 rounded-lg p-1 w-fit">
                        {(["overview", "stores", "bottom10"] as const).map(t => (
                            <button key={t} onClick={() => setTab(t)}
                                className={`px-4 py-1.5 rounded-md text-sm font-medium transition-colors ${tab === t ? "bg-violet-600 text-white" : "text-zinc-400 hover:text-ms-text"}`}>
                                {t === "overview" ? "Genel" : t === "stores" ? "Mağazalar" : "En Kötü 10"}
                            </button>
                        ))}
                    </div>

                    {/* Tab Content */}
                    {tab === "overview" && (
                        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-3">
                            {/* Critical */}
                            {data.bottom10.filter(d => d.status === "Critical").map(d => (
                                <DeviceCard key={d.deviceId} device={d}
                                    expanded={expandedDevice === d.deviceId}
                                    onToggle={() => setExpandedDevice(prev => prev === d.deviceId ? null : d.deviceId)} />
                            ))}
                        </div>
                    )}

                    {tab === "bottom10" && (
                        <div className="space-y-2">
                            {data.bottom10.map((d, i) => (
                                <DeviceRow key={d.deviceId} device={d} rank={i + 1}
                                    expanded={expandedDevice === d.deviceId}
                                    onToggle={() => setExpandedDevice(prev => prev === d.deviceId ? null : d.deviceId)} />
                            ))}
                            {data.bottom10.length === 0 && (
                                <p className="text-center text-zinc-500 py-10">Tüm cihazlar sağlıklı!</p>
                            )}
                        </div>
                    )}

                    {tab === "stores" && (
                        <div className="space-y-2">
                            {data.storeAverages.map(s => (
                                <StoreRow key={s.storeCode} store={s} />
                            ))}
                        </div>
                    )}
                </>
            ) : null}
        </div>
    );
}

// ── Sub-components ────────────────────────────────────────────────────────

function StatCard({ label, value, icon, color }: { label: string; value: string; icon: React.ReactNode; color: string }) {
    return (
        <div className="bg-zinc-900/60 border border-zinc-800 rounded-xl p-4">
            <div className={`flex items-center gap-2 mb-2 ${color}`}>{icon}<span className="text-xs">{label}</span></div>
            <p className={`text-2xl font-bold ${color}`}>{value}</p>
        </div>
    );
}

function DeviceCard({ device, expanded, onToggle }: { device: DeviceHealthBreakdown; expanded: boolean; onToggle: () => void }) {
    return (
        <div className={`rounded-xl border p-4 cursor-pointer transition-all ${scoreBg(device.score)}`} onClick={onToggle}>
            <div className="flex items-center gap-3">
                <ScoreRing score={device.score} size={52} />
                <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                        <span className="font-semibold text-sm text-ms-text truncate">{device.hostname}</span>
                        <TrendIcon direction={device.trendDirection} />
                    </div>
                    <p className="text-xs text-zinc-500">{device.ipAddress} · {device.storeName ?? `Mağaza ${device.storeCode}`}</p>
                    <div className="flex items-center gap-2 mt-1">
                        <StatusBadge status={device.online ? device.status : "Offline"} />
                        <span className="text-xs text-zinc-600">{timeAgo(device.lastSeen)}</span>
                    </div>
                </div>
            </div>
            {expanded && (
                <div className="mt-3 pt-3 border-t border-white/10 space-y-1">
                    {device.deductions.map((d, i) => (
                        <div key={i} className="flex items-center justify-between text-xs">
                            <span className="text-zinc-400">{d.category}: {d.reason}</span>
                            <span className="text-red-400 font-mono">-{d.points}</span>
                        </div>
                    ))}
                    <Link to={`/devices/${device.deviceId}/health`}
                        className="block mt-2 text-xs text-violet-400 hover:text-violet-300"
                        onClick={e => e.stopPropagation()}>
                        Detaylı sağlık görünümü →
                    </Link>
                </div>
            )}
        </div>
    );
}

function DeviceRow({ device, rank, expanded, onToggle }: { device: DeviceHealthBreakdown; rank: number; expanded: boolean; onToggle: () => void }) {
    return (
        <div className={`rounded-xl border ${scoreBg(device.score)} transition-all`}>
            <div className="flex items-center gap-3 p-3 cursor-pointer" onClick={onToggle}>
                <span className="text-zinc-600 font-mono text-sm w-5">{rank}</span>
                <ScoreRing score={device.score} size={44} />
                <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                        <span className="font-medium text-sm text-ms-text truncate">{device.hostname}</span>
                        <TrendIcon direction={device.trendDirection} />
                        <StatusBadge status={device.online ? device.status : "Offline"} />
                    </div>
                    <p className="text-xs text-zinc-500">{device.storeName ?? `Mağaza ${device.storeCode}`} · {device.ipAddress}</p>
                </div>
                <div className="hidden sm:flex items-center gap-4 text-xs text-zinc-500 shrink-0">
                    <span>CPU {device.cpuPercent.toFixed(0)}%</span>
                    <span>RAM {device.ramPercent.toFixed(0)}%</span>
                    <span>Disk {device.diskPercent.toFixed(0)}%</span>
                    <span>{timeAgo(device.lastSeen)}</span>
                </div>
            </div>
            {expanded && (
                <div className="px-4 pb-3 pt-2 border-t border-white/5 space-y-1">
                    {device.deductions.map((d, i) => (
                        <div key={i} className="flex items-center justify-between text-xs">
                            <span className="text-zinc-400">{d.category}: {d.reason}</span>
                            <span className="text-red-400 font-mono">-{d.points}</span>
                        </div>
                    ))}
                    <Link to={`/devices/${device.deviceId}/health`}
                        className="block mt-2 text-xs text-violet-400 hover:text-violet-300">
                        Detaylı sağlık görünümü →
                    </Link>
                </div>
            )}
        </div>
    );
}

function StoreRow({ store }: { store: StoreHealthAverage }) {
    return (
        <div className={`flex items-center gap-4 rounded-xl border p-3 ${scoreBg(store.averageScore)}`}>
            <Building2 className={`w-4 h-4 shrink-0 ${scoreColor(store.averageScore)}`} />
            <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-ms-text">
                    {store.storeCode} — {store.storeName ?? `Mağaza ${store.storeCode}`}
                </p>
                <p className="text-xs text-zinc-500">{store.deviceCount} cihaz · {store.criticalCount} kritik</p>
            </div>
            <div className="flex items-center gap-3 shrink-0">
                <div className="w-32 bg-zinc-800 rounded-full h-2">
                    <div className={`h-2 rounded-full transition-all ${store.averageScore >= 80 ? "bg-emerald-500" : store.averageScore >= 60 ? "bg-amber-500" : store.averageScore >= 40 ? "bg-orange-500" : "bg-red-500"}`}
                        style={{ width: `${store.averageScore}%` }} />
                </div>
                <span className={`text-sm font-bold w-8 text-right ${scoreColor(store.averageScore)}`}>
                    {store.averageScore.toFixed(0)}
                </span>
                <StatusBadge status={store.status} />
            </div>
        </div>
    );
}
