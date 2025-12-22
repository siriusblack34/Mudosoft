import React, { useState, useRef, useEffect } from 'react';
import { apiClient } from '../../lib/apiClient';
import { Terminal, Play, RotateCcw, AlertCircle, CheckCircle, Loader2 } from 'lucide-react';

interface RunScriptPanelProps {
    deviceId: string;
}

const RunScriptPanel: React.FC<RunScriptPanelProps> = ({ deviceId }) => {
    const [script, setScript] = useState('ipconfig /all');
    const [output, setOutput] = useState('');
    const [running, setRunning] = useState(false);
    const [polling, setPolling] = useState(false);
    const pollingRef = useRef<NodeJS.Timeout | null>(null);
    const commandIdRef = useRef<string | null>(null);

    // Cleanup polling on unmount
    useEffect(() => {
        return () => {
            if (pollingRef.current) {
                clearInterval(pollingRef.current);
            }
        };
    }, []);

    const startPolling = (commandId: string) => {
        setPolling(true);
        let attempts = 0;
        const maxAttempts = 30; // 30 * 2s = 60 seconds max

        pollingRef.current = setInterval(async () => {
            attempts++;

            try {
                const res = await apiClient.get<any>(
                    `/api/agent/command-results/latest?deviceId=${deviceId}`
                );

                // Check if this result matches our command
                const resultId = res.commandId || res.CommandId;
                if (res && resultId === commandId) {
                    // Got the result!
                    if (pollingRef.current) clearInterval(pollingRef.current);
                    setPolling(false);
                    setOutput(res.output || 'Command completed (no output)');
                    return;
                }
            } catch (err) {
                console.error('Polling error:', err);
            }

            // Timeout check
            if (attempts >= 200) { // Increased to 200 (60s total)
                if (pollingRef.current) clearInterval(pollingRef.current);
                setPolling(false);
                setOutput(`Timeout: Command ${commandId} did not complete within 60 seconds. The agent may be offline or busy.`);
            }
        }, 300); // Reduced from 2000ms
    };

    const runScript = async () => {
        if (!script.trim()) return;

        // Clear previous polling
        if (pollingRef.current) {
            clearInterval(pollingRef.current);
        }

        setRunning(true);
        setOutput('');
        setPolling(false);

        try {
            const res = await apiClient.runScript(deviceId, script);
            commandIdRef.current = res.commandId;
            setOutput(`Command queued (ID: ${res.commandId}). Waiting for agent response...`);
            setRunning(false);

            // Start polling for result
            startPolling(res.commandId);
        } catch (error) {
            console.error(error);
            setOutput(`Error: ${error instanceof Error ? error.message : String(error)}`);
            setRunning(false);
        }
    };

    const isError = output.startsWith('Error') || output.startsWith('Timeout');
    const isWaiting = polling || running;

    return (
        <div className="flex flex-col gap-6">
            {/* Editor Panel */}
            <div className="bg-slate-900/50 border border-slate-700 rounded-xl p-5 shadow-sm">
                <div className="flex justify-between items-center mb-4">
                    <h3 className="text-lg font-semibold text-white flex items-center gap-2">
                        <Terminal className="w-5 h-5 text-indigo-400" />
                        Run Remote Script
                    </h3>
                    <span className="text-xs text-slate-500 font-mono bg-slate-800 px-2 py-1 rounded">PowerShell / Bash</span>
                </div>

                <div className="relative">
                    <textarea
                        value={script}
                        onChange={(e) => setScript(e.target.value)}
                        rows={6}
                        className="w-full bg-slate-950 text-emerald-400 font-mono text-sm p-4 rounded-lg border border-slate-700 focus:outline-none focus:border-indigo-500 transition-colors resize-none mb-4"
                        placeholder="Type your script here..."
                        spellCheck={false}
                        disabled={isWaiting}
                    />

                    <div className="flex justify-end">
                        <button
                            onClick={runScript}
                            disabled={isWaiting || !script.trim()}
                            className="flex items-center gap-2 px-6 py-2.5 bg-gradient-to-r from-indigo-600 to-violet-600 hover:from-indigo-500 hover:to-violet-500 text-white rounded-lg shadow-lg shadow-indigo-900/20 disabled:opacity-50 disabled:cursor-not-allowed transition-all font-medium text-sm"
                        >
                            {isWaiting ? <RotateCcw className="w-4 h-4 animate-spin" /> : <Play className="w-4 h-4 fill-current" />}
                            {running ? 'Sending...' : polling ? 'Waiting...' : 'Run Script'}
                        </button>
                    </div>
                </div>
            </div>

            {/* Output Panel */}
            {output && (
                <div className={`rounded-xl border ${isError ? 'bg-rose-950/20 border-rose-900/50' : 'bg-slate-900/50 border-slate-700'}`}>
                    <div className="flex items-center justify-between px-4 py-2 border-b border-slate-700/50">
                        <span className="text-sm font-medium text-slate-300 flex items-center gap-2">
                            {polling ? (
                                <>
                                    <Loader2 className="w-4 h-4 animate-spin text-indigo-400" />
                                    Waiting for agent response...
                                </>
                            ) : isError ? (
                                <>
                                    <AlertCircle className="w-4 h-4 text-rose-400" />
                                    Error
                                </>
                            ) : (
                                <>
                                    <CheckCircle className="w-4 h-4 text-emerald-400" />
                                    Command Output
                                </>
                            )}
                        </span>
                    </div>
                    <div className="p-4 max-h-96 overflow-y-auto">
                        <pre className={`font-mono text-sm whitespace-pre-wrap ${isError ? 'text-rose-300' : 'text-emerald-300'}`}>
                            {output}
                        </pre>
                    </div>
                </div>
            )}
        </div>
    );
};

export default RunScriptPanel;