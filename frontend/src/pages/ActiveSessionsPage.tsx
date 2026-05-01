import React, { useEffect, useState } from "react";
import { ActiveVncSession, apiClient } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import { RefreshCw, Radio, Clock, User, Monitor, X } from "lucide-react";

const formatStarted = (iso: string) => new Date(iso).toLocaleString("tr-TR", {
    day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit",
});

const formatDuration = (mins: number): string => {
    if (mins < 1) return "<1 dk";
    if (mins < 60) return `${mins} dk`;
    const h = Math.floor(mins / 60);
    return `${h} sa ${mins % 60} dk`;
};

const ActiveSessionsPage: React.FC = () => {
    const [sessions, setSessions] = useState<ActiveVncSession[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [lastUpdated, setLastUpdated] = useState<Date>(new Date());
    const [terminating, setTerminating] = useState<string | null>(null);

    const load = async (silent = false) => {
        try {
            if (!silent) setIsLoading(true);
            const data = await apiClient.getActiveVncSessions();
            setSessions(data ?? []);
            setLastUpdated(new Date());
        } catch (err) { console.error("Active sessions load failed:", err); }
        finally { if (!silent) setIsLoading(false); }
    };

    useEffect(() => {
        load();
        const t = setInterval(() => load(true), 10_000);
        return () => clearInterval(t);
    }, []);

    const handleTerminate = async (sessionId: string) => {
        if (!confirm("Oturumu sonlandırmak istediğinizden emin misiniz?")) return;
        setTerminating(sessionId);
        try {
            await apiClient.terminateVncSession(sessionId);
            setSessions(prev => prev.filter(s => s.sessionId !== sessionId));
        } catch (err) {
            console.error("Terminate failed:", err);
            alert("Oturum sonlandırılamadı.");
        } finally { setTerminating(null); }
    };

    if (isLoading) return <div className="flex h-[80vh] items-center justify-center"><Spinner size="lg" /></div>;

    return (
        <div className="p-6 max-w-[1920px] mx-auto w-full flex flex-col gap-5">
            <div className="flex items-center justify-between">
                <h1 className="text-2xl font-bold text-white flex items-center gap-3">
                    <Radio className="w-7 h-7 text-emerald-500" />
                    Aktif Oturumlar
                    <span className="text-sm font-mono text-emerald-400 bg-emerald-500/10 px-2 py-0.5 rounded-md border border-emerald-500/30">
                        {sessions.length}
                    </span>
                </h1>
                <div className="flex items-center gap-3 text-xs text-slate-400 font-mono bg-slate-900/40 px-4 py-2 rounded-xl border border-slate-700/50">
                    Son: {lastUpdated.toLocaleTimeString("tr-TR")}
                    <button onClick={() => load()} className="p-1.5 bg-slate-800 hover:bg-slate-700 rounded-lg border border-slate-700/50" title="Yenile">
                        <RefreshCw className="w-3.5 h-3.5 text-sky-500" />
                    </button>
                </div>
            </div>

            {sessions.length === 0 ? (
                <div className="flex flex-col items-center justify-center py-16 text-slate-500">
                    <Radio className="w-12 h-12 mb-3 opacity-30" />
                    <p>Şu anda aktif oturum yok.</p>
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
                                <th className="px-4 py-3 text-left">Süre</th>
                                <th className="px-4 py-3 text-right">İşlem</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-800">
                            {sessions.map(s => (
                                <tr key={s.sessionId} className="text-slate-200 hover:bg-slate-800/40">
                                    <td className="px-4 py-3">
                                        <div className="flex items-center gap-2">
                                            <User className="w-4 h-4 text-sky-400" />
                                            <span className="font-semibold">{s.username || "—"}</span>
                                        </div>
                                    </td>
                                    <td className="px-4 py-3 font-mono text-xs text-slate-400">
                                        <div className="flex items-center gap-2">
                                            <Monitor className="w-3.5 h-3.5" />
                                            <span className="truncate max-w-[260px]">{s.deviceId}</span>
                                        </div>
                                    </td>
                                    <td className="px-4 py-3 font-mono text-xs">{s.targetIp}</td>
                                    <td className="px-4 py-3 text-xs">{formatStarted(s.startedAt)}</td>
                                    <td className="px-4 py-3">
                                        <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-emerald-400">
                                            <Clock className="w-3.5 h-3.5" />
                                            {formatDuration(s.durationMinutes)}
                                        </span>
                                    </td>
                                    <td className="px-4 py-3 text-right">
                                        <button
                                            onClick={() => handleTerminate(s.sessionId)}
                                            disabled={terminating === s.sessionId}
                                            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-red-500/10 text-red-400 hover:bg-red-500/20 border border-red-500/30 disabled:opacity-50"
                                        >
                                            <X className="w-3.5 h-3.5" />
                                            Sonlandır
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </div>
    );
};

export default ActiveSessionsPage;
