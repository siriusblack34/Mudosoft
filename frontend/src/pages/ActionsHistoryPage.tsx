import React, { useEffect, useState, useCallback } from 'react';
import { apiClient } from '../lib/apiClient';
import type { CommandHistoryItem } from '../lib/apiClient';
import StatusPill from '../components/common/StatusPill';
import Modal from '../components/ui/Modal';
import { formatDistanceToNow } from 'date-fns';
import { RefreshCw, CheckCircle, XCircle, History } from 'lucide-react';

const ActionsHistoryPage: React.FC = () => {
  const [history, setHistory] = useState<CommandHistoryItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedOutput, setSelectedOutput] = useState<string | null>(null);
  const [outputLoading, setOutputLoading] = useState(false);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [lastRefreshed, setLastRefreshed] = useState<Date | null>(null);

  const loadHistory = useCallback(async () => {
    try {
      const res = await apiClient.getCommandHistory();
      setHistory(res);
      setLastRefreshed(new Date());
    } catch (err) {
      console.error('Komut gecmisi yuklenirken hata:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadHistory();
  }, [loadHistory]);

  useEffect(() => {
    if (!autoRefresh) return;
    const interval = setInterval(loadHistory, 15000);
    return () => clearInterval(interval);
  }, [autoRefresh, loadHistory]);

  const viewDetails = async (commandId: string) => {
    setOutputLoading(true);
    setSelectedOutput(null);
    try {
      // Tam çıktıyı çekmek için API'yi kullan
      const res = await apiClient.getCommandDetails(commandId);
      setSelectedOutput(res.output || "Komut çıktısı boş.");
    } catch (error) {
      console.error('Komut detayları yüklenirken hata:', error);
      setSelectedOutput("Hata: Tam çıktı yüklenemedi.");
    } finally {
      setOutputLoading(false);
    }
  };

  const closeModal = () => {
    setSelectedOutput(null);
  };

  const successCount = history.filter(h => h.success).length;
  const failCount = history.filter(h => !h.success).length;

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <div className="w-8 h-8 border-2 border-violet-500 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-indigo-500 to-violet-600 flex items-center justify-center shadow-lg shadow-indigo-500/30">
            <History className="w-5 h-5 text-white" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-white tracking-tight">Command History</h1>
            <p className="text-[11px] text-slate-500 mt-0.5">
              {lastRefreshed && `Son guncelleme: ${lastRefreshed.toLocaleTimeString('tr-TR')}`}
            </p>
          </div>
        </div>

        <div className="flex items-center gap-3">
          {/* Summary pills */}
          <div className="flex items-center gap-2">
            <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-emerald-500/10 border border-emerald-500/20">
              <CheckCircle className="w-3.5 h-3.5 text-emerald-400" />
              <span className="text-xs font-bold text-emerald-400">{successCount}</span>
            </div>
            <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-rose-500/10 border border-rose-500/20">
              <XCircle className="w-3.5 h-3.5 text-rose-400" />
              <span className="text-xs font-bold text-rose-400">{failCount}</span>
            </div>
          </div>

          {/* Auto-refresh toggle */}
          <button
            onClick={() => setAutoRefresh(prev => !prev)}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all border ${
              autoRefresh
                ? 'bg-violet-500/10 border-violet-500/20 text-violet-400'
                : 'bg-white/5 border-white/10 text-slate-400'
            }`}
            title={autoRefresh ? 'Otomatik yenileme acik (15sn)' : 'Otomatik yenileme kapali'}
          >
            <RefreshCw className={`w-3.5 h-3.5 ${autoRefresh ? 'animate-spin' : ''}`} style={autoRefresh ? { animationDuration: '3s' } : undefined} />
            {autoRefresh ? 'Auto' : 'Paused'}
          </button>

          {/* Manual refresh */}
          <button
            onClick={loadHistory}
            className="p-2 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-all active:scale-95"
            title="Yenile"
          >
            <RefreshCw className="w-4 h-4 text-violet-400" />
          </button>
        </div>
      </div>

      <div className="overflow-hidden rounded-2xl border-white/5 glass-card shadow-xl">
        <table className="w-full text-sm">
          <thead className="bg-white/5 text-slate-400 uppercase tracking-widest text-xs font-semibold border-b border-white/5">
            <tr>
              <th className="text-left px-5 py-4 w-1/5">Hostname</th>
              <th className="text-left px-5 py-4 w-1/10">Type</th>
              <th className="text-left px-5 py-4 w-1/10">Status</th>
              <th className="text-left px-5 py-4 w-1/5">Completed</th>
              <th className="text-left px-5 py-4 w-2/5">Output Snippet</th>
              <th className="text-left px-5 py-4 w-1/12"></th>
            </tr>
          </thead>

          <tbody>
            {history.length > 0 ? (
              history.map((item) => (
                <tr key={item.commandId} className={`border-t border-slate-700/50 hover:bg-slate-800/30 transition-colors ${!item.success ? 'bg-rose-500/5 border-l-2 border-l-rose-500/50' : ''}`}>
                  <td className="px-5 py-3 font-medium text-white">{item.hostname}</td>
                  <td className="px-5 py-3 text-slate-400">{item.typeName}</td>
                  <td className="px-5 py-3">
                    {/* PROP DÜZELTME: 'type' yerine 'tone' kullanıldı */}
                    <StatusPill tone={item.success ? 'success' : 'danger'} text={item.success ? 'SUCCESS' : 'FAILED'} />
                  </td>
                  <td className="px-5 py-3 text-slate-400">
                    {formatDistanceToNow(new Date(item.completedAtUtc), { addSuffix: true })}
                  </td>
                  <td className="px-5 py-3 font-mono text-xs text-slate-500 truncate max-w-[200px]" title={item.outputSnippet}>
                    {item.outputSnippet}
                  </td>
                  <td className="px-5 py-3">
                    <button
                      onClick={() => viewDetails(item.commandId)}
                      className="text-xs text-indigo-400 hover:text-indigo-300 font-medium disabled:opacity-50 transition-colors"
                      disabled={outputLoading}
                    >
                      {outputLoading ? 'Loading...' : 'Details'}
                    </button>
                  </td>
                </tr>
              ))
            ) : (
              <tr>
                <td colSpan={6} className="px-5 py-12 text-center text-slate-500 text-sm">
                  No command history found 🎉
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Output Modal */}
      <Modal isOpen={selectedOutput !== null} onClose={closeModal} title="Command Output Details">
        <pre className="bg-gray-800 p-4 rounded-lg text-white whitespace-pre-wrap text-xs font-mono max-h-96 overflow-y-auto">
          {outputLoading ? 'Loading command output...' : selectedOutput}
        </pre>
        <button
          onClick={closeModal}
          className="mt-4 px-4 py-2 text-sm font-medium rounded-lg text-white bg-ms-primary hover:bg-ms-primary-dark"
        >
          Close
        </button>
      </Modal>
    </div>
  );
};

export default ActionsHistoryPage;