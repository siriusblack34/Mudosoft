import React, { useState, useEffect, useCallback } from "react";
import { Upload, RefreshCcw, Rocket, Package, CheckCircle, AlertCircle, MonitorSmartphone, History, Wifi, WifiOff, Hammer, Activity } from "lucide-react";
import { apiClient } from "../lib/apiClient";

interface LatestVersionInfo {
    version: string;
    fileName?: string;
    uploadedAt?: string;
    sizeBytes?: number;
    message?: string;
}

interface DeviceVersion {
    id: string;
    hostname: string;
    online: boolean;
    agentVersion: string | null;
    storeCode: string | null;
    lastSeen: string | null;
}

interface HistoryEntry {
    version: string;
    fileName: string;
    uploadedAt: string;
    sizeBytes: number;
}

const AgentUpdatePage: React.FC = () => {
    const [latestVersion, setLatestVersion] = useState<LatestVersionInfo | null>(null);
    const [uploading, setUploading] = useState(false);
    const [triggering, setTriggering] = useState(false);
    const [message, setMessage] = useState<{ text: string; type: 'success' | 'error' } | null>(null);
    const [newVersion, setNewVersion] = useState("");
    const [selectedFile, setSelectedFile] = useState<File | null>(null);
    const [devices, setDevices] = useState<DeviceVersion[]>([]);
    const [history, setHistory] = useState<HistoryEntry[]>([]);

    // Seçili cihazlara güncelleme — toplu dağıtım öncesi test için
    const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
    const [bulkTriggering, setBulkTriggering] = useState(false);
    const [bulkProgress, setBulkProgress] = useState<{ done: number; total: number; failed: number } | null>(null);

    // Build state
    const [isBuilding, setIsBuilding] = useState(false);
    const [buildStatus, setBuildStatus] = useState("");
    const [buildError, setBuildError] = useState("");

    const [remoteBackendUrl, setRemoteBackendUrl] = useState(() => {
        return localStorage.getItem('remoteBackendUrl') || window.location.origin;
    });

    const fetchLatestVersion = useCallback(async () => {
        try {
            const data = await apiClient.getLatestVersion();
            setLatestVersion(data);
        } catch (err) { console.error(err); }
    }, []);

    const fetchDevices = useCallback(async () => {
        try {
            const data = await apiClient.getDeviceVersions();
            setDevices(data);
        } catch (err) { console.error(err); }
    }, []);

    const fetchHistory = useCallback(async () => {
        try {
            const data = await apiClient.getUpdateHistory();
            setHistory(Array.isArray(data) ? data.reverse() : []);
        } catch (err) { console.error(err); }
    }, []);

    useEffect(() => {
        fetchLatestVersion();
        fetchDevices();
        fetchHistory();
        const interval = setInterval(() => {
            fetchDevices();
        }, 10000);
        return () => clearInterval(interval);
    }, [fetchLatestVersion, fetchDevices, fetchHistory]);

    // Build status poller
    useEffect(() => {
        const pollBuildStatus = async () => {
            try {
                const data = await apiClient.getBuildStatus();

                // If it just finished building, refresh list
                if (isBuilding && !data.isBuilding) {
                    fetchLatestVersion();
                    fetchHistory();
                    if (!data.error) {
                        setMessage({ text: 'Yeni agent build başarıyla tamamlandı!', type: 'success' });
                    }
                }

                setIsBuilding(data.isBuilding);
                setBuildStatus(data.status);
                setBuildError(data.error);

            } catch (err) { console.error(err); }
        };

        const buildInterval = setInterval(pollBuildStatus, 2000);
        pollBuildStatus(); // initial fetch
        return () => clearInterval(buildInterval);
    }, [isBuilding, fetchLatestVersion, fetchHistory]);

    useEffect(() => {
        localStorage.setItem('remoteBackendUrl', remoteBackendUrl);
    }, [remoteBackendUrl]);

    const handleUpload = async () => {
        if (!selectedFile || !newVersion.trim()) {
            setMessage({ text: "Dosya ve versiyon numarası gerekli", type: 'error' });
            return;
        }
        setUploading(true);
        setMessage(null);
        try {
            const data = await apiClient.uploadAgentPackage(selectedFile, newVersion.trim());
            setMessage({ text: data.message || 'Upload başarılı!', type: 'success' });
            setSelectedFile(null);
            setNewVersion("");
            fetchLatestVersion();
            fetchHistory();
        } catch (err: unknown) {
            const msg = err instanceof Error ? err.message : 'Bilinmeyen hata';
            setMessage({ text: `Hata: ${msg}`, type: 'error' });
        } finally { setUploading(false); }
    };

    const handleTriggerAll = async () => {
        if (!latestVersion?.version || latestVersion.version === 'none') {
            setMessage({ text: "Önce bir versiyon yükleyin", type: 'error' });
            return;
        }
        if (!remoteBackendUrl.trim()) {
            setMessage({ text: "Backend URL gerekli", type: 'error' });
            return;
        }
        setTriggering(true);
        setMessage(null);
        try {
            const data = await apiClient.triggerAllUpdates(remoteBackendUrl);
            setMessage({ text: data.message || 'Güncelleme komutu gönderildi!', type: 'success' });
        } catch (err: unknown) {
            const msg = err instanceof Error ? err.message : 'Bilinmeyen hata';
            setMessage({ text: `Hata: ${msg}`, type: 'error' });
        } finally { setTriggering(false); }
    };

    const toggleSelect = (id: string) => {
        setSelectedIds(prev => {
            const next = new Set(prev);
            if (next.has(id)) next.delete(id); else next.add(id);
            return next;
        });
    };

    const toggleSelectAll = () => {
        setSelectedIds(prev => {
            if (prev.size === devices.length) return new Set();
            return new Set(devices.map(d => d.id));
        });
    };

    const clearSelection = () => setSelectedIds(new Set());

    const handleTriggerSelected = async () => {
        if (selectedIds.size === 0) return;
        if (!latestVersion?.version || latestVersion.version === 'none') {
            setMessage({ text: "Önce bir versiyon yükleyin", type: 'error' });
            return;
        }
        if (!remoteBackendUrl.trim()) {
            setMessage({ text: "Backend URL gerekli", type: 'error' });
            return;
        }
        const ids = Array.from(selectedIds);
        setBulkTriggering(true);
        setBulkProgress({ done: 0, total: ids.length, failed: 0 });
        setMessage(null);
        let failed = 0;
        for (let i = 0; i < ids.length; i++) {
            try {
                await apiClient.triggerUpdate(ids[i], remoteBackendUrl);
            } catch (err) {
                failed++;
                console.error(`triggerUpdate ${ids[i]} failed`, err);
            }
            setBulkProgress({ done: i + 1, total: ids.length, failed });
        }
        setBulkTriggering(false);
        setMessage({
            text: failed === 0
                ? `${ids.length} cihaza güncelleme komutu gönderildi.`
                : `${ids.length} cihazdan ${ids.length - failed} başarılı, ${failed} başarısız.`,
            type: failed === 0 ? 'success' : 'error'
        });
    };

    const handleBuildNewAgent = async () => {

        setMessage(null);
        try {
            await apiClient.buildNewAgent();
            setIsBuilding(true);
            setBuildStatus("Derleme başlatılıyor...");
        } catch (err: unknown) {
            const msg = err instanceof Error ? err.message : 'Bilinmeyen hata';
            setMessage({ text: `Build başlatılamadı: ${msg}`, type: 'error' });
        }
    };

    const formatBytes = (bytes: number) => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    };

    const formatDate = (iso: string) => {
        try { return new Date(iso).toLocaleString('tr-TR'); } catch { return iso; }
    };

    const isUpToDate = (deviceVersion: string | null): boolean => {
        if (!deviceVersion || !latestVersion?.version || latestVersion.version === 'none') return false;
        return deviceVersion === latestVersion.version;
    };

    return (
        <div className="space-y-6 p-6 max-w-6xl mx-auto">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                    <Package className="w-8 h-8 text-violet-400" />
                    <h1 className="text-2xl font-semibold">Agent Güncelleme</h1>
                </div>
                <button onClick={() => { fetchDevices(); fetchLatestVersion(); fetchHistory(); }}
                    className="text-slate-400 hover:text-white p-2 rounded-lg hover:bg-slate-700 transition-colors">
                    <RefreshCcw className="w-5 h-5" />
                </button>
            </div>

            {/* Message */}
            {message && (
                <div className={`p-4 rounded-xl flex items-center gap-3 ${message.type === 'success' ? 'bg-emerald-900/30 border border-emerald-700 text-emerald-300' : 'bg-red-900/30 border border-red-700 text-red-300'}`}>
                    {message.type === 'success' ? <CheckCircle className="w-5 h-5" /> : <AlertCircle className="w-5 h-5" />}
                    {message.text}
                </div>
            )}

            {/* Top Row: Current Version + Upload */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                {/* Current Version */}
                <section className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-lg">
                    <h2 className="text-lg font-medium mb-4 flex items-center gap-2">
                        <Rocket className="w-5 h-5 text-violet-400" />
                        Mevcut Versiyon
                    </h2>
                    {latestVersion ? (
                        <div className="space-y-3">
                            <span className="text-4xl font-bold text-violet-400">
                                v{latestVersion.version !== 'none' ? latestVersion.version : '-'}
                            </span>
                            {latestVersion.fileName && (
                                <div className="text-sm text-slate-400 space-y-1">
                                    <div>Dosya: <span className="text-slate-300">{latestVersion.fileName}</span></div>
                                    {latestVersion.sizeBytes && <div>Boyut: <span className="text-slate-300">{formatBytes(latestVersion.sizeBytes)}</span></div>}
                                    {latestVersion.uploadedAt && <div>Tarih: <span className="text-slate-300">{formatDate(latestVersion.uploadedAt)}</span></div>}
                                </div>
                            )}
                            {latestVersion.version !== 'none' && (
                                <div className="space-y-3 mt-4">
                                    <div>
                                        <label className="block text-xs text-slate-500 mb-1">Backend URL (Uzak Makineler İçin)</label>
                                        <input type="text" value={remoteBackendUrl} onChange={(e) => setRemoteBackendUrl(e.target.value)}
                                            className="w-full bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-violet-500" />
                                    </div>
                                    <button onClick={handleTriggerAll} disabled={triggering}
                                        className="w-full bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white px-4 py-3 rounded-xl flex items-center justify-center gap-2 transition-colors font-medium">
                                        <Rocket className="w-5 h-5" />
                                        {triggering ? 'Gönderiliyor...' : 'Tüm Cihazları Güncelle'}
                                    </button>
                                </div>
                            )}
                        </div>
                    ) : (
                        <div className="text-slate-500">Yükleniyor...</div>
                    )}
                </section>

                {/* Upload */}
                <section className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-lg">
                    <h2 className="text-lg font-medium mb-4 flex items-center gap-2">
                        <Upload className="w-5 h-5 text-slate-400" />
                        Yeni Versiyon Yükle
                    </h2>
                    <div className="space-y-4">
                        <div>
                            <label className="block text-xs text-slate-500 mb-1">Versiyon Numarası</label>
                            <input type="text" value={newVersion} onChange={(e) => setNewVersion(e.target.value)} placeholder="1.0.0.36"
                                className="w-full bg-slate-900 border border-slate-600 rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-violet-500" />
                        </div>
                        <div>
                            <label className="block text-xs text-slate-500 mb-1">Agent Paketi (ZIP)</label>
                            <div className="flex items-center gap-3">
                                <label className="cursor-pointer bg-slate-700 hover:bg-slate-600 px-4 py-2 rounded-lg transition-colors text-sm">
                                    Dosya Seç
                                    <input type="file" accept=".zip" onChange={(e) => e.target.files?.length && setSelectedFile(e.target.files[0])} className="hidden" />
                                </label>
                                {selectedFile && <span className="text-slate-300 text-sm">{selectedFile.name} ({formatBytes(selectedFile.size)})</span>}
                            </div>
                        </div>
                        <button onClick={handleUpload} disabled={uploading || !selectedFile || !newVersion.trim()}
                            className="w-full bg-violet-600 hover:bg-violet-500 disabled:opacity-50 disabled:cursor-not-allowed text-white px-4 py-3 rounded-xl flex items-center justify-center gap-2 transition-colors font-medium">
                            <Upload className="w-5 h-5" />
                            {uploading ? 'Yükleniyor...' : 'Manuel Yükle'}
                        </button>
                    </div>
                </section>
            </div>

            {/* Auto Build Card */}
            <section className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-lg">
                <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-6">
                    <div>
                        <h2 className="text-lg font-medium mb-2 flex items-center gap-2">
                            <Hammer className="w-5 h-5 text-amber-500" />
                            Otomatik Agent Build
                        </h2>
                        <p className="text-sm text-slate-400 max-w-2xl">
                            Bu özellik agent kaynak kodunu derler, versiyonunu otomatik artırır, ZIP'ler ve sisteme yükler.
                            Geliştirmeyi tamamladıktan sonra yeni versiyonu tüm cihazlara yaymak için bu butonu kullanın.
                        </p>
                    </div>

                    <div className="flex-shrink-0 w-full md:w-auto">
                        <button
                            onClick={handleBuildNewAgent}
                            disabled={isBuilding}
                            className="w-full md:w-auto bg-amber-600 hover:bg-amber-500 disabled:bg-slate-700 disabled:text-slate-400 text-white px-6 py-3 rounded-xl flex items-center justify-center gap-2 transition-colors font-medium shadow-lg shadow-amber-900/20"
                        >
                            {isBuilding ? <Activity className="w-5 h-5 animate-spin" /> : <Hammer className="w-5 h-5" />}
                            {isBuilding ? 'Derleniyor...' : 'Yeni Agent Build Oluştur'}
                        </button>
                    </div>
                </div>

                {/* Build Status Bar */}
                {isBuilding && (
                    <div className="mt-6 p-4 bg-slate-900/50 rounded-lg border border-slate-700/50 flex items-center gap-3">
                        <div className="w-2 h-2 rounded-full bg-amber-500 animate-pulse"></div>
                        <span className="font-mono text-sm text-amber-400">{buildStatus || 'İşlem devam ediyor...'}</span>
                    </div>
                )}
                {buildError && !isBuilding && (
                    <div className="mt-4 p-4 rounded-lg bg-red-900/30 border border-red-800 text-red-400 text-sm whitespace-pre-wrap">
                        <span className="font-semibold block mb-1">Build Hatası:</span>
                        {buildError}
                    </div>
                )}
            </section>

            {/* Device Versions Table */}
            <section className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-lg">
                <div className="flex items-center justify-between mb-4 gap-4 flex-wrap">
                    <h2 className="text-lg font-medium flex items-center gap-2">
                        <MonitorSmartphone className="w-5 h-5 text-blue-400" />
                        Cihaz Versiyonları
                        <span className="text-sm text-slate-500 font-normal ml-2">({devices.length} cihaz)</span>
                        {selectedIds.size > 0 && (
                            <span className="text-sm font-normal ml-2 px-2 py-0.5 rounded-full bg-violet-900/40 text-violet-300 border border-violet-700">
                                {selectedIds.size} seçili
                            </span>
                        )}
                    </h2>
                    <div className="flex items-center gap-2">
                        {selectedIds.size > 0 && (
                            <button onClick={clearSelection}
                                className="text-xs px-3 py-1.5 rounded-lg text-slate-300 hover:bg-slate-700 transition-colors">
                                Seçimi Temizle
                            </button>
                        )}
                        <button onClick={handleTriggerSelected}
                            disabled={bulkTriggering || selectedIds.size === 0 || !latestVersion?.version || latestVersion.version === 'none'}
                            className="text-sm px-4 py-2 rounded-xl bg-violet-600 hover:bg-violet-500 disabled:opacity-40 disabled:cursor-not-allowed text-white flex items-center gap-2 transition-colors font-medium">
                            <Rocket className="w-4 h-4" />
                            {bulkTriggering && bulkProgress
                                ? `Gönderiliyor ${bulkProgress.done}/${bulkProgress.total}${bulkProgress.failed > 0 ? ` (${bulkProgress.failed} hata)` : ''}`
                                : `Seçili Cihazları Güncelle${selectedIds.size > 0 ? ` (${selectedIds.size})` : ''}`}
                        </button>
                    </div>
                </div>
                <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                        <thead>
                            <tr className="text-left text-slate-400 border-b border-slate-700">
                                <th className="pb-3 pr-4 w-8">
                                    <input type="checkbox"
                                        checked={devices.length > 0 && selectedIds.size === devices.length}
                                        ref={el => { if (el) el.indeterminate = selectedIds.size > 0 && selectedIds.size < devices.length; }}
                                        onChange={toggleSelectAll}
                                        className="w-4 h-4 accent-violet-500 cursor-pointer"
                                        title="Tümünü seç / Tümünü temizle"
                                    />
                                </th>
                                <th className="pb-3 pr-4">Durum</th>
                                <th className="pb-3 pr-4">Cihaz</th>
                                <th className="pb-3 pr-4">Mağaza</th>
                                <th className="pb-3 pr-4">Mevcut Versiyon</th>
                                <th className="pb-3 pr-4">Güncel?</th>
                                <th className="pb-3">Son Heartbeat</th>
                            </tr>
                        </thead>
                        <tbody>
                            {devices.map((d) => (
                                <tr key={d.id}
                                    onClick={() => toggleSelect(d.id)}
                                    className={`border-b border-slate-800 cursor-pointer transition-colors ${selectedIds.has(d.id) ? 'bg-violet-900/20 hover:bg-violet-900/30' : 'hover:bg-slate-800/30'}`}>
                                    <td className="py-3 pr-4" onClick={e => e.stopPropagation()}>
                                        <input type="checkbox"
                                            checked={selectedIds.has(d.id)}
                                            onChange={() => toggleSelect(d.id)}
                                            className="w-4 h-4 accent-violet-500 cursor-pointer"
                                        />
                                    </td>
                                    <td className="py-3 pr-4">
                                        {d.online
                                            ? <Wifi className="w-4 h-4 text-emerald-400" />
                                            : <WifiOff className="w-4 h-4 text-slate-600" />}
                                    </td>
                                    <td className="py-3 pr-4 font-medium text-slate-200">{d.hostname || d.id}</td>
                                    <td className="py-3 pr-4 text-slate-400">{d.storeCode || '-'}</td>
                                    <td className="py-3 pr-4">
                                        <span className="font-mono text-slate-300">{d.agentVersion || '-'}</span>
                                    </td>
                                    <td className="py-3 pr-4">
                                        {isUpToDate(d.agentVersion) ? (
                                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-emerald-900/40 text-emerald-400 border border-emerald-800">
                                                <CheckCircle className="w-3 h-3" /> Güncel
                                            </span>
                                        ) : (
                                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-amber-900/40 text-amber-400 border border-amber-800">
                                                <AlertCircle className="w-3 h-3" /> Eski
                                            </span>
                                        )}
                                    </td>
                                    <td className="py-3 text-slate-500 text-xs">
                                        {d.lastSeen ? formatDate(d.lastSeen) : '-'}
                                    </td>
                                </tr>
                            ))}
                            {devices.length === 0 && (
                                <tr><td colSpan={7} className="py-6 text-center text-slate-500">Henüz cihaz yok</td></tr>
                            )}
                        </tbody>
                    </table>
                </div>
            </section>

            {/* Version History */}
            {history.length > 0 && (
                <section className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-lg">
                    <h2 className="text-lg font-medium mb-4 flex items-center gap-2">
                        <History className="w-5 h-5 text-slate-400" />
                        Versiyon Geçmişi
                    </h2>
                    <div className="space-y-2">
                        {history.map((h, i) => (
                            <div key={i} className={`flex items-center justify-between py-2 px-3 rounded-lg ${i === 0 ? 'bg-violet-900/20 border border-violet-800/50' : 'hover:bg-slate-800/30'}`}>
                                <div className="flex items-center gap-3">
                                    <span className={`font-mono font-bold ${i === 0 ? 'text-violet-400' : 'text-slate-400'}`}>v{h.version}</span>
                                    {i === 0 && <span className="text-xs bg-violet-600 px-2 py-0.5 rounded-full text-white">Son</span>}
                                </div>
                                <div className="flex items-center gap-4 text-xs text-slate-500">
                                    <span>{formatBytes(h.sizeBytes)}</span>
                                    <span>{formatDate(h.uploadedAt)}</span>
                                </div>
                            </div>
                        ))}
                    </div>
                </section>
            )}
        </div>
    );
};

export default AgentUpdatePage;
