import React, { useEffect, useState } from 'react';
import { apiClient } from '../lib/apiClient';
import type { CommandHistoryItem } from '../lib/apiClient';
import StatusPill from '../components/common/StatusPill'; // Başarı/Hata durumunu gösteren küçük bileşen
import Modal from '../components/ui/Modal'; // Çıktı detayları için modal
import { formatDistanceToNow } from 'date-fns'; // ⚠️ Harici paket: npm install date-fns

const ActionsHistoryPage: React.FC = () => {
  const [history, setHistory] = useState<CommandHistoryItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedOutput, setSelectedOutput] = useState<string | null>(null);
  const [outputLoading, setOutputLoading] = useState(false);

  useEffect(() => {
    let cancelled = false;

    const loadHistory = async () => {
      try {
        const res = await apiClient.getCommandHistory();
        if (!cancelled) {
          setHistory(res);
        }
      } catch (err) {
        console.error('Komut geçmişi yüklenirken hata:', err);
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    loadHistory();
    return () => {
      cancelled = true;
    };
  }, []);

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

  if (loading) return <div>Loading command history...</div>;

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Command History</h1>

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
                <tr key={item.commandId} className="border-t border-slate-700/50 hover:bg-slate-800/30 transition-colors">
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