import React, { useState, useEffect, useRef, useCallback } from "react";
import { useParams, useNavigate } from "react-router-dom";
import {
    ArrowLeft, Folder, FolderOpen, File, Upload, Download, Trash2,
    FolderPlus, RefreshCcw, ChevronRight, Home, AlertCircle,
    X, CheckCircle2, Loader2, FileText, Image, FileCode, Archive, Zap,
} from "lucide-react";
import { apiClient, API_BASE_URL } from "../lib/apiClient";

// ── Types ─────────────────────────────────────────────────────────────────

interface FileItem {
    name: string;
    fullPath: string;
    isDirectory: boolean;
    sizeBytes: number;
    lastModified: string;
}

type UploadStatus = "idle" | "hashing" | "uploading" | "completing" | "done" | "error" | "cancelled";

interface UploadJob {
    id: string;
    fileName: string;
    status: UploadStatus;
    progress: number;
    speedKbps: number;
    error?: string;
    transferId?: string;
    cancel?: () => void;
}

// ── Helpers ───────────────────────────────────────────────────────────────

const CHUNK_SIZE = 1024 * 1024; // 1MB

function formatSize(bytes: number): string {
    if (!bytes) return "";
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

function fileIcon(name: string, isDir: boolean): React.ReactNode {
    if (isDir) return <Folder className="w-4 h-4 text-amber-400 shrink-0" />;
    const ext = name.split(".").pop()?.toLowerCase() ?? "";
    if (["png", "jpg", "jpeg", "gif", "bmp", "webp"].includes(ext))
        return <Image className="w-4 h-4 text-blue-400 shrink-0" />;
    if (["zip", "rar", "7z", "tar", "gz"].includes(ext))
        return <Archive className="w-4 h-4 text-orange-400 shrink-0" />;
    if (["js", "ts", "cs", "py", "bat", "ps1", "sh", "xml", "json", "sql"].includes(ext))
        return <FileCode className="w-4 h-4 text-violet-400 shrink-0" />;
    if (["txt", "log", "md", "csv"].includes(ext))
        return <FileText className="w-4 h-4 text-zinc-400 shrink-0" />;
    return <File className="w-4 h-4 text-zinc-400 shrink-0" />;
}

async function computeSha256(file: File): Promise<string> {
    const buffer = await file.arrayBuffer();
    const hashBuffer = await crypto.subtle.digest("SHA-256", buffer);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    return hashArray.map(b => b.toString(16).padStart(2, "0")).join("");
}

// ── Main Page ─────────────────────────────────────────────────────────────

const FileManagerPage: React.FC = () => {
    const { deviceId } = useParams<{ deviceId: string }>();
    const navigate = useNavigate();

    const [currentPath, setCurrentPath] = useState("C:\\");
    const [files, setFiles] = useState<FileItem[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [selectedFile, setSelectedFile] = useState<FileItem | null>(null);
    const [showNewFolderDialog, setShowNewFolderDialog] = useState(false);
    const [newFolderName, setNewFolderName] = useState("");

    // Upload jobs
    const [uploadJobs, setUploadJobs] = useState<UploadJob[]>([]);

    // Download status
    const [downloadStatus, setDownloadStatus] = useState<string | null>(null);

    const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);
    const commandIdRef = useRef<string | null>(null);

    // ── Directory Operations ─────────────────────────────────────────

    const loadDirectory = useCallback(async (path: string) => {
        if (!deviceId) return;
        setLoading(true);
        setError(null);
        setFiles([]);
        setSelectedFile(null);
        if (pollRef.current) clearInterval(pollRef.current);

        try {
            const res = await apiClient.post<{ commandId: string }>(
                `/api/agent/files/list?deviceId=${deviceId}&path=${encodeURIComponent(path)}`
            );
            commandIdRef.current = res.commandId;

            let attempts = 0;
            pollRef.current = setInterval(async () => {
                attempts++;
                try {
                    const result = await apiClient.get<any>(
                        `/api/agent/command-results/${commandIdRef.current}`
                    );
                    if (result?.success) {
                        if (pollRef.current) clearInterval(pollRef.current);
                        setLoading(false);
                        try {
                            const parsed = JSON.parse(result.output);
                            const items: FileItem[] = Array.isArray(parsed) ? parsed : [];
                            items.sort((a, b) => {
                                if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1;
                                return a.name.localeCompare(b.name);
                            });
                            setFiles(items);
                            setCurrentPath(path);
                        } catch { setError("Dizin listesi işlenemedi"); }
                    } else if (result && !result.success && result.output) {
                        if (pollRef.current) clearInterval(pollRef.current);
                        setLoading(false);
                        setError(result.output);
                    }
                } catch (err: any) {
                    if (!err.message?.includes("404")) {
                        if (pollRef.current) clearInterval(pollRef.current);
                        setLoading(false);
                        setError("Polling hatası");
                    }
                }
                if (attempts >= 60) {
                    if (pollRef.current) clearInterval(pollRef.current);
                    setLoading(false);
                    setError("Zaman aşımı");
                }
            }, 400);
        } catch {
            setError("Komut gönderilemedi");
            setLoading(false);
        }
    }, [deviceId]);

    useEffect(() => {
        loadDirectory(currentPath);
        return () => { if (pollRef.current) clearInterval(pollRef.current); };
    }, [deviceId]); // eslint-disable-line

    const navigateTo = (path: string) => loadDirectory(path);

    const navigateUp = () => {
        const parts = currentPath.split("\\").filter(Boolean);
        if (parts.length > 1) {
            parts.pop();
            navigateTo(parts.join("\\") + "\\");
        } else if (parts.length === 1) {
            navigateTo(parts[0] + "\\");
        }
    };

    const handleItemClick = (item: FileItem) => {
        if (item.isDirectory) navigateTo(item.fullPath + "\\");
        else setSelectedFile(prev => prev?.fullPath === item.fullPath ? null : item);
    };

    const handleCreateFolder = async () => {
        if (!deviceId || !newFolderName.trim()) return;
        const folderPath = currentPath + newFolderName.trim();
        try {
            await apiClient.post(`/api/agent/files/mkdir?deviceId=${deviceId}&path=${encodeURIComponent(folderPath)}`);
            setShowNewFolderDialog(false);
            setNewFolderName("");
            setTimeout(() => loadDirectory(currentPath), 600);
        } catch { /* ignore */ }
    };

    const handleDelete = async (item: FileItem) => {
        if (!deviceId) return;
        if (!window.confirm(`"${item.name}" silinsin mi?${item.isDirectory ? " Tüm içerik silinecek!" : ""}`)) return;
        try {
            await apiClient.delete(`/api/agent/files?deviceId=${deviceId}&path=${encodeURIComponent(item.fullPath)}`);
            setTimeout(() => loadDirectory(currentPath), 500);
        } catch { /* ignore */ }
    };

    // ── Chunked Upload ───────────────────────────────────────────────

    const handleUpload = async (event: React.ChangeEvent<HTMLInputElement>) => {
        if (!deviceId || !event.target.files?.length) return;
        const files = Array.from(event.target.files);
        event.target.value = "";
        for (const file of files) {
            startChunkedUpload(file);
        }
    };

    const startChunkedUpload = async (file: File) => {
        const jobId = `${Date.now()}-${file.name}`;
        let cancelled = false;

        const updateJob = (patch: Partial<UploadJob>) =>
            setUploadJobs(prev => prev.map(j => j.id === jobId ? { ...j, ...patch } : j));

        const cancel = () => {
            cancelled = true;
            updateJob({ status: "cancelled" });
        };

        setUploadJobs(prev => [...prev, {
            id: jobId, fileName: file.name,
            status: "hashing", progress: 0, speedKbps: 0, cancel
        }]);

        try {
            // 1. Hash computation
            const hash = await computeSha256(file);
            if (cancelled) return;
            updateJob({ status: "uploading" });

            // 2. Init transfer
            const totalChunks = Math.ceil(file.size / CHUNK_SIZE);
            const initRes = await apiClient.post<{ transferId: string }>("/api/files/upload/init", {
                deviceId,
                fileName: file.name,
                remotePath: currentPath,
                totalSize: file.size,
                totalChunks,
                expectedHash: hash
            });

            updateJob({ transferId: initRes.transferId });

            // 3. Send chunks
            const startTime = Date.now();
            let bytesSent = 0;

            for (let i = 0; i < totalChunks; i++) {
                if (cancelled) {
                    await apiClient.delete(`/api/files/upload/${initRes.transferId}`);
                    return;
                }

                const start = i * CHUNK_SIZE;
                const end = Math.min(start + CHUNK_SIZE, file.size);
                const chunk = file.slice(start, end);

                const formData = new FormData();
                formData.append("chunk", chunk, `chunk_${i}`);

                const res = await fetch(
                    `${API_BASE_URL}/api/files/upload/chunk/${initRes.transferId}?chunkIndex=${i}`,
                    {
                        method: "POST",
                        headers: { Authorization: `Bearer ${localStorage.getItem("token")}` },
                        body: formData
                    }
                );

                if (!res.ok) throw new Error(`Chunk ${i} failed: ${res.status}`);

                bytesSent += (end - start);
                const elapsed = (Date.now() - startTime) / 1000;
                const speedKbps = elapsed > 0 ? Math.round(bytesSent / 1024 / elapsed) : 0;
                const progress = Math.round((i + 1) / totalChunks * 100);
                updateJob({ progress, speedKbps });
            }

            if (cancelled) return;

            // 4. Complete
            updateJob({ status: "completing", progress: 99 });
            await apiClient.post(`/api/files/upload/complete/${initRes.transferId}`);
            updateJob({ status: "done", progress: 100, speedKbps: 0 });

            setTimeout(() => {
                setUploadJobs(prev => prev.filter(j => j.id !== jobId));
                loadDirectory(currentPath);
            }, 3000);

        } catch (e: unknown) {
            if (!cancelled) {
                updateJob({ status: "error", error: e instanceof Error ? e.message : "Bilinmeyen hata" });
            }
        }
    };

    // ── Download ─────────────────────────────────────────────────────

    const handleDownload = async (item: FileItem) => {
        if (!deviceId || item.isDirectory) return;
        setDownloadStatus(`"${item.name}" indiriliyor...`);

        try {
            const res = await apiClient.post<{ commandId: string }>(
                `/api/agent/files/download?deviceId=${deviceId}&path=${encodeURIComponent(item.fullPath)}`
            );

            const deadline = Date.now() + 60_000;
            while (Date.now() < deadline) {
                await new Promise(r => setTimeout(r, 500));
                try {
                    const result = await apiClient.get<any>(`/api/agent/command-results/${res.commandId}`);
                    if (result?.success) {
                        const bytes = Uint8Array.from(atob(result.output), c => c.charCodeAt(0));
                        const blob = new Blob([bytes]);
                        const url = URL.createObjectURL(blob);
                        const a = document.createElement("a");
                        a.href = url;
                        a.download = item.name;
                        a.click();
                        URL.revokeObjectURL(url);
                        setDownloadStatus(null);
                        return;
                    }
                } catch { /* 404 = still pending */ }
            }
            setDownloadStatus("İndirme zaman aşımı");
            setTimeout(() => setDownloadStatus(null), 4000);
        } catch (e: unknown) {
            setDownloadStatus(`Hata: ${e instanceof Error ? e.message : "bilinmeyen"}`);
            setTimeout(() => setDownloadStatus(null), 4000);
        }
    };

    // ── Render ────────────────────────────────────────────────────────

    const pathParts = currentPath.split("\\").filter(Boolean);

    if (!deviceId) return <div className="p-4 text-red-500">Device ID bulunamadı</div>;

    return (
        <div className="flex flex-col h-full p-4 gap-3">
            {/* Header */}
            <div className="flex items-center gap-3">
                <button onClick={() => navigate(`/devices/${deviceId}`)}
                    className="p-2 hover:bg-zinc-800 rounded-lg transition-colors">
                    <ArrowLeft className="w-5 h-5 text-zinc-400" />
                </button>
                <FolderOpen className="w-5 h-5 text-violet-400" />
                <h1 className="text-lg font-semibold text-ms-text">Dosya Yöneticisi</h1>
                <span className="text-xs text-zinc-500 font-mono">{deviceId}</span>
            </div>

            {/* Toolbar */}
            <div className="flex items-center gap-2 bg-zinc-900/60 border border-zinc-800 rounded-xl px-3 py-2">
                {/* Nav buttons */}
                <button onClick={() => navigateTo("C:\\")}
                    className="p-1.5 hover:bg-zinc-800 rounded-lg" title="Ana dizin">
                    <Home className="w-4 h-4 text-zinc-400" />
                </button>
                <button onClick={navigateUp}
                    className="px-2 py-1 text-xs hover:bg-zinc-800 rounded-lg text-zinc-400" title="Üst dizin">
                    ..
                </button>

                {/* Breadcrumb */}
                <div className="flex items-center gap-1 flex-1 overflow-x-auto min-w-0">
                    {pathParts.map((part, i) => (
                        <React.Fragment key={i}>
                            <ChevronRight className="w-3.5 h-3.5 text-zinc-600 shrink-0" />
                            <button
                                onClick={() => navigateTo(pathParts.slice(0, i + 1).join("\\") + "\\")}
                                className="text-xs hover:text-violet-400 text-zinc-300 whitespace-nowrap transition-colors">
                                {part}
                            </button>
                        </React.Fragment>
                    ))}
                </div>

                {/* Actions */}
                <button onClick={() => loadDirectory(currentPath)}
                    className="p-1.5 hover:bg-zinc-800 rounded-lg" title="Yenile">
                    <RefreshCcw className={`w-4 h-4 text-zinc-400 ${loading ? "animate-spin" : ""}`} />
                </button>
                <button onClick={() => setShowNewFolderDialog(true)}
                    className="p-1.5 hover:bg-zinc-800 rounded-lg" title="Yeni Klasör">
                    <FolderPlus className="w-4 h-4 text-zinc-400" />
                </button>
                <label className="p-1.5 hover:bg-zinc-800 rounded-lg cursor-pointer" title="Dosya Yükle">
                    <Upload className="w-4 h-4 text-violet-400" />
                    <input type="file" multiple className="hidden" onChange={handleUpload} />
                </label>
            </div>

            {/* Upload Jobs Panel */}
            {uploadJobs.length > 0 && (
                <div className="space-y-2">
                    {uploadJobs.map(job => (
                        <UploadJobRow key={job.id} job={job}
                            onDismiss={() => setUploadJobs(prev => prev.filter(j => j.id !== job.id))} />
                    ))}
                </div>
            )}

            {/* Download status */}
            {downloadStatus && (
                <div className="flex items-center gap-2 bg-blue-500/10 border border-blue-500/30 rounded-lg px-3 py-2 text-sm text-blue-300">
                    <Loader2 className="w-4 h-4 animate-spin shrink-0" />
                    {downloadStatus}
                </div>
            )}

            {/* File List */}
            <div className="flex-1 bg-zinc-900/40 border border-zinc-800 rounded-xl overflow-hidden min-h-0">
                {/* Column headers */}
                <div className="grid grid-cols-12 gap-2 px-4 py-2 bg-zinc-900 border-b border-zinc-800 text-xs text-zinc-500 uppercase tracking-wider font-medium">
                    <div className="col-span-6">Ad</div>
                    <div className="col-span-2">Boyut</div>
                    <div className="col-span-3">Değiştirme</div>
                    <div className="col-span-1">İşlem</div>
                </div>

                <div className="overflow-y-auto max-h-[calc(100vh-320px)]">
                    {loading && (
                        <div className="flex items-center justify-center py-16 gap-3">
                            <Loader2 className="w-5 h-5 animate-spin text-violet-400" />
                            <span className="text-sm text-zinc-500">Dizin yükleniyor...</span>
                        </div>
                    )}

                    {error && !loading && (
                        <div className="flex flex-col items-center justify-center py-12 gap-3">
                            <AlertCircle className="w-8 h-8 text-red-400" />
                            <p className="text-sm text-red-400">{error}</p>
                            <button onClick={() => loadDirectory(currentPath)}
                                className="text-xs text-zinc-400 hover:text-white underline">
                                Tekrar dene
                            </button>
                        </div>
                    )}

                    {!loading && !error && files.map((item, i) => (
                        <div key={i}
                            className={`grid grid-cols-12 gap-2 px-4 py-2 border-b border-zinc-800/50 cursor-pointer hover:bg-zinc-800/40 transition-colors ${selectedFile?.fullPath === item.fullPath ? "bg-violet-900/15" : ""}`}
                            onClick={() => handleItemClick(item)}>

                            <div className="col-span-6 flex items-center gap-2 min-w-0">
                                {fileIcon(item.name, item.isDirectory)}
                                <span className="truncate text-sm text-ms-text" title={item.name}>{item.name}</span>
                            </div>
                            <div className="col-span-2 text-zinc-500 text-xs self-center">
                                {formatSize(item.sizeBytes)}
                            </div>
                            <div className="col-span-3 text-zinc-600 text-xs self-center">
                                {item.lastModified ? new Date(item.lastModified).toLocaleString("tr-TR") : "—"}
                            </div>
                            <div className="col-span-1 flex items-center gap-1">
                                {!item.isDirectory && (
                                    <button
                                        onClick={e => { e.stopPropagation(); handleDownload(item); }}
                                        className="p-1 hover:bg-zinc-700 rounded text-zinc-400 hover:text-blue-400 transition-colors"
                                        title="İndir">
                                        <Download className="w-3.5 h-3.5" />
                                    </button>
                                )}
                                <button
                                    onClick={e => { e.stopPropagation(); handleDelete(item); }}
                                    className="p-1 hover:bg-red-900/30 rounded text-zinc-600 hover:text-red-400 transition-colors"
                                    title="Sil">
                                    <Trash2 className="w-3.5 h-3.5" />
                                </button>
                            </div>
                        </div>
                    ))}

                    {!loading && !error && files.length === 0 && (
                        <div className="flex flex-col items-center justify-center py-16 gap-3 text-zinc-600">
                            <Folder className="w-10 h-10 opacity-40" />
                            <p className="text-sm">Klasör boş</p>
                        </div>
                    )}
                </div>
            </div>

            {/* New Folder Dialog */}
            {showNewFolderDialog && (
                <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50">
                    <div className="bg-zinc-900 border border-zinc-700 rounded-2xl p-6 w-96 shadow-2xl">
                        <h3 className="text-base font-semibold text-ms-text mb-4">Yeni Klasör</h3>
                        <input type="text" value={newFolderName}
                            onChange={e => setNewFolderName(e.target.value)}
                            onKeyDown={e => e.key === "Enter" && handleCreateFolder()}
                            placeholder="Klasör adı"
                            className="w-full bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-white placeholder-zinc-500 mb-4 focus:outline-none focus:border-violet-500"
                            autoFocus />
                        <div className="flex justify-end gap-2">
                            <button onClick={() => { setShowNewFolderDialog(false); setNewFolderName(""); }}
                                className="px-4 py-2 bg-zinc-800 hover:bg-zinc-700 text-sm rounded-lg">
                                İptal
                            </button>
                            <button onClick={handleCreateFolder} disabled={!newFolderName.trim()}
                                className="px-4 py-2 bg-violet-600 hover:bg-violet-500 text-sm rounded-lg disabled:opacity-40">
                                Oluştur
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

// ── Upload Job Row Component ──────────────────────────────────────────────

function UploadJobRow({ job, onDismiss }: { job: UploadJob; onDismiss: () => void }) {
    const isDone = job.status === "done" || job.status === "error" || job.status === "cancelled";

    const statusIcon = () => {
        switch (job.status) {
            case "hashing": return <Loader2 className="w-4 h-4 animate-spin text-zinc-400" />;
            case "uploading": case "completing": return <Loader2 className="w-4 h-4 animate-spin text-blue-400" />;
            case "done": return <CheckCircle2 className="w-4 h-4 text-emerald-400" />;
            case "error": return <AlertCircle className="w-4 h-4 text-red-400" />;
            case "cancelled": return <X className="w-4 h-4 text-zinc-500" />;
            default: return <Zap className="w-4 h-4 text-zinc-400" />;
        }
    };

    const statusText = () => {
        switch (job.status) {
            case "hashing": return "Hash hesaplanıyor...";
            case "uploading": return `Yükleniyor %${job.progress} · ${job.speedKbps > 1024 ? `${(job.speedKbps / 1024).toFixed(1)} MB/s` : `${job.speedKbps} KB/s`}`;
            case "completing": return "Tamamlanıyor...";
            case "done": return "Yükleme tamamlandı";
            case "error": return `Hata: ${job.error}`;
            case "cancelled": return "İptal edildi";
            default: return "";
        }
    };

    return (
        <div className="bg-zinc-900/60 border border-zinc-800 rounded-lg px-3 py-2">
            <div className="flex items-center gap-2">
                {statusIcon()}
                <span className="text-xs text-ms-text truncate flex-1">{job.fileName}</span>
                <span className="text-xs text-zinc-500 shrink-0">{statusText()}</span>
                {!isDone && job.cancel && (
                    <button onClick={job.cancel} className="p-0.5 hover:bg-zinc-700 rounded text-zinc-500 hover:text-red-400">
                        <X className="w-3.5 h-3.5" />
                    </button>
                )}
                {isDone && (
                    <button onClick={onDismiss} className="p-0.5 hover:bg-zinc-700 rounded text-zinc-500">
                        <X className="w-3.5 h-3.5" />
                    </button>
                )}
            </div>
            {job.status === "uploading" && (
                <div className="mt-1.5 h-1 bg-zinc-800 rounded-full overflow-hidden">
                    <div className="h-full bg-blue-500 rounded-full transition-all duration-300"
                        style={{ width: `${job.progress}%` }} />
                </div>
            )}
        </div>
    );
}

export default FileManagerPage;
