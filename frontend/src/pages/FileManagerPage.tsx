import React, { useState, useEffect, useRef } from "react";
import { useParams, useNavigate } from "react-router-dom";
import {
    ArrowLeft, Folder, FolderOpen, File, Upload, Download, Trash2,
    FolderPlus, RefreshCcw, ChevronRight, Home, AlertCircle
} from "lucide-react";
import { apiClient } from "../lib/apiClient";

interface FileItem {
    name: string;
    fullPath: string;
    isDirectory: boolean;
    sizeBytes: number;
    lastModified: string;
}

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

    const pollRef = useRef<NodeJS.Timeout | null>(null);
    const commandIdRef = useRef<string | null>(null);

    const loadDirectory = async (path: string) => {
        if (!deviceId) return;
        setLoading(true);
        setError(null);
        setFiles([]);
        setSelectedFile(null);

        try {
            const res = await apiClient.post<{ commandId: string }>(
                `/api/agent/files/list?deviceId=${deviceId}&path=${encodeURIComponent(path)}`
            );
            commandIdRef.current = res.commandId;

            // Poll for result
            let attempts = 0;
            pollRef.current = setInterval(async () => {
                attempts++;
                try {
                    const result = await apiClient.get<any>(
                        `/api/agent/command-results/${commandIdRef.current}`
                    );

                    if (result && result.success) {
                        if (pollRef.current) clearInterval(pollRef.current);
                        setLoading(false);

                        try {
                            const parsed = JSON.parse(result.output);
                            const items = Array.isArray(parsed) ? parsed : [];
                            // Sort: directories first, then by name
                            items.sort((a: FileItem, b: FileItem) => {
                                if (a.isDirectory !== b.isDirectory) {
                                    return a.isDirectory ? -1 : 1;
                                }
                                return a.name.localeCompare(b.name);
                            });
                            setFiles(items);
                            setCurrentPath(path);
                        } catch {
                            setError("Failed to parse file list");
                        }
                    } else if (result && !result.success && result.output) {
                        if (pollRef.current) clearInterval(pollRef.current);
                        setLoading(false);
                        setError(result.output);
                    }
                } catch (err: any) {
                    // 404 means still pending
                    if (err.message?.includes('404')) {
                        // continue polling
                    } else {
                        console.error("Polling error:", err);
                    }
                }

                if (attempts >= 30) {
                    if (pollRef.current) clearInterval(pollRef.current);
                    setLoading(false);
                    setError("Timeout waiting for file list");
                }
            }, 1000);

        } catch (err: any) {
            console.error("Failed to list directory:", err);
            setError("Failed to send command");
            setLoading(false);
        }
    };

    useEffect(() => {
        loadDirectory(currentPath);
        return () => {
            if (pollRef.current) clearInterval(pollRef.current);
        };
    }, [deviceId]);

    const navigateTo = (path: string) => {
        loadDirectory(path);
    };

    const navigateUp = () => {
        const parts = currentPath.split("\\").filter(p => p);
        if (parts.length > 1) {
            parts.pop();
            navigateTo(parts.join("\\") + "\\");
        } else if (parts.length === 1) {
            navigateTo(parts[0] + "\\");
        }
    };

    const handleItemClick = (item: FileItem) => {
        if (item.isDirectory) {
            navigateTo(item.fullPath + "\\");
        } else {
            setSelectedFile(item);
        }
    };

    const handleCreateFolder = async () => {
        if (!deviceId || !newFolderName.trim()) return;

        const folderPath = currentPath + newFolderName.trim();
        try {
            await apiClient.post(`/api/agent/files/mkdir?deviceId=${deviceId}&path=${encodeURIComponent(folderPath)}`);
            setShowNewFolderDialog(false);
            setNewFolderName("");
            setTimeout(() => loadDirectory(currentPath), 1500);
        } catch (err) {
            console.error("Failed to create folder:", err);
        }
    };

    const handleDelete = async (item: FileItem) => {
        if (!deviceId) return;
        const confirmed = window.confirm(`Delete "${item.name}"? ${item.isDirectory ? 'This will delete all contents!' : ''}`);
        if (!confirmed) return;

        try {
            await apiClient.delete(`/api/agent/files?deviceId=${deviceId}&path=${encodeURIComponent(item.fullPath)}`);
            setTimeout(() => loadDirectory(currentPath), 1500);
        } catch (err) {
            console.error("Failed to delete:", err);
        }
    };

    const handleDownload = async (item: FileItem) => {
        if (!deviceId || item.isDirectory) return;

        try {
            const res = await apiClient.post<{ commandId: string }>(
                `/api/agent/files/download?deviceId=${deviceId}&path=${encodeURIComponent(item.fullPath)}`
            );

            // Poll for result
            const checkResult = async () => {
                for (let i = 0; i < 30; i++) {
                    await new Promise(r => setTimeout(r, 1000));
                    try {
                        const result = await apiClient.get<any>(`/api/agent/command-results/${res.commandId}`);
                        if (result && result.success) {
                            // Decode base64 and download
                            const bytes = atob(result.output);
                            const arr = new Uint8Array(bytes.length);
                            for (let j = 0; j < bytes.length; j++) {
                                arr[j] = bytes.charCodeAt(j);
                            }
                            const blob = new Blob([arr]);
                            const url = URL.createObjectURL(blob);
                            const a = document.createElement('a');
                            a.href = url;
                            a.download = item.name;
                            a.click();
                            URL.revokeObjectURL(url);
                            return;
                        }
                    } catch { }
                }
                alert("Download timeout");
            };
            checkResult();
        } catch (err) {
            console.error("Failed to download:", err);
        }
    };

    const handleUpload = async (event: React.ChangeEvent<HTMLInputElement>) => {
        if (!deviceId || !event.target.files?.length) return;
        const file = event.target.files[0];

        const reader = new FileReader();
        reader.onload = async () => {
            const base64 = (reader.result as string).split(',')[1] || '';
            const targetPath = currentPath + file.name;

            try {
                await apiClient.post(`/api/agent/files/upload?deviceId=${deviceId}`, {
                    path: targetPath,
                    content: base64
                });
                setTimeout(() => loadDirectory(currentPath), 1500);
            } catch (err) {
                console.error("Failed to upload:", err);
            }
        };
        reader.readAsDataURL(file);
    };

    const formatSize = (bytes: number) => {
        if (bytes === 0) return "";
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
        return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
    };

    const formatDate = (isoString: string) => {
        try {
            return new Date(isoString).toLocaleString();
        } catch {
            return "-";
        }
    };

    // Build breadcrumb parts
    const pathParts = currentPath.split("\\").filter(p => p);

    if (!deviceId) {
        return <div className="p-4 text-red-500">Device ID not found</div>;
    }

    return (
        <div className="space-y-4 p-4">
            {/* Header */}
            <div className="flex items-center gap-4">
                <button
                    onClick={() => navigate(`/devices/${deviceId}`)}
                    className="p-2 hover:bg-slate-700 rounded-lg transition-colors"
                >
                    <ArrowLeft className="w-5 h-5" />
                </button>
                <div className="flex items-center gap-2">
                    <FolderOpen className="w-6 h-6 text-violet-400" />
                    <h1 className="text-2xl font-semibold">File Manager</h1>
                </div>
            </div>

            {/* Toolbar */}
            <div className="flex flex-wrap items-center gap-2 bg-slate-800/50 p-3 rounded-xl border border-slate-700">
                {/* Breadcrumb */}
                <button onClick={() => navigateTo("C:\\")} className="p-1 hover:bg-slate-700 rounded">
                    <Home className="w-4 h-4" />
                </button>
                <button onClick={navigateUp} className="p-1 hover:bg-slate-700 rounded text-sm">
                    ..
                </button>

                <div className="flex items-center gap-1 flex-1 overflow-x-auto">
                    {pathParts.map((part, i) => (
                        <React.Fragment key={i}>
                            <ChevronRight className="w-4 h-4 text-slate-500 shrink-0" />
                            <button
                                onClick={() => navigateTo(pathParts.slice(0, i + 1).join("\\") + "\\")}
                                className="text-sm hover:text-violet-400 whitespace-nowrap"
                            >
                                {part}
                            </button>
                        </React.Fragment>
                    ))}
                </div>

                <button onClick={() => loadDirectory(currentPath)} className="p-2 hover:bg-slate-700 rounded-lg">
                    <RefreshCcw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
                </button>
                <button onClick={() => setShowNewFolderDialog(true)} className="p-2 hover:bg-slate-700 rounded-lg" title="New Folder">
                    <FolderPlus className="w-4 h-4" />
                </button>
                <label className="p-2 hover:bg-slate-700 rounded-lg cursor-pointer" title="Upload File">
                    <Upload className="w-4 h-4" />
                    <input type="file" className="hidden" onChange={handleUpload} />
                </label>
            </div>

            {/* File List */}
            <div className="bg-ms-panel rounded-2xl border border-ms-border shadow-lg overflow-hidden">
                {/* Header Row */}
                <div className="grid grid-cols-12 gap-2 px-4 py-2 bg-slate-800/50 text-xs text-slate-400 font-medium uppercase tracking-wider">
                    <div className="col-span-6">Name</div>
                    <div className="col-span-2">Size</div>
                    <div className="col-span-3">Modified</div>
                    <div className="col-span-1">Actions</div>
                </div>

                {/* Loading */}
                {loading && (
                    <div className="p-8 text-center text-slate-400">
                        <RefreshCcw className="w-6 h-6 animate-spin mx-auto mb-2" />
                        Loading directory...
                    </div>
                )}

                {/* Error */}
                {error && !loading && (
                    <div className="p-8 text-center text-red-400">
                        <AlertCircle className="w-8 h-8 mx-auto mb-2" />
                        {error}
                    </div>
                )}

                {/* File Items */}
                {!loading && !error && (
                    <div className="max-h-[60vh] overflow-y-auto">
                        {files.map((item, i) => (
                            <div
                                key={i}
                                className={`grid grid-cols-12 gap-2 px-4 py-2 hover:bg-slate-800/30 border-b border-slate-700/50 cursor-pointer ${selectedFile?.fullPath === item.fullPath ? 'bg-violet-900/20' : ''}`}
                                onClick={() => handleItemClick(item)}
                            >
                                <div className="col-span-6 flex items-center gap-2 truncate">
                                    {item.isDirectory ? (
                                        <Folder className="w-4 h-4 text-amber-400 shrink-0" />
                                    ) : (
                                        <File className="w-4 h-4 text-slate-400 shrink-0" />
                                    )}
                                    <span className="truncate" title={item.name}>{item.name}</span>
                                </div>
                                <div className="col-span-2 text-slate-400 text-sm">
                                    {formatSize(item.sizeBytes)}
                                </div>
                                <div className="col-span-3 text-slate-400 text-xs">
                                    {formatDate(item.lastModified)}
                                </div>
                                <div className="col-span-1 flex gap-1">
                                    {!item.isDirectory && (
                                        <button
                                            onClick={(e) => { e.stopPropagation(); handleDownload(item); }}
                                            className="p-1 hover:bg-slate-700 rounded"
                                            title="Download"
                                        >
                                            <Download className="w-3 h-3" />
                                        </button>
                                    )}
                                    <button
                                        onClick={(e) => { e.stopPropagation(); handleDelete(item); }}
                                        className="p-1 hover:bg-red-900/50 rounded text-red-400"
                                        title="Delete"
                                    >
                                        <Trash2 className="w-3 h-3" />
                                    </button>
                                </div>
                            </div>
                        ))}

                        {files.length === 0 && !loading && !error && (
                            <div className="p-8 text-center text-slate-500">
                                <Folder className="w-12 h-12 mx-auto mb-3 opacity-50" />
                                <p>Empty directory</p>
                            </div>
                        )}
                    </div>
                )}
            </div>

            {/* New Folder Dialog */}
            {showNewFolderDialog && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                    <div className="bg-slate-800 p-6 rounded-xl border border-slate-700 w-96">
                        <h3 className="text-lg font-medium mb-4">Create New Folder</h3>
                        <input
                            type="text"
                            value={newFolderName}
                            onChange={(e) => setNewFolderName(e.target.value)}
                            placeholder="Folder name"
                            className="w-full bg-slate-900 border border-slate-600 rounded-lg px-4 py-2 mb-4 focus:outline-none focus:border-violet-500"
                            autoFocus
                        />
                        <div className="flex justify-end gap-2">
                            <button
                                onClick={() => { setShowNewFolderDialog(false); setNewFolderName(""); }}
                                className="px-4 py-2 bg-slate-700 hover:bg-slate-600 rounded-lg"
                            >
                                Cancel
                            </button>
                            <button
                                onClick={handleCreateFolder}
                                disabled={!newFolderName.trim()}
                                className="px-4 py-2 bg-violet-600 hover:bg-violet-500 rounded-lg disabled:opacity-50"
                            >
                                Create
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default FileManagerPage;
