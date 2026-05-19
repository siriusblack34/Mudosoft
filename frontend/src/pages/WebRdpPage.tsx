import React, { useEffect, useRef, useState, useCallback } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { VncScreen } from "react-vnc";
import type { VncScreenHandle } from "react-vnc";
import { API_BASE_URL, apiClient } from "../lib/apiClient";
import {
    ArrowLeft, Maximize2, Minimize2,
    Clipboard, Loader2, Monitor,
    Camera, Settings, Upload, Download, Keyboard, Wrench,
} from "lucide-react";

// ─── Types ────────────────────────────────────────────────────────────────────

type Phase = "checking" | "connecting" | "connected" | "disconnected" | "error";

const PHASE_LABELS: Record<Phase, string> = {
    checking:     "Cihaz kontrol ediliyor...",
    connecting:   "VNC baglantisi kuruluyor...",
    connected:    "Bagli",
    disconnected: "Baglanti kesildi",
    error:        "Baglanti hatasi",
};

// ─── Helpers ──────────────────────────────────────────────────────────────────

function detectLeftMonitorWidth(totalW: number): number {
    const std = [1920, 1680, 1600, 1440, 1366, 1280, 1152, 1024, 800];
    for (const w of std) {
        const rem = totalW - w;
        if (rem > 0 && std.includes(rem)) return w;
    }
    return Math.round(totalW / 2);
}

function fmtTime(s: number): string {
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = s % 60;
    return [h, m, sec].map(v => String(v).padStart(2, "0")).join(":");
}

