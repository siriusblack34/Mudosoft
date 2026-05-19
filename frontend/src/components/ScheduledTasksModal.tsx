import { useState, useEffect } from "react";
import { apiClient } from "../lib/apiClient";
import Spinner from "./ui/Spinner";
import { Clock, Plus, Trash2, Calendar, Repeat, AlertTriangle } from "lucide-react";

interface ScheduledTask {
    id: number;
    taskType: string;
    frequency: string;
    targetTime?: string; // "14:30:00"
    targetDate?: string; // ISO String
    nextRunTime: string; // ISO String
    lastRunTime?: string;
    lastResult?: string;
    isActive: boolean;
}

interface ScheduledTasksModalProps {
    onClose: () => void;
}

export default function ScheduledTasksModal({ onClose }: ScheduledTasksModalProps) {
    const [tasks, setTasks] = useState<ScheduledTask[]>([]);
    const [loading, setLoading] = useState(false);
    const [adding, setAdding] = useState(false);

    // Form inputs
    const [frequency, setFrequency] = useState<"OneTime" | "Daily">("OneTime");
    const [targetDate, setTargetDate] = useState("");
    const [targetTime, setTargetTime] = useState("");

    const fetchTasks = async () => {
        setLoading(true);
        try {
            const data = await apiClient.get<ScheduledTask[]>("/api/scheduled-tasks");
            setTasks(data);
        } catch (error) {
            console.error(error);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchTasks();
    }, []);

    const handleAdd = async () => {
        if (frequency === "OneTime" && !targetDate) return alert("Tarih seçmelisiniz.");
        if (frequency === "Daily" && !targetTime) return alert("Saat seçmelisiniz.");

        setAdding(true);
        try {
            await apiClient.post("/api/scheduled-tasks", {
                type: "InboxCleanup",
                frequency,
                targetDate: frequency === "OneTime" ? targetDate : null,
                targetTime: frequency === "Daily" ? targetTime + ":00" : null
            });
            await fetchTasks();
            // Reset form
            setTargetDate("");
            setTargetTime("");
        } catch (error) {
            console.error(error);
            alert(error instanceof Error ? error.message : "Ekleme hatası!");
        } finally {
            setAdding(false);
        }
    };

    const handleDelete = async (id: number) => {
        if (!confirm("Görevi silmek istediğinize emin misiniz?")) return;
        try {
            await apiClient.delete(`/api/scheduled-tasks/${id}`);
            setTasks(prev => prev.filter(t => t.id !== id));
        } catch (error) {
            console.error(error);
            alert("Silme hatası!");
        }
    };

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm p-4">
            <div className="bg-slate-900 border border-slate-700 rounded-xl shadow-2xl w-full max-w-2xl flex flex-col max-h-[90vh]">

                {/* Header */}
                <div className="flex items-center justify-between p-4 border-b border-slate-800">
                    <h2 className="text-lg font-bold text-white flex items-center gap-2">
                        <Clock className="w-5 h-5 text-cyan-500" />
                        Zamanlanmış Temizlik Görevleri
                    </h2>
                    <button onClick={onClose} className="text-slate-400 hover:text-white transition-colors">
                        ✕
                    </button>
                </div>

                {/* Body */}
                <div className="p-4 flex-1 overflow-auto space-y-6">

                    {/* Add New Task Form */}
                    <div className="bg-slate-800/50 p-4 rounded-lg border border-slate-700/50">
                        <h3 className="text-sm font-semibold text-slate-300 mb-3 flex items-center gap-2">
                            <Plus className="w-4 h-4" /> Yeni Görev Ekle
                        </h3>
                        <div className="flex flex-col sm:flex-row gap-3">
                            <select
                                value={frequency}
                                onChange={(e) => setFrequency(e.target.value as any)}
                                className="bg-slate-900 border border-slate-600 rounded px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500"
                            >
                                <option value="OneTime">Tek Seferlik</option>
                                <option value="Daily">Her Gün (Tekrarlı)</option>
                            </select>

                            {frequency === "OneTime" ? (
                                <input
                                    type="datetime-local"
                                    value={targetDate}
                                    onChange={(e) => setTargetDate(e.target.value)}
                                    className="bg-slate-900 border border-slate-600 rounded px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500 flex-1"
                                />
                            ) : (
                                <input
                                    type="time"
                                    value={targetTime}
                                    onChange={(e) => setTargetTime(e.target.value)}
                                    className="bg-slate-900 border border-slate-600 rounded px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500 flex-1"
                                />
                            )}

                            <button
                                onClick={handleAdd}
                                disabled={adding}
                                className="bg-gradient-to-r from-cyan-600 to-blue-600 hover:from-cyan-500 hover:to-blue-500 text-white px-4 py-2 rounded text-sm font-medium transition-all shadow-lg shadow-cyan-500/20 disabled:opacity-50"
                            >
                                {adding ? <Spinner size="sm" /> : "Ekle"}
                            </button>
                        </div>
                        <div className="mt-2 text-xs text-slate-500">
                            {frequency === "OneTime"
                                ? "ℹ️ Belirtilen tarih ve saatte bir kez çalışır."
                                : "ℹ️ Her gün belirtilen saatte otomatik çalışır."}
                        </div>
                    </div>

                    {/* Task List */}
                    <div className="space-y-2">
                        <h3 className="text-sm font-semibold text-slate-300">Mevcut Görevler</h3>

                        {loading && tasks.length === 0 ? (
                            <div className="flex justify-center p-4"><Spinner /></div>
                        ) : tasks.length === 0 ? (
                            <div className="text-center p-8 text-slate-500 bg-slate-800/20 rounded-lg border border-slate-800 border-dashed">
                                Aktif görev bulunmuyor.
                            </div>
                        ) : (
                            <div className="grid gap-2">
                                {tasks.map(task => (
                                    <div key={task.id} className="flex items-center justify-between bg-slate-800 p-3 rounded-lg border border-slate-700">
                                        <div className="flex items-start gap-3">
                                            <div className={`mt-1 p-1.5 rounded-full ${task.frequency === 'Daily' ? 'bg-purple-500/20 text-purple-400' : 'bg-blue-500/20 text-blue-400'}`}>
                                                {task.frequency === 'Daily' ? <Repeat className="w-4 h-4" /> : <Calendar className="w-4 h-4" />}
                                            </div>
                                            <div>
                                                <div className="flex items-center gap-2 flex-wrap">
                                                    <span className="font-medium text-white text-sm">
                                                        {task.frequency === 'Daily' ? 'Her Gün' : 'Tek Seferlik'}
                                                    </span>
                                                    <span className={`text-[10px] px-1.5 py-0.5 rounded font-mono ${
                                                        task.taskType === 'StockCleanup'
                                                            ? 'bg-amber-500/15 text-amber-400 border border-amber-500/25'
                                                            : 'bg-cyan-500/15 text-cyan-400 border border-cyan-500/25'
                                                    }`}>
                                                        {task.taskType === 'StockCleanup' ? 'POS Stock Transfer' : 'Inbox Cleanup'}
                                                    </span>
                                                    {!task.isActive && (
                                                        <span className="text-[10px] bg-slate-700 text-slate-400 px-1.5 py-0.5 rounded">Pasif</span>
                                                    )}
                                                </div>
                                                <div className="text-xs text-slate-400 mt-0.5">
                                                    Sonraki Çalışma: <span className="text-cyan-400 font-mono">{new Date(task.nextRunTime).toLocaleString('tr-TR')}</span>
                                                </div>
                                                {task.lastResult && (
                                                    <div className={`text-[10px] mt-1 font-mono ${task.lastResult.includes('Success') ? 'text-emerald-500' : 'text-rose-500'}`}>
                                                        Son Sonuç: {task.lastResult}
                                                    </div>
                                                )}
                                            </div>
                                        </div>
                                        <button
                                            onClick={() => handleDelete(task.id)}
                                            className="p-2 text-slate-500 hover:text-rose-500 hover:bg-rose-500/10 rounded transition-colors"
                                            title="Görevi Sil"
                                        >
                                            <Trash2 className="w-4 h-4" />
                                        </button>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>

                </div>

                {/* Footer */}
                <div className="p-4 border-t border-slate-800 flex justify-end">
                    <button onClick={onClose} className="text-sm text-slate-400 hover:text-white px-4 py-2">
                        Kapat
                    </button>
                </div>
            </div>
        </div>
    );
}
