import React, { useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { HubConnectionBuilder, HubConnection, HubConnectionState } from '@microsoft/signalr';
import { Wifi, WifiOff, Loader2, MousePointer2, Keyboard, Command, AlertTriangle } from 'lucide-react';
import { API_BASE_URL } from '../lib/apiClient';

const RemoteDesktopPage: React.FC = () => {
    const { deviceId } = useParams<{ deviceId: string }>();
    const canvasRef = useRef<HTMLCanvasElement>(null);
    const [connection, setConnection] = useState<HubConnection | null>(null);
    const [status, setStatus] = useState<'connecting' | 'connected' | 'disconnected' | 'error'>('connecting');
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

    // Connection Logic
    useEffect(() => {
        if (!deviceId) return;

        const newConnection = new HubConnectionBuilder()
            .withUrl(`${API_BASE_URL}/hubs/desktop`)
            .withAutomaticReconnect()
            .build();

        newConnection.start()
            .then(() => {
                console.log("Connected to Remote Desktop Hub");
                setStatus('connected');
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
    }, [deviceId]);

    // Helpers
    const drawFrame = (base64: string) => {
        const canvas = canvasRef.current;
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        const img = new Image();
        img.onload = () => {
            if (canvas.width !== img.width || canvas.height !== img.height) {
                canvas.width = img.width;
                canvas.height = img.height;
            }
            ctx.drawImage(img, 0, 0);
        };
        img.src = "data:image/jpeg;base64," + base64;
    };

    const sendInput = (type: string, data: any) => {
        if (!connection || connection.state !== HubConnectionState.Connected || !controlEnabled) return;

        const payload = {
            Type: ['MouseMove', 'MouseDown', 'MouseUp', 'KeyDown', 'KeyUp', 'Scroll'].indexOf(type),
            ...data
        };
        connection.invoke("SendInput", deviceId, payload).catch(err => console.error(err));
    };

    // Events
    const handleMouseMove = (e: React.MouseEvent) => {
        const now = Date.now();
        if (now - lastMouseMoveRef.current < 10) return; // 10ms throttle
        lastMouseMoveRef.current = now;
        const rect = canvasRef.current?.getBoundingClientRect();
        if (!rect) return;
        sendInput('MouseMove', { X: (e.clientX - rect.left) / rect.width, Y: (e.clientY - rect.top) / rect.height, Button: 0 });
    };

    const handleMouseDown = (e: React.MouseEvent) => {
        const rect = canvasRef.current?.getBoundingClientRect();
        if (!rect) return;
        sendInput('MouseDown', { X: (e.clientX - rect.left) / rect.width, Y: (e.clientY - rect.top) / rect.height, Button: e.button });
    };

    const handleMouseUp = (e: React.MouseEvent) => {
        const rect = canvasRef.current?.getBoundingClientRect();
        if (!rect) return;
        sendInput('MouseUp', { X: (e.clientX - rect.left) / rect.width, Y: (e.clientY - rect.top) / rect.height, Button: e.button });
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
        sendInput('KeyDown', { Key: e.key, Ctrl: e.ctrlKey, Alt: e.altKey, Shift: e.shiftKey });
        e.preventDefault();
    };

    const handleKeyUp = (e: React.KeyboardEvent) => {
        sendInput('KeyUp', { Key: e.key, Ctrl: e.ctrlKey, Alt: e.altKey, Shift: e.shiftKey });
        e.preventDefault();
    };

    // Special Keys
    const sendSpecialKey = (key: string) => {
        if (!controlEnabled) return;
        // Simulate press and release
        sendInput('KeyDown', { Key: key });
        setTimeout(() => sendInput('KeyUp', { Key: key }), 100);
    };

    return (
        <div
            className="w-screen h-screen bg-black flex flex-col overflow-hidden outline-none"
            tabIndex={0}
            onKeyDown={handleKeyDown}
            onKeyUp={handleKeyUp}
        >
            {/* Toolbar */}
            <div className="h-12 bg-slate-900 border-b border-slate-700 flex items-center justify-between px-4 shrink-0 shadow-md z-10">
                <div className="flex items-center gap-3">
                    <div className={`w-2 h-2 rounded-full ${status === 'connected' ? 'bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.5)]' : 'bg-rose-500'}`} />
                    <span className="text-slate-200 font-semibold tracking-wide text-sm">{deviceId}</span>
                    <span className="text-slate-500 text-xs font-mono bg-slate-800 px-2 py-0.5 rounded">{fps} FPS</span>
                </div>

                <div className="flex items-center gap-2">
                    {/* Special Keys Group */}
                    <div className="flex items-center bg-slate-800 rounded-lg p-1 gap-1 mr-4 border border-slate-700">
                        <button onClick={() => sendSpecialKey("Control+Alt+Delete")} className="px-3 py-1 text-xs font-mono text-slate-300 hover:bg-slate-700 hover:text-white rounded transition-colors" disabled={!controlEnabled} title="Send Ctrl+Alt+Del">
                            CAD
                        </button>
                        <button onClick={() => sendSpecialKey("Meta")} className="px-3 py-1 text-xs font-mono text-slate-300 hover:bg-slate-700 hover:text-white rounded transition-colors" disabled={!controlEnabled} title="Windows Key">
                            WIN
                        </button>
                        <button onClick={() => sendSpecialKey("Escape")} className="px-3 py-1 text-xs font-mono text-slate-300 hover:bg-slate-700 hover:text-white rounded transition-colors" disabled={!controlEnabled}>
                            ESC
                        </button>
                        <button onClick={() => sendSpecialKey("Tab")} className="px-3 py-1 text-xs font-mono text-slate-300 hover:bg-slate-700 hover:text-white rounded transition-colors" disabled={!controlEnabled}>
                            TAB
                        </button>
                    </div>

                    <button
                        onClick={() => setControlEnabled(!controlEnabled)}
                        className={`px-4 py-1.5 rounded-md text-sm font-bold transition-all flex items-center gap-2 shadow-sm ${controlEnabled
                            ? 'bg-rose-600 text-white hover:bg-rose-500 ring-2 ring-rose-500/20'
                            : 'bg-emerald-600 text-white hover:bg-emerald-500 ring-2 ring-emerald-500/20'
                            }`}
                    >
                        {controlEnabled ? <Keyboard className="w-4 h-4" /> : <MousePointer2 className="w-4 h-4" />}
                        {controlEnabled ? 'RELEASE CONTROL' : 'TAKE CONTROL'}
                    </button>
                </div>
            </div>

            {/* Canvas Area */}
            <div className="flex-1 relative flex items-center justify-center bg-zinc-900/50">
                {status === 'connecting' && (
                    <div className="absolute flex flex-col items-center gap-2 text-emerald-500">
                        <Loader2 className="w-8 h-8 animate-spin" />
                        <span className="text-sm font-medium">Connecting to secure stream...</span>
                    </div>
                )}

                <canvas
                    ref={canvasRef}
                    className="max-w-full max-h-full object-contain shadow-2xl"
                    style={{ cursor: 'default' }}
                    onMouseMove={handleMouseMove}
                    onMouseDown={handleMouseDown}
                    onMouseUp={handleMouseUp}
                    onContextMenu={(e) => e.preventDefault()}
                />
            </div>
        </div>
    );
};

export default RemoteDesktopPage;
