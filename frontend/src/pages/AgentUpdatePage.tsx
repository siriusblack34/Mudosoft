import React, { useState, useEffect } from "react";
import { Upload, RefreshCcw, Rocket, Package, CheckCircle, AlertCircle } from "lucide-react";
import { API_BASE_URL } from "../lib/apiClient";

interface LatestVersionInfo {
    version: string;
    fileName?: string;
    uploadedAt?: string;
    sizeBytes?: number;
    message?: string;
}

const AgentUpdatePage: React.FC = () => {
    const [latestVersion, setLatestVersion] = useState<LatestVersionInfo | null>(null);
    const [uploading, setUploading] = useState(false);
    const [triggering, setTriggering] = useState(false);
    const [message, setMessage] = useState<{ text: string; type: 'success' | 'error' } | null>(null);
    const [newVersion, setNewVersion] = useState("");
    const [selectedFile, setSelectedFile] = useState<File | null>(null);

    // Backend URL that remote agents can reach (not localhost!)
    const [remoteBackendUrl, setRemoteBackendUrl] = useState(() => {
        return localStorage.getItem('remoteBackendUrl') || 'http://10.0.102.60:5102';
    });

    const fetchLatestVersion = async () => {
        try {
            const res = await fetch(`${API_BASE_URL}/api/updates/latest`);
            const data = await res.json();
            setLatestVersion(data);
        } catch (err) {
            console.error("Failed to fetch latest version:", err);
        }
    };

    useEffect(() => {
        fetchLatestVersion();
    }, []);

    useEffect(() => {
        localStorage.setItem('remoteBackendUrl', remoteBackendUrl);
    }, [remoteBackendUrl]);

    const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files?.length) {
            setSelectedFile(e.target.files[0]);
        }
    };

    const handleUpload = async () => {
        if (!selectedFile || !newVersion.trim()) {
            setMessage({ text: "Dosya ve versiyon numarası gerekli", type: 'error' });
            return;
        }

        setUploading(true);
        setMessage(null);

        try {
            const formData = new FormData();
            formData.append('file', selectedFile);
            formData.append('version', newVersion.trim());

            const res = await fetch(`${API_BASE_URL}/api/updates/upload`, {
                method: 'POST',
                body: formData
            });

            if (res.ok) {
                const data = await res.json();
                setMessage({ text: `${data.message || 'Upload başarılı!'}`, type: 'success' });
                setSelectedFile(null);
                setNewVersion("");
                fetchLatestVersion();
            } else {
                setMessage({ text: `Upload başarısız: ${res.status}`, type: 'error' });
            }
        } catch (err: any) {
            setMessage({ text: `Hata: ${err.message}`, type: 'error' });
        } finally {
            setUploading(false);
        }
    };

    const handleTriggerAll = async () => {
        if (!latestVersion?.version || latestVersion.version === 'none') {
            setMessage({ text: "Önce bir versiyon yükleyin", type: 'error' });
            return;
        }

        if (!remoteBackendUrl.trim()) {
            setMessage({ text: "Backend URL gerekli (uzak makinelerin erişebileceği IP)", type: 'error' });
            return;
        }

        const confirmed = window.confirm(`Tüm online cihazlara v${latestVersion.version} güncellemesi gönderilecek.\nBackend URL: ${remoteBackendUrl}\n\nOnaylıyor musunuz?`);
        if (!confirmed) return;

        setTriggering(true);
        setMessage(null);

        try {
            const res = await fetch(`${API_BASE_URL}/api/updates/trigger-all?backendUrl=${encodeURIComponent(remoteBackendUrl)}`, {
                method: 'POST'
            });

            if (res.ok) {
                const data = await res.json();
                setMessage({ text: `${data.message || 'Güncelleme komutu gönderildi!'}`, type: 'success' });
            } else {
                setMessage({ text: `Güncelleme başarısız: ${res.status}`, type: 'error' });
            }
        } catch (err: any) {
            setMessage({ text: `Hata: ${err.message}`, type: 'error' });
        } finally {
            setTriggering(false);
        }
    };

    const formatBytes = (bytes: number) => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    };

    const formatDate = (isoString: string) => {
        try {
            return new Date(isoString).toLocaleString();
        } catch {
            return isoString;
        }
    };

    return (
        <div className="space-y-6 p-6 max-w-4xl mx-auto">
            {/* Header */}
            <div className="flex items-center gap-3">
                <Package className="w-8 h-8 text-violet-400" />
                <h1 className="text-2xl font-semibold">Agent Güncelleme</h1>
            </div>

            {/* Message */}
            {message && (
                <div className={`p-4 rounded-xl flex items-center gap-3 ${message.type === 'success' ? 'bg-emerald-900/30 border border-emerald-700 text-emerald-300' : 'bg-red-900/30 border border-red-700 text-red-300'}`}>
                    {message.type === 'success' ? <CheckCircle className="w-5 h-5" /> : <AlertCircle className="w-5 h-5" />}
                    {message.text}
                </div>
            )}

            {/* Current Version Card */}
            <section className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-lg">
                <h2 className="text-lg font-medium mb-4 flex items-center gap-2">
                    <RefreshCcw className="w-5 h-5 text-slate-400" />
                    Mevcut Versiyon
                </h2>

                {latestVersion ? (
                    <div className="space-y-3">
                        <div className="flex items-center gap-4">
                            <span className="text-3xl font-bold text-violet-400">
                                v{latestVersion.version !== 'none' ? latestVersion.version : '-'}
                            </span>
                            {latestVersion.version === 'none' && (
                                <span className="text-slate-500">Henüz versiyon yüklenmedi</span>
                            )}
                        </div>

                        {latestVersion.fileName && (
                            <div className="text-sm text-slate-400 space-y-1">
                                <div>Dosya: <span className="text-slate-300">{latestVersion.fileName}</span></div>
                                {latestVersion.sizeBytes && <div>Boyut: <span className="text-slate-300">{formatBytes(latestVersion.sizeBytes)}</span></div>}
                                {latestVersion.uploadedAt && <div>Yüklenme: <span className="text-slate-300">{formatDate(latestVersion.uploadedAt)}</span></div>}
                            </div>
                        )}

                        {latestVersion.version !== 'none' && (
                            <div className="space-y-4 mt-4">
                                {/* Backend URL Input */}
                                <div>
                                    <label className="block text-sm text-slate-400 mb-1">
                                        Backend URL (Uzak Makinelerin Erişeceği)
                                    </label>
                                    <input
                                        type="text"
                                        value={remoteBackendUrl}
                                        onChange={(e) => setRemoteBackendUrl(e.target.value)}
                                        placeholder="http://10.0.102.60:5102"
                                        className="w-full max-w-md bg-slate-900 border border-slate-600 rounded-lg px-4 py-2 focus:outline-none focus:border-violet-500"
                                    />
                                    <p className="text-xs text-slate-500 mt-1">
                                        ⚠️ localhost yerine gerçek IP adresi gir (örn: http://10.0.102.60:5102)
                                    </p>
                                </div>

                                <button
                                    onClick={handleTriggerAll}
                                    disabled={triggering}
                                    className="bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white px-6 py-3 rounded-xl flex items-center gap-2 transition-colors font-medium"
                                >
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

            {/* Upload New Version */}
            <section className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-lg">
                <h2 className="text-lg font-medium mb-4 flex items-center gap-2">
                    <Upload className="w-5 h-5 text-slate-400" />
                    Yeni Versiyon Yükle
                </h2>

                <div className="space-y-4">
                    {/* Version Input */}
                    <div>
                        <label className="block text-sm text-slate-400 mb-1">Versiyon Numarası</label>
                        <input
                            type="text"
                            value={newVersion}
                            onChange={(e) => setNewVersion(e.target.value)}
                            placeholder="1.0.0.2"
                            className="w-full max-w-xs bg-slate-900 border border-slate-600 rounded-lg px-4 py-2 focus:outline-none focus:border-violet-500"
                        />
                    </div>

                    {/* File Input */}
                    <div>
                        <label className="block text-sm text-slate-400 mb-1">Agent Paketi (ZIP)</label>
                        <div className="flex items-center gap-4">
                            <label className="cursor-pointer bg-slate-700 hover:bg-slate-600 px-4 py-2 rounded-lg transition-colors">
                                <span>Dosya Seç</span>
                                <input
                                    type="file"
                                    accept=".zip"
                                    onChange={handleFileSelect}
                                    className="hidden"
                                />
                            </label>
                            {selectedFile && (
                                <span className="text-slate-300">
                                    {selectedFile.name} ({formatBytes(selectedFile.size)})
                                </span>
                            )}
                        </div>
                    </div>

                    {/* Upload Button */}
                    <button
                        onClick={handleUpload}
                        disabled={uploading || !selectedFile || !newVersion.trim()}
                        className="bg-violet-600 hover:bg-violet-500 disabled:opacity-50 disabled:cursor-not-allowed text-white px-6 py-3 rounded-xl flex items-center gap-2 transition-colors font-medium"
                    >
                        <Upload className="w-5 h-5" />
                        {uploading ? 'Yükleniyor...' : 'Yükle'}
                    </button>
                </div>
            </section>

            {/* Instructions */}
            <section className="bg-slate-800/30 p-6 rounded-2xl border border-slate-700/50">
                <h3 className="font-medium mb-3 text-slate-300">Nasıl Kullanılır?</h3>
                <ol className="text-sm text-slate-400 space-y-2 list-decimal list-inside">
                    <li><code className="bg-slate-800 px-1 rounded">publish_single/</code> klasörünün içeriğini ZIP'le</li>
                    <li>Versiyon numarasını gir (örn: 1.0.0.2)</li>
                    <li>ZIP dosyasını seç ve "Yükle" butonuna tıkla</li>
                    <li>"Tüm Cihazları Güncelle" butonuna tıkla</li>
                    <li>Agentlar otomatik olarak güncellenir ve restart olur</li>
                </ol>
            </section>
        </div>
    );
};

export default AgentUpdatePage;
