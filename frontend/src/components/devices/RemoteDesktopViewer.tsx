import React, { useEffect, useRef, useState } from 'react';
import { HubConnectionBuilder, HubConnection, HubConnectionState } from '@microsoft/signalr';
import { X, Wifi, WifiOff, Maximize2, Minimize2, Loader2 } from 'lucide-react';
import { API_BASE_URL } from '../../lib/apiClient';

interface Props {
    deviceId: string;
    isOpen: boolean;
    onClose: () => void;
}

const RemoteDesktopViewer: React.FC<Props> = ({ deviceId, isOpen, onClose }) => {
    const canvasRef = useRef<HTMLCanvasElement>(null);
    const [connection, setConnection] = useState<HubConnection | null>(null);
    const [status, setStatus] = useState<'connecting' | 'connected' | 'disconnected' | 'error'>('disconnected');
    const [isFullscreen, setIsFullscreen] = useState(false);
    const [fps, setFps] = useState(0);
    const frameCountRef = useRef(0);
    const [controlEnabled, setControlEnabled] = useState(false);
    const lastMouseMoveRef = useRef<number>(0);

    // FPS Meter
    useEffect(() => {
        const interval = setInterval(() => {
            setFps(frameCountRef.current);
            frameCountRef.current = 0;
        }, 1000);
        return () => clearInterval(interval);
    }, []);

    useEffect(() => {
        if (!isOpen) {
            if (connection) {
                connection.stop();
                setConnection(null);
            }
            return;
        }

        setStatus('connecting');

        const newConnection = new HubConnectionBuilder()
            .withUrl(`${API_BASE_URL}/hubs/desktop`)
            .withAutomaticReconnect()
            .build();

        newConnection.start()
            .then(() => {
                console.log("Connected to Remote Desktop Hub");
                setStatus('connected');
                // Join Session
                newConnection.invoke("JoinSession", deviceId);
            })
            .catch(err => {
                console.error("Connection failed: ", err);
                setStatus('error');
            });

        newConnection.on("ReceiveFrame", (base64Content: string) => {
            frameCountRef.current++;
            drawFrame(base64Content);
        });

        newConnection.onclose(() => setStatus('disconnected'));

        setConnection(newConnection);

        return () => {
            newConnection.stop();
        };
    }, [isOpen, deviceId]);

    // Input Handlers
    const sendInput = (type: string, data: any) => {
        if (!connection || connection.state !== HubConnectionState.Connected || !controlEnabled) return;

        const payload = {
            Type: ['MouseMove', 'MouseDown', 'MouseUp', 'KeyDown', 'KeyUp', 'Scroll'].indexOf(type),
            ...data
        };

        connection.invoke("SendInput", deviceId, payload)
            .catch(err => console.error("SendInput Error:", err));
    };

    const handleMouseMove = (e: React.MouseEvent) => {
        const now = Date.now();
        if (now - lastMouseMoveRef.current < 100) return; // Throttle 100ms
        lastMouseMoveRef.current = now;

        const rect = canvasRef.current?.getBoundingClientRect();
        if (!rect) return;

        const x = (e.clientX - rect.left) / rect.width;
        const y = (e.clientY - rect.top) / rect.height;

        sendInput('MouseMove', { X: x, Y: y, Button: 0 });
    };

    const handleMouseDown = (e: React.MouseEvent) => {
        const rect = canvasRef.current?.getBoundingClientRect();
        if (!rect) return;
        const x = (e.clientX - rect.left) / rect.width;
        const y = (e.clientY - rect.top) / rect.height;
        sendInput('MouseDown', { X: x, Y: y, Button: e.button });
    };

    const handleMouseUp = (e: React.MouseEvent) => {
        const rect = canvasRef.current?.getBoundingClientRect();
        if (!rect) return;
        const x = (e.clientX - rect.left) / rect.width;
        const y = (e.clientY - rect.top) / rect.height;
        sendInput('MouseUp', { X: x, Y: y, Button: e.button });
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
        sendInput('KeyDown', { Key: e.key });
        // e.preventDefault(); // Tarayıcı kısayollarını engellemek gerekebilir
    };

    const handleKeyUp = (e: React.KeyboardEvent) => {
        sendInput('KeyUp', { Key: e.key });
    };

    const drawFrame = (base64: string) => {
        const canvas = canvasRef.current;
        if (!canvas) return;

        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        const img = new Image();
        img.onload = () => {
            // Resize canvas if source size changes (optional, but good for sharpness)
            if (canvas.width !== img.width || canvas.height !== img.height) {
                canvas.width = img.width;
                canvas.height = img.height;
            }
            ctx.drawImage(img, 0, 0);
        };
        img.src = "data:image/jpeg;base64," + base64;
    };

    if (!isOpen) return null;

    const toggleFullscreen = () => {
        if (!document.fullscreenElement) {
            document.getElementById('rdp-container')?.requestFullscreen();
            setIsFullscreen(true);
        } else {
            document.exitFullscreen();
            setIsFullscreen(false);
        }
    };

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 backdrop-blur-sm p-4 animate-in fade-in duration-200">
            <div
                id="rdp-container"
                className={`bg-slate-900 border border-slate-700 rounded-xl overflow-hidden shadow-2xl flex flex-col transition-all duration-300 outline-none ${isFullscreen ? 'w-full h-full rounded-none' : 'w-full max-w-5xl max-h-[90vh]'
                    }`}
                tabIndex={0}
                onKeyDown={handleKeyDown}
                onKeyUp={handleKeyUp}
            >
                {/* Header */}
                <div className="flex items-center justify-between p-3 bg-slate-950 border-b border-slate-800">
                    <div className="flex items-center gap-3">
                        {/* Status Badges */}
                        <div className={`flex items-center gap-1.5 px-2 py-1 rounded-full text-xs font-mono border ${status === 'connected' ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' :
                            status === 'error' ? 'bg-rose-500/10 text-rose-400 border-rose-500/20' :
                                'bg-amber-500/10 text-amber-400 border-amber-500/20'
                            }`}>
                            {status === 'connected' ? <Wifi className="w-3 h-3" /> : <WifiOff className="w-3 h-3" />}
                            <span className="uppercase">{status}</span>
                        </div>
                        <span className="text-slate-400 text-sm font-medium">|</span>
                        <span className="text-slate-300 text-sm font-medium">{deviceId}</span>
                        <span className="text-slate-500 text-xs font-mono ml-2">{fps} FPS</span>
                    </div>

                    <div className="flex items-center gap-2">
                        {/* Control Toggle */}
                        <button
                            onClick={() => setControlEnabled(!controlEnabled)}
                            className={`px-3 py-1.5 rounded text-xs font-semibold transition-colors flex items-center gap-2 ${controlEnabled ? 'bg-rose-600 text-white hover:bg-rose-500' : 'bg-slate-800 text-slate-400 hover:bg-slate-700'}`}
                        >
                            {controlEnabled ? '🛑 Control ON' : '🖱️ Control OFF'}
                        </button>

                        <button
                            onClick={toggleFullscreen}
                            className="p-1.5 text-slate-400 hover:text-white hover:bg-slate-800 rounded transition-colors"
                            title="Toggle Fullscreen"
                        >
                            {isFullscreen ? <Minimize2 className="w-4 h-4" /> : <Maximize2 className="w-4 h-4" />}
                        </button>
                        <button
                            onClick={onClose}
                            className="p-1.5 text-slate-400 hover:text-rose-400 hover:bg-rose-500/10 rounded transition-colors"
                            title="Close Connection"
                        >
                            <X className="w-5 h-5" />
                        </button>
                    </div>
                </div>

                {/* Viewport */}
                <div
                    className="flex-1 bg-black relative flex items-center justify-center overflow-auto focus:outline-none"
                >
                    {/* States (Loading, Error) ... */}
                    {status === 'connecting' && (
                        <div className="absolute inset-0 flex flex-col items-center justify-center text-slate-500 gap-3">
                            <Loader2 className="w-10 h-10 animate-spin text-emerald-500" />
                            <p className="font-medium animate-pulse">Establishing secure connection...</p>
                        </div>
                    )}

                    {status === 'error' && (
                        <div className="absolute inset-0 flex flex-col items-center justify-center text-rose-500 gap-3">
                            <WifiOff className="w-10 h-10" />
                            <p>Connection failed. Is the Agent running?</p>
                        </div>
                    )}

                    <canvas
                        ref={canvasRef}
                        className="max-w-full max-h-full object-contain shadow-lg"
                        style={{ cursor: controlEnabled ? 'default' : 'zoom-in' }}
                        onMouseMove={handleMouseMove}
                        onMouseDown={handleMouseDown}
                        onMouseUp={handleMouseUp}
                    />
                </div>
            </div>
        </div>
    );
};

export default RemoteDesktopViewer;
