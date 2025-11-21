import React, { useState } from 'react';
import { apiClient } from '../../lib/apiClient';
// HATA DÜZELTİLDİ: Named import yerine default import kullanıldı
import Spinner from '../ui/Spinner'; 

interface RunScriptPanelProps {
    deviceId: string;
}

const RunScriptPanel: React.FC<RunScriptPanelProps> = ({ deviceId }) => {
    const [script, setScript] = useState('Get-Process | Select-Object -First 5');
    const [output, setOutput] = useState('');
    const [running, setRunning] = useState(false);
    const [commandId, setCommandId] = useState<string | null>(null);

    const runScript = async () => {
        if (!script.trim()) {
            alert("Lütfen çalıştırılacak bir betik girin.");
            return;
        }

        setRunning(true);
        setOutput('⌛ Komut kuyruğa alınıyor...');

        try {
            const res = await apiClient.runScript(deviceId, script);
            
            setCommandId(res.commandId);
            setOutput(`✅ Komut kuyruğa alındı (ID: ${res.commandId}). Agent'ın sonucu göndermesi bekleniyor...`);
            
            // NOTE: Gerçek projede, sonucun CommandResult API'si üzerinden 
            // kaydedildiği ActionsHistoryPage'i kontrol etmelisiniz.
            
        } catch (error) {
            console.error(error);
            setOutput(`❌ Hata: Komut gönderilemedi. ${error instanceof Error ? error.message : String(error)}`);
        } finally {
            setRunning(false);
        }
    };

    return (
        <div className="space-y-4">
            <h3 className="text-lg font-semibold text-ms-text">Run Remote Script (PowerShell / Bash)</h3>
            
            {/* Betik Girişi */}
            <textarea
                value={script}
                onChange={(e) => setScript(e.target.value)}
                rows={8}
                className="w-full p-3 text-sm font-mono bg-ms-bg-soft border border-ms-border rounded-lg focus:ring-ms-primary focus:border-ms-primary text-ms-text"
                placeholder="Örn: Get-Service | Select-Object -First 5"
                disabled={running}
            />

            {/* Çalıştırma Butonu */}
            <button
                onClick={runScript}
                disabled={running}
                className="px-4 py-2 text-sm font-medium rounded-lg text-white bg-ms-primary hover:bg-ms-primary-dark disabled:opacity-50 flex items-center"
            >
                {running ? <Spinner className="w-4 h-4 mr-2" /> : '🚀'}
                {running ? 'Çalıştırılıyor...' : 'Betik Çalıştır'}
            </button>

            {/* Çıktı Alanı */}
            <div className="p-3 text-sm font-mono bg-ms-panel border border-ms-border rounded-lg whitespace-pre-wrap">
                <p className="text-ms-text-muted mb-1">Çıktı:</p>
                {output || "Çalıştırma sonucu buraya gelecek."}
            </div>
            
            {commandId && (
                <p className="text-xs text-ms-text-muted">
                    Sonucu izlemek için Komut Geçmişi'ni kontrol edin (ID: {commandId})
                </p>
            )}
        </div>
    );
};

export default RunScriptPanel;