import React, { useEffect, useMemo, useState } from "react";
import { apiClient, VncSessionLog } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import { History, RefreshCw, Search, User, Monitor, Clock } from "lucide-react";

const formatDateTime = (iso: string | null): string => {
    if (!iso) return "—";
    return new Date(iso).toLocaleString("tr-TR", {
        day: "2-digit", month: "2-digit", year: "numeric",
        hour: "2-digit", minute: "2-digit",
    });
};

const formatDuration = (seconds: number | null): string => {
    if (seconds === null || seconds === undefined) return "—";
    if (seconds < 60) return `${seconds} sn`;
    const m = Math.floor(seconds / 60);
    if (m < 60) return `${m} dk`;
    const h = Math.floor(m / 60);
    return `${h}sa ${m % 60}dk`;
};

const SessionHistoryPage: React.FC = () => {
    const [logs, setLogs] = useState<VncSessionLog[]>([]);
    const [search, setSearch] = useState("");
    const [isLoading, setIsLoading] = useState(true);
    const [lastUpdated, setLastUpdated] = useState<Date>(new Date());

    const load = async () => {
        setIsLoading(true);
        try {
            const data = await apiClient.getVncSessionLogs(undefined, 200);
            setLogs(data ?? []);
            setLastUpdated(new Date());
        } catch (err) { console.error("Session history load failed:", err); }
        finally { setIsLoading(false); }
    };

    useEffect(() => { load(); }, []);

    const filtered = useMemo(() => {
        const q = search.trim().toLowerCase();
        if (!q) return logs;
        return logs.filter(l =>
            (l.username ?? "").toLowerCase().includes(q) ||
            l.deviceId.toLowerCase().includes(q) ||
            l.targetIp.includes(q)
        );
    }, [logs, search]);

    if (isLoading) return <div className="flex h-[80vh] items-center justify-center"><Spinner size="lg" /></div>;

    return (
        <div className="p-6 max-w-[1920px] mx-auto w-full flex flex-col gap-5">
            <div className="flex items-center justify-between">
                <h1 className="text-2xl font-bold text-white flex items-center gap-3">
                    <History className="w-7 h-7 text-sky-500" />
                    Oturum Geçmişi
                    <span className="text-sm font-mono text-slate-400 bg-slate-800/60 px-2 py-0.5 rounded-md border border-slate-700/50">
                        {logs.length}
                    </span>
                </h1>
                <div className="flex items-center gap-3 text-xs text-slate-400 font-mono bg-slate-900/40 px-4 py-2 rounded-xl border border-slate-700/50">
                    Son: {lastUpdated.toLocaleTimeString("tr-TR")}
                    <button onClick={load} className="p-1.5 bg-slate-800 hover:bg-slate-700 rounded-lg border border-slate-700/50" title="Yenile">
                        <RefreshCw className="w-3.5 h-3.5 text-sky-500" />
                    </button>
                </div>
            </div>

            <div className="relative max-w-md">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                <input
                    value={search}
                    onChange={e => setSearch(e.target.value)}
                    placeholder="Kullanıcı, cihaz ID veya IP ara..."
                    className="w-full pl-9 pr-3 py-2 bg-slate-900/50 border border-slate-700/60 rounded-xl text-sm text-slate-200 placeholder-slate-500 focus:outline-none focus:border-sky-500"
                />
            </div>

            {filtered.length === 0 ? (
                <div className="flex flex-col items-center justify-center py-16 text-slate-500">
                    <History className="w-12 h-12 mb-3 opacity-30" />
                    <p>Geçmiş kaydı bulunamadı.</p>
                </div>
            ) : (
                <div className="overflow-hidden rounded-xl border border-slate-700/60 bg-slate-900/40">
                    <table className="w-full text-sm">
                        <thead className="bg-slate-800/60 text-xs uppercase text-slate-400">
                            <tr>
                                <th className="px-4 py-3 text-left">Kullanıcı</th>
                                <th className="px-4 py-3 text-left">Cihaz</th>
                                <th className="px-4 py-3 text-left">Hedef IP</th>
                                <th className="px-4 py-3 text-left">Başlangıç</th>
                                <th className="px-4 py-3 text-left">Bitiş</th>
                                <th className="px-4 py-3 text-left">Süre</th>
                                <th className="px-4 py-3 text-left">Sebep</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-800">
                            {filtered.map(l => (
                                <tr key={l.id} className="text-slate-200 hover:bg-slate-800/40">
                                    <td className="px-4 py-3">
                                        <div className="flex items-center gap-2">
                                            <User className="w-4 h-4 text-sky-400" />
                                            <span className="font-semibold">{l.username || "—"}</span>
                                        </div>
                                    </td>
                                    <td className="px-4 py-3 font-mono text-xs text-slate-400">
                                        <div className="flex items-center gap-2">
                                            <Monitor className="w-3.5 h-3.5" />
                                            <span className="truncate max-w-[240px]">{l.deviceId}</span>
                                        </div>
                                    </td>
                                    <td className="px-4 py-3 font-mono text-xs">{l.targetIp}</td>
                                    <td className="px-4 py-3 text-xs">{formatDateTime(l.startedAtUtc)}</td>
                                    <td className="px-4 py-3 text-xs">{l.endedAtUtc ? formatDateTime(l.endedAtUtc) : <span className="text-emerald-400 font-semibold">Devam ediyor</span>}</td>
                                    <td className="px-4 py-3">
                                        <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-slate-300">
                                            <Clock className="w-3.5 h-3.5" />
                                            {formatDuration(l.durationSeconds)}
                                        </span>
                                    </td>
                                    <td className="px-4 py-3 text-xs text-slate-400">{l.disconnectReason || "—"}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </div>
    );
};

export default SessionHistoryPage;