function pingColor(ms: number | null): string {
    if (ms === null) return "bg-gray-500";
    if (ms <= 50)   return "bg-emerald-400";
    if (ms <= 150)  return "bg-amber-400";
    return "bg-red-400";
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function WebRdpPage() {
    const { deviceId } = useParams<{ deviceId: string }>();
    const navigate = useNavigate();
    const vncRef = useRef<VncScreenHandle>(null);

    // Core state
    const [phase, setPhase] = useState<Phase>("checking");
    const [errorMsg, setErrorMsg] = useState("");
    const [deviceInfo, setDeviceInfo] = useState({ hostname: "", ipAddress: "" });
    const [isFullscreen, setIsFullscreen] = useState(false);
    const [wsUrl, setWsUrl] = useState("");
    const [ready, setReady] = useState(false);
    const [screenCount, setScreenCount] = useState(1);
    const [activeScreen, setActiveScreen] = useState(1);
    const [splitRatio, setSplitRatio] = useState(0.5);
    const [screenDetected, setScreenDetected] = useState(false);

    // Clipboard
    const [clipboardText, setClipboardText] = useState("");
    const [showClipboard, setShowClipboard] = useState(false);

    // Auto-reconnect
    const [retryKey, setRetryKey] = useState(0);
    const reconnectCountRef = useRef(0);
    const MAX_RECONNECT = 3;

    // VNC onar (agent uzerinden)
    const [repairing, setRepairing] = useState(false);
    const [repairMsg, setRepairMsg] = useState<string | null>(null);

    // Session timer
    const [elapsedSeconds, setElapsedSeconds] = useState(0);

    // Ping
    const [pingMs, setPingMs] = useState<number | null>(null);

    // Quality
    const [qualityLevel, setQualityLevel] = useState(6);
    const [compressionLevel, setCompressionLevel] = useState(2);
    const [showQualityPanel, setShowQualityPanel] = useState(false);

    // Keyboard layout toggle (Win+Space)
    const [kbLayout, setKbLayout] = useState<"TR" | "EN">("TR");

    // File transfer
    const [showFilePanel, setShowFilePanel] = useState(false);
    const [fileUpStatus, setFileUpStatus] = useState("");
    const [fileDownStatus, setFileDownStatus] = useState("");
    const [remoteUploadPath, setRemoteUploadPath] = useState("C:\\Temp\\");
    const [remoteDownloadPath, setRemoteDownloadPath] = useState("C:\\Temp\\filename.txt");
    const fileInputRef = useRef<HTMLInputElement>(null);

    // Idle timeout
    const idleTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const idleWarnTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const [showIdleWarning, setShowIdleWarning] = useState(false);
    const IDLE_WARN_MS      = 14 * 60 * 1000;
    const IDLE_DISCONNECT_MS = 15 * 60 * 1000;

    const isDual = screenCount >= 2 && phase === "connected";

    // ── Pre-check (re-runs on retryKey) ────────────────────────────────────
    useEffect(() => {
        if (!deviceId) return;
        let cancelled = false;
        async function check() {
            setReady(false);
            setScreenDetected(false);
            setScreenCount(1);
            setPhase("checking");
            try {
                const r = await apiClient.get<{
                    online: boolean; vncReachable: boolean; vncInstalled: boolean;
                    ipAddress: string; hostname: string;
                    activeSessionCount?: number; maxConnections?: number;
                }>(`/api/rdp/check/${deviceId}`);
                if (cancelled) return;
                setDeviceInfo({ hostname: r.hostname, ipAddress: r.ipAddress });
                if (!r.online)       { setPhase("error"); setErrorMsg("Cihaz cevrimdisi"); return; }
                if (!r.vncInstalled) { setPhase("error"); setErrorMsg("VNC kurulu degil."); return; }
                if (!r.vncReachable) { setPhase("error"); setErrorMsg("VNC portu (5900) erisilebilir degil"); return; }
                if ((r.activeSessionCount ?? 0) >= (r.maxConnections ?? 10)) {
                    setPhase("error"); setErrorMsg("Maksimum oturum sayisina ulasildi"); return;
                }
                const token = localStorage.getItem("token");
                if (!token) { setPhase("error"); setErrorMsg("Oturum bulunamadi"); return; }
                const wsProtocol = window.location.protocol === "https:" ? "wss:" : "ws:";
                const apiHost = API_BASE_URL.replace(/^https?:\/\//, "");
                setWsUrl(`${wsProtocol}//${apiHost}/ws/vnc?deviceId=${encodeURIComponent(deviceId!)}&access_token=${encodeURIComponent(token)}`);
                setPhase("connecting");
                setReady(true);
            } catch (err: unknown) {
                if (!cancelled) {
                    setPhase("error");
                    setErrorMsg((err instanceof Error ? err.message : null) || "Cihaz kontrolu basarisiz");
                }
            }
        }
        check();
        return () => { cancelled = true; };
    }, [deviceId, retryKey]);

    // ── Session timer ───────────────────────────────────────────────────────
    useEffect(() => {
        if (phase !== "connected") return;
        setElapsedSeconds(0);
        const t = setInterval(() => setElapsedSeconds(s => s + 1), 1000);
        return () => clearInterval(t);
    }, [phase]);

    // ── Ping indicator ──────────────────────────────────────────────────────
    useEffect(() => {
        if (phase !== "connected") return;
        let active = true;
        const measure = async () => {
            const t0 = performance.now();
            try {
                await apiClient.get("/api/rdp/ping", 5000);
                if (active) setPingMs(Math.round(performance.now() - t0));
            } catch { if (active) setPingMs(null); }
        };
        measure();
        const t = setInterval(measure, 5000);
        return () => { active = false; clearInterval(t); };
    }, [phase]);

    // ── Idle timeout ────────────────────────────────────────────────────────
    useEffect(() => {
        if (phase !== "connected") return;

        const resetIdle = () => {
            setShowIdleWarning(false);
            if (idleWarnTimerRef.current)   clearTimeout(idleWarnTimerRef.current);
            if (idleTimerRef.current)       clearTimeout(idleTimerRef.current);

            idleWarnTimerRef.current = setTimeout(() => setShowIdleWarning(true), IDLE_WARN_MS);
            idleTimerRef.current = setTimeout(() => {
                try { vncRef.current?.disconnect(); } catch { }
                setPhase("disconnected");
                setErrorMsg("15 dakika islem yapilmadigi icin baglanti kesildi.");
            }, IDLE_DISCONNECT_MS);
        };

        resetIdle();
        window.addEventListener("mousemove", resetIdle);
        window.addEventListener("keydown", resetIdle);

        return () => {
            if (idleWarnTimerRef.current)   clearTimeout(idleWarnTimerRef.current);
            if (idleTimerRef.current)       clearTimeout(idleTimerRef.current);
            window.removeEventListener("mousemove", resetIdle);
            window.removeEventListener("keydown", resetIdle);
        };
    }, [phase]);

    // ── Fullscreen listener ─────────────────────────────────────────────────
    useEffect(() => {
        const h = () => setIsFullscreen(!!document.fullscreenElement);
        document.addEventListener("fullscreenchange", h);
        return () => document.removeEventListener("fullscreenchange", h);
    }, []);

    // ── noVNC cursor fix ────────────────────────────────────────────────────
    useEffect(() => {
        if (phase !== "connected") return;
        const fix = () => {
            document.querySelectorAll<HTMLElement>(".vnc-inner canvas").forEach(el => {
                if (el.style.cursor === "none") el.style.cursor = "default";
            });
        };
        const observer = new MutationObserver(fix);
        const target = document.querySelector(".vnc-inner");
        if (target) observer.observe(target, { attributes: true, subtree: true, attributeFilter: ["style"] });
        fix();
        return () => observer.disconnect();
    }, [phase]);

    // ── Callbacks ───────────────────────────────────────────────────────────

    const goBack = useCallback(() => {
        try { vncRef.current?.disconnect(); } catch { }
        navigate(`/devices/${deviceId}`);
    }, [navigate, deviceId]);

    const toggleFullscreen = useCallback(() => {
        if (!document.fullscreenElement)
            document.documentElement.requestFullscreen().then(() => setIsFullscreen(true));
        else
            document.exitFullscreen().then(() => setIsFullscreen(false));
    }, []);

    const sendCAD     = useCallback(() => { vncRef.current?.sendCtrlAltDel(); }, []);
    const sendWinKey  = useCallback(() => {
        vncRef.current?.sendKey(0xFFEB, "MetaLeft", true);
        vncRef.current?.sendKey(0xFFEB, "MetaLeft", false);
    }, []);

    const sendWinSpace = useCallback(() => {
        // Win+Space → Windows keyboard layout switcher
        vncRef.current?.sendKey(0xFFEB, "MetaLeft", true);
        vncRef.current?.sendKey(0x0020, "Space",    true);
        vncRef.current?.sendKey(0x0020, "Space",    false);
        vncRef.current?.sendKey(0xFFEB, "MetaLeft", false);
        setKbLayout(k => k === "TR" ? "EN" : "TR");
    }, []);

    const takeScreenshot = useCallback(() => {
        const canvas = document.querySelector<HTMLCanvasElement>(".vnc-inner canvas");
        if (!canvas) return;
        const a = document.createElement("a");
        a.href = canvas.toDataURL("image/png");
        a.download = `screenshot-${deviceInfo.hostname || deviceId}-${Date.now()}.png`;
        a.click();
    }, [deviceInfo.hostname, deviceId]);

    // Poll until command result is written to DB (max 30s)
    const pollCommandResult = useCallback(async (commandId: string) => {
        const deadline = Date.now() + 30_000;
        while (Date.now() < deadline) {
            await new Promise(r => setTimeout(r, 500));
            try {
                const res = await apiClient.get<{ success: boolean; output: string }>(
                    `/api/agent/command-results/${commandId}`
                );
                return res;
            } catch { /* 404 = still pending */ }
        }
        throw new Error("Zaman asimi: Agent yanit vermedi.");
    }, []);

    const handleFileUpload = useCallback(async (file: File) => {
        if (!deviceId) return;
        setFileUpStatus("Yukleniyor...");
        try {
            const res = await apiClient.uploadFile(deviceId, file, remoteUploadPath) as { commandId: string };
            setFileUpStatus("Komut kuyruklandi, agent bekleniyor...");
            const result = await pollCommandResult(res.commandId);
            setFileUpStatus(result.success ? `Tamamlandi: ${result.output}` : `Hata: ${result.output}`);
        } catch (e: unknown) {
            setFileUpStatus(`Hata: ${e instanceof Error ? e.message : String(e)}`);
        }
    }, [deviceId, remoteUploadPath, pollCommandResult]);

    const handleFileDownload = useCallback(async () => {
        if (!deviceId || !remoteDownloadPath.trim()) return;
        setFileDownStatus("Indirme istegi gonderiliyor...");
        try {
            const res = await apiClient.post<{ commandId: string }>(
                `/api/agent/files/download?deviceId=${encodeURIComponent(deviceId)}&path=${encodeURIComponent(remoteDownloadPath)}`
            );
            setFileDownStatus("Agent dosyayi okuyor...");
            const result = await pollCommandResult(res.commandId);
            if (!result.success) { setFileDownStatus(`Hata: ${result.output}`); return; }
            // result.output is base64 file content
            const bytes = Uint8Array.from(atob(result.output), c => c.charCodeAt(0));
            const blob  = new Blob([bytes]);
            const fname = remoteDownloadPath.split(/[\\/]/).pop() ?? "download";
            const a = document.createElement("a");
            a.href = URL.createObjectURL(blob);
            a.download = fname;
            a.click();
            URL.revokeObjectURL(a.href);
            setFileDownStatus("Indirme tamamlandi.");
        } catch (e: unknown) {
            setFileDownStatus(`Hata: ${e instanceof Error ? e.message : String(e)}`);
        }
    }, [deviceId, remoteDownloadPath, pollCommandResult]);

    // ── Computed ────────────────────────────────────────────────────────────

    const isConnecting = phase === "checking" || phase === "connecting";
    const showOverlay  = isConnecting || phase === "error" || phase === "disconnected";

    // Dual-monitor CSS
    const innerStyle: React.CSSProperties = isDual
        ? {
            position: "absolute",
            top: 0, bottom: 0, left: 0,
            width: `${(1 / splitRatio) * 100}%`,
            transform: activeScreen === 2 ? `translateX(-${splitRatio * 100}%)` : "none",
            transition: "transform 0.15s ease",
          }
        : { position: "absolute", inset: 0 };

    // ── Close all panels when another opens ─────────────────────────────────
    const openClipboard = () => { setShowClipboard(v => !v); setShowQualityPanel(false); setShowFilePanel(false); };
    const openQuality   = () => { setShowQualityPanel(v => !v); setShowClipboard(false); setShowFilePanel(false); };
    const openFile      = () => { setShowFilePanel(v => !v); setShowClipboard(false); setShowQualityPanel(false); };

    // ── Render ───────────────────────────────────────────────────────────────

    return (
        <div className="fixed inset-0 bg-gray-950 flex flex-col z-50">

            {/* ── Toolbar ─────────────────────────────────────────────────── */}
            <div className="h-12 bg-gray-900/95 border-b border-white/10 flex items-center px-4 gap-2 shrink-0 overflow-x-auto">

                <button onClick={goBack}
                    className="p-1.5 hover:bg-gray-700/50 rounded-lg transition-colors shrink-0"
                    title="Geri">
                    <ArrowLeft className="w-4 h-4 text-gray-400" />
                </button>

                <div className="h-5 w-px bg-white/10 shrink-0" />

                {/* Device info */}
                <div className="flex items-center gap-2 text-sm shrink-0">
                    <span className="text-white font-medium">{deviceInfo.hostname || deviceId}</span>
                    {deviceInfo.ipAddress && <span className="text-gray-500">{deviceInfo.ipAddress}</span>}
                </div>

                {/* Connection status dot */}
                <div className="flex items-center gap-1.5 ml-1 shrink-0">
                    <div className={`w-2 h-2 rounded-full ${
                        phase === "connected" ? "bg-emerald-400 shadow-emerald-400/50 shadow-sm" :
                        phase === "error"     ? "bg-red-400" :
                        isConnecting         ? "bg-amber-400 animate-pulse" : "bg-gray-500"
                    }`} />
                    <span className={`text-xs ${
                        phase === "connected" ? "text-emerald-400" :
                        phase === "error"     ? "text-red-400" : "text-gray-400"
                    }`}>{PHASE_LABELS[phase]}</span>
                </div>

                <div className="flex-1 min-w-0" />

                {phase === "connected" && (<>
                    {/* Session timer */}
                    <span className="text-xs font-mono text-gray-400 tabular-nums shrink-0"
                        title="Oturum süresi">
                        {fmtTime(elapsedSeconds)}
                    </span>

                    {/* Ping */}
                    <div className="flex items-center gap-1 shrink-0" title={pingMs !== null ? `${pingMs}ms gecikme` : "Gecikme olculuyor"}>
                        <div className={`w-1.5 h-1.5 rounded-full ${pingColor(pingMs)}`} />
                        <span className="text-xs text-gray-500 tabular-nums w-10">
                            {pingMs !== null ? `${pingMs}ms` : "—"}
                        </span>
                    </div>

                    <div className="h-5 w-px bg-white/10 shrink-0" />

                    {/* Dual-monitor switcher */}
                    {isDual && (<>
                        <ToolbarButton onClick={() => setActiveScreen(1)} title="1. Ekran" active={activeScreen === 1}>
                            <Monitor className="w-3.5 h-3.5" /><span>1</span>
                        </ToolbarButton>
                        <ToolbarButton onClick={() => setActiveScreen(2)} title="2. Ekran" active={activeScreen === 2}>
                            <Monitor className="w-3.5 h-3.5" /><span>2</span>
                        </ToolbarButton>
                        <div className="h-5 w-px bg-white/10 mx-0.5 shrink-0" />
                    </>)}

                    <ToolbarButton onClick={sendCAD} title="Ctrl+Alt+Del">CAD</ToolbarButton>
                    <ToolbarButton onClick={sendWinKey} title="Windows Tusu">WIN</ToolbarButton>

                    {/* Keyboard layout toggle */}
                    <ToolbarButton onClick={sendWinSpace} title={`Klavye dili: ${kbLayout} (Win+Space)`}>
                        <Keyboard className="w-3 h-3" />
                        <span>{kbLayout}</span>
                    </ToolbarButton>

                    {/* Screenshot */}
                    <ToolbarButton onClick={takeScreenshot} title="Ekran goruntüsü al">
                        <Camera className="w-3.5 h-3.5" />
                    </ToolbarButton>

                    {/* Clipboard */}
                    <ToolbarButton onClick={openClipboard} title="Pano" active={showClipboard}>
                        <Clipboard className="w-3.5 h-3.5" />
                    </ToolbarButton>

                    {/* Quality */}
                    <ToolbarButton onClick={openQuality} title="Goruntu kalitesi" active={showQualityPanel}>
                        <Settings className="w-3.5 h-3.5" />
                    </ToolbarButton>

                    {/* File transfer */}
                    <ToolbarButton onClick={openFile} title="Dosya transfer" active={showFilePanel}>
                        <Upload className="w-3.5 h-3.5" />
                    </ToolbarButton>
                </>)}

                <ToolbarButton onClick={toggleFullscreen} title={isFullscreen ? "Kucult" : "Tam Ekran"}>
                    {isFullscreen ? <Minimize2 className="w-3.5 h-3.5" /> : <Maximize2 className="w-3.5 h-3.5" />}
                </ToolbarButton>

                <button onClick={goBack}
                    className="px-3 py-1 bg-red-600/80 hover:bg-red-500 text-white text-xs font-medium rounded-lg transition-colors shrink-0">
                    Baglantiyi Kes
                </button>
            </div>

            {/* ── Idle timeout warning ─────────────────────────────────────── */}
            {showIdleWarning && phase === "connected" && (
                <div className="absolute top-14 left-1/2 -translate-x-1/2 z-50 bg-amber-900/90 border border-amber-500/40 rounded-xl px-5 py-3 flex items-center gap-3 shadow-2xl">
                    <span className="text-amber-300 text-sm">1 dakika hareketsizlik — baglanti kesilecek</span>
                    <button onClick={() => setShowIdleWarning(false)}
                        className="px-3 py-1 bg-amber-600 hover:bg-amber-500 text-white text-xs rounded-lg">
                        Devam Et
                    </button>
                </div>
            )}

            {/* ── Clipboard panel ──────────────────────────────────────────── */}
            {showClipboard && phase === "connected" && (
                <div className="absolute top-14 right-4 z-50 bg-gray-800 border border-white/10 rounded-xl shadow-2xl p-4 w-80">
                    <div className="flex items-center justify-between mb-2">
                        <span className="text-sm font-medium text-white">Pano (Clipboard)</span>
                        <button onClick={() => setShowClipboard(false)} className="text-gray-400 hover:text-white text-xs">✕</button>
                    </div>
                    <textarea
                        value={clipboardText} onChange={e => setClipboardText(e.target.value)}
                        className="w-full h-24 bg-gray-900 border border-white/10 rounded-lg p-2 text-sm text-gray-300 resize-none focus:outline-none focus:border-blue-500"
                        placeholder="Uzak masaustunden kopyalanan metin burada gorunur..."
                    />
                    <div className="flex gap-2 mt-2">
                        <button onClick={async () => { try { setClipboardText(await navigator.clipboard.readText()); } catch { } }}
                            className="flex-1 px-2 py-1 bg-gray-700 hover:bg-gray-600 text-xs text-gray-300 rounded-lg transition-colors">
                            Yerel Panodan Al
                        </button>
                        <button onClick={() => { if (clipboardText) vncRef.current?.clipboardPaste(clipboardText); }}
                            className="flex-1 px-2 py-1 bg-blue-600 hover:bg-blue-500 text-xs text-white rounded-lg transition-colors">
                            Uzak Masaustune Gonder
                        </button>
                    </div>
                </div>
            )}

            {/* ── Quality panel ────────────────────────────────────────────── */}
            {showQualityPanel && phase === "connected" && (
                <div className="absolute top-14 right-4 z-50 bg-gray-800 border border-white/10 rounded-xl shadow-2xl p-4 w-72">
                    <div className="flex items-center justify-between mb-3">
                        <span className="text-sm font-medium text-white">Goruntu Kalitesi</span>
                        <button onClick={() => setShowQualityPanel(false)} className="text-gray-400 hover:text-white text-xs">✕</button>
                    </div>
                    <div className="space-y-4">
                        <div>
                            <div className="flex justify-between mb-1">
                                <label className="text-xs text-gray-400">Kalite (yuksek = daha net)</label>
                                <span className="text-xs text-gray-300 font-mono">{qualityLevel}</span>
                            </div>
                            <input type="range" min={0} max={9} value={qualityLevel}
                                onChange={e => setQualityLevel(+e.target.value)}
                                className="w-full accent-blue-500" />
                            <div className="flex justify-between text-xs text-gray-600 mt-0.5">
                                <span>Dusuk</span><span>Yuksek</span>
                            </div>
                        </div>
                        <div>
                            <div className="flex justify-between mb-1">
                                <label className="text-xs text-gray-400">Sikistirma (yuksek = daha az bant)</label>
                                <span className="text-xs text-gray-300 font-mono">{compressionLevel}</span>
                            </div>
                            <input type="range" min={0} max={9} value={compressionLevel}
                                onChange={e => setCompressionLevel(+e.target.value)}
                                className="w-full accent-blue-500" />
                            <div className="flex justify-between text-xs text-gray-600 mt-0.5">
                                <span>Az</span><span>Cok</span>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* ── File transfer panel ──────────────────────────────────────── */}
            {showFilePanel && phase === "connected" && (
                <div className="absolute top-14 right-4 z-50 bg-gray-800 border border-white/10 rounded-xl shadow-2xl p-4 w-96">
                    <div className="flex items-center justify-between mb-3">
                        <span className="text-sm font-medium text-white">Dosya Transfer</span>
                        <button onClick={() => setShowFilePanel(false)} className="text-gray-400 hover:text-white text-xs">✕</button>
                    </div>

                    {/* Upload */}
                    <div className="mb-4 pb-4 border-b border-white/10">
                        <p className="text-xs text-gray-400 mb-2 font-medium uppercase tracking-wide">
                            <Upload className="w-3 h-3 inline mr-1" />Yukle (Bilgisayar → Cihaz)
                        </p>
                        <input
                            value={remoteUploadPath}
                            onChange={e => setRemoteUploadPath(e.target.value)}
                            placeholder="C:\Temp\"
                            className="w-full bg-gray-900 border border-white/10 rounded-lg px-3 py-1.5 text-xs text-gray-300 focus:outline-none focus:border-blue-500 mb-2"
                        />
                        <input ref={fileInputRef} type="file" hidden
                            onChange={e => { const f = e.target.files?.[0]; if (f) handleFileUpload(f); e.target.value = ""; }}
                        />
                        <button onClick={() => fileInputRef.current?.click()}
                            className="w-full px-3 py-1.5 bg-blue-600 hover:bg-blue-500 text-white text-xs rounded-lg transition-colors">
                            Dosya Sec ve Yukle
                        </button>
                        {fileUpStatus && (
                            <p className="text-xs mt-1.5 text-gray-400">{fileUpStatus}</p>
                        )}
                    </div>

                    {/* Download */}
                    <div>
                        <p className="text-xs text-gray-400 mb-2 font-medium uppercase tracking-wide">
                            <Download className="w-3 h-3 inline mr-1" />Indir (Cihaz → Bilgisayar)
                        </p>
                        <input
                            value={remoteDownloadPath}
                            onChange={e => setRemoteDownloadPath(e.target.value)}
                            placeholder="C:\Temp\dosya.txt"
                            className="w-full bg-gray-900 border border-white/10 rounded-lg px-3 py-1.5 text-xs text-gray-300 focus:outline-none focus:border-blue-500 mb-2"
                        />
                        <button onClick={handleFileDownload}
                            className="w-full px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white text-xs rounded-lg transition-colors">
                            Dosyayi Indir
                        </button>
                        {fileDownStatus && (
                            <p className="text-xs mt-1.5 text-gray-400">{fileDownStatus}</p>
                        )}
                    </div>
                </div>
            )}

            {/* ── VNC Display ──────────────────────────────────────────────── */}
            <div className="flex-1 overflow-hidden bg-black relative">
                <div className="vnc-inner" style={innerStyle}>
                    {ready && wsUrl && (
                        <VncScreen
                            ref={vncRef}
                            url={wsUrl}
                            scaleViewport
                            background="#000000"
                            style={{ width: "100%", height: "100%" }}
                            rfbOptions={{ wsProtocols: ["binary"] }}
                            qualityLevel={qualityLevel}
                            compressionLevel={compressionLevel}
                            onConnect={() => {
                                reconnectCountRef.current = 0;
                                setPhase("connected");
                                // Dual-monitor detection
                                let attempts = 0;
                                const detect = () => {
                                    const rfb = (vncRef.current as any)?.rfb;
                                    const fbW: number = rfb?._display?.width  ?? rfb?._display?._fbWidth  ?? 0;
                                    const fbH: number = rfb?._display?.height ?? rfb?._display?._fbHeight ?? 0;
                                    if ((!fbW || !fbH) && attempts++ < 20) { setTimeout(detect, 100); return; }
                                    if (fbW > 0 && fbH > 0 && fbW / fbH > 1.8) {
                                        const leftW = detectLeftMonitorWidth(fbW);
                                        setSplitRatio(leftW / fbW);
                                        setScreenCount(2);
                                    }
                                    setScreenDetected(true);
                                };
                                setTimeout(detect, 150);
                            }}
                            onDisconnect={(e: any) => {
                                if (e?.detail?.clean) {
                                    reconnectCountRef.current = 0;
                                    setPhase("disconnected");
                                } else if (reconnectCountRef.current < MAX_RECONNECT) {
                                    reconnectCountRef.current++;
                                    setPhase("connecting");
                                    setTimeout(() => setRetryKey(k => k + 1), 3000);
                                } else {
                                    reconnectCountRef.current = 0;
                                    setPhase("error");
                                    setErrorMsg("Baglanti defalarca kesildi. Lutfen tekrar deneyin.");
                                }
                            }}
                            onSecurityFailure={(e: any) => {
                                setPhase("error");
                                setErrorMsg(`Kimlik dogrulama hatasi: ${e?.detail?.reason || "Bilinmeyen"}`);
                            }}
                            onClipboard={(e: any) => { if (e?.detail?.text) setClipboardText(e.detail.text); }}
                        />
                    )}
                </div>

                {/* Screen detection spinner */}
                {phase === "connected" && !screenDetected && (
                    <div className="absolute inset-0 bg-black flex items-center justify-center z-10">
                        <Loader2 className="w-8 h-8 text-gray-500 animate-spin" />
                    </div>
                )}
            </div>

            {/* ── Connection overlay ───────────────────────────────────────── */}
            {showOverlay && (
                <div className="absolute inset-0 top-12 bg-gray-950/90 backdrop-blur-sm flex items-center justify-center z-40">
                    <div className="bg-gray-800/90 border border-white/10 rounded-2xl p-8 max-w-md w-full mx-4 shadow-2xl">
                        <h2 className="text-lg font-semibold text-white mb-6 text-center">
                            {phase === "error" || phase === "disconnected" ? "Baglanti Durumu" : "Web RDP Baglantisi"}
                        </h2>
                        <div className="space-y-4 mb-6">
                            <ProgressStep label="Cihaz Kontrolu" status={
                                phase === "checking" ? "active" :
                                phase === "error" && errorMsg.includes("cevrimdisi") ? "error" : "done"
                            } />
                            <ProgressStep label={
                                reconnectCountRef.current > 0
                                    ? `VNC Baglantisi (Deneme ${reconnectCountRef.current}/${MAX_RECONNECT})`
                                    : "VNC Baglantisi"
                            } status={
                                phase === "checking" ? "pending" : phase === "connecting" ? "active" :
                                phase === "error" && !errorMsg.includes("cevrimdisi") ? "error" : "done"
                            } />
                        </div>
                        {(phase === "error" || phase === "disconnected") && (
                            <div className="space-y-4">
                                {errorMsg && (
                                    <div className="bg-red-500/10 border border-red-500/20 rounded-xl p-3 text-center">
                                        <p className="text-sm text-red-400">{errorMsg}</p>
                                    </div>
                                )}
                                {repairMsg && (
                                    <div className="bg-emerald-500/10 border border-emerald-500/20 rounded-xl p-3 max-h-72 overflow-auto">
                                        <pre className="text-[11px] text-emerald-200 font-mono whitespace-pre-wrap text-left">{repairMsg}</pre>
                                    </div>
                                )}
                                {/* VNC port erisilemiyorsa onar butonunu goster */}
                                {(errorMsg.toLowerCase().includes("vnc port") || errorMsg.toLowerCase().includes("erisilebilir")) && deviceId && (
                                    <button
                                        onClick={async () => {
                                            setRepairing(true); setRepairMsg("Onar komutu agent'a gonderildi, sonuc bekleniyor...");
                                            try {
                                                const resp = await apiClient.post<{ commandId: string }>(`/api/agent/vnc-install/${deviceId}`, {});
                                                const commandId = resp?.commandId;
                                                const deadline = Date.now() + 90_000;
                                                let result: { success: boolean; output: string } | null = null;
                                                while (Date.now() < deadline) {
                                                    await new Promise(r => setTimeout(r, 3000));
                                                    try {
                                                        const r = await apiClient.get<{ success: boolean; output: string }>(`/api/agent/command-results/${commandId}`);
                                                        if (r && (r as any).output) { result = r; break; }
                                                    } catch { /* 404 = hala bekliyor */ }
                                                }
                                                if (result) {
                                                    setRepairMsg((result.success ? "Onar OK: " : "Onar BASARISIZ: ") + result.output);
                                                } else {
                                                    setRepairMsg("90sn icinde sonuc dönmedi. Cihaz offline olabilir.");
                                                }
                                            } catch (e: any) {
                                                setRepairMsg("Onar komutu yollanamadi: " + (e?.message || "bilinmeyen hata"));
                                            } finally {
                                                setRepairing(false);
                                            }
                                        }}
                                        disabled={repairing}
                                        className="w-full px-4 py-2 bg-amber-600 hover:bg-amber-500 disabled:opacity-50 text-white text-sm font-medium rounded-xl transition-colors flex items-center justify-center gap-2"
                                    >
                                        {repairing ? <Loader2 className="w-4 h-4 animate-spin" /> : <Wrench className="w-4 h-4" />}
                                        VNC Onar (agent uzerinden)
                                    </button>
                                )}
                                <div className="flex gap-2">
                                    <button onClick={() => { reconnectCountRef.current = 0; setRetryKey(k => k + 1); setRepairMsg(null); }}
                                        className="flex-1 px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white text-sm font-medium rounded-xl transition-colors">
                                        Tekrar Dene
                                    </button>
                                    <button onClick={goBack}
                                        className="flex-1 px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white text-sm font-medium rounded-xl transition-colors">
                                        Geri Don
                                    </button>
                                </div>
                            </div>
                        )}
                    </div>
                </div>
            )}
        </div>
    );
}

// ─── Sub-components ───────────────────────────────────────────────────────────

function ToolbarButton({ onClick, title, children, active }: {
    onClick: () => void; title: string; children: React.ReactNode; active?: boolean;
}) {
    return (
        <button onClick={onClick} title={title}
            className={`px-2 py-1 text-xs font-medium rounded-lg border transition-colors flex items-center gap-1 shrink-0 ${
                active
                    ? "bg-blue-600 border-blue-500 text-white"
                    : "bg-gray-800 hover:bg-gray-700 text-gray-300 border-white/10"
            }`}>
            {children}
        </button>
    );
}

function ProgressStep({ label, status }: { label: string; status: "pending" | "active" | "done" | "error" }) {
    return (
        <div className="flex items-center gap-3">
            <div className="w-6 h-6 flex items-center justify-center shrink-0">
                {status === "active"  && <Loader2 className="w-5 h-5 text-blue-400 animate-spin" />}
                {status === "done"    && <div className="w-5 h-5 rounded-full bg-emerald-500/20 border border-emerald-500/50 flex items-center justify-center"><span className="text-emerald-400 text-xs">✓</span></div>}
                {status === "error"   && <div className="w-5 h-5 rounded-full bg-red-500/20 border border-red-500/50 flex items-center justify-center"><span className="text-red-400 text-xs">✕</span></div>}
                {status === "pending" && <div className="w-5 h-5 rounded-full border border-gray-600" />}
            </div>
            <span className={`text-sm ${
                status === "active"  ? "text-blue-400 font-medium" :
                status === "done"    ? "text-emerald-400" :
                status === "error"   ? "text-red-400" : "text-gray-500"
            }`}>{label}</span>
        </div>
    );
}
