import React, { useEffect, useRef, useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { HubConnectionBuilder, HubConnection, HubConnectionState } from '@microsoft/signalr';
import { Wifi, WifiOff, Loader2, MousePointer2, Keyboard, Video, AlertTriangle } from 'lucide-react';
import { API_BASE_URL } from '../lib/apiClient';

// ICE Servers for WebRTC (STUN + TURN on backend)
const ICE_SERVERS: RTCIceServer[] = [
    { urls: 'stun:stun.l.google.com:19302' },
    // TURN server on backend
    {
        urls: 'turn:10.0.213.89:3478',
        username: 'mudosoft',
        credential: 'Mudo2024Turn!'
    },
    {
        urls: 'turn:10.0.213.89:3478?transport=tcp',
        username: 'mudosoft',
        credential: 'Mudo2024Turn!'
    }
];

const RemoteDesktopPage: React.FC = () => {
    const { deviceId } = useParams<{ deviceId: string }>();
    const canvasRef = useRef<HTMLCanvasElement>(null);
    const videoRef = useRef<HTMLVideoElement>(null);
    const [connection, setConnection] = useState<HubConnection | null>(null);
    const [status, setStatus] = useState<'connecting' | 'connected' | 'webrtc' | 'disconnected' | 'error'>('connecting');
    const [fps, setFps] = useState(0);
    const frameCountRef = useRef(0);
    const [controlEnabled, setControlEnabled] = useState(false);
    const lastMouseMoveRef = useRef<number>(0);

    // WebRTC state
    const peerConnectionRef = useRef<RTCPeerConnection | null>(null);
    const dataChannelRef = useRef<RTCDataChannel | null>(null);
    const [useWebRTC, setUseWebRTC] = useState(true); // Try WebRTC first

    // Monitor selection state (-1 = all, 0+ = specific monitor)
    const [selectedMonitor, setSelectedMonitor] = useState(-1);

    // FPS Meter
    useEffect(() => {
        const interval = setInterval(() => {
            setFps(frameCountRef.current);
            frameCountRef.current = 0;
        }, 1000);
        return () => clearInterval(interval);
    }, []);

    // WebRTC Setup
    const setupWebRTC = useCallback(async (hub: HubConnection) => {
        console.log('🔧 Setting up WebRTC...');

        // Use all transport methods (STUN + TURN)
        const pc = new RTCPeerConnection({
            iceServers: ICE_SERVERS
        });
        peerConnectionRef.current = pc;
        console.log('✅ PeerConnection created');

        // Handle incoming video track
        pc.ontrack = (event) => {
            console.log('📺 Received video track from WebRTC');
            if (videoRef.current && event.streams[0]) {
                videoRef.current.srcObject = event.streams[0];
                setStatus('webrtc');
            }
        };

        // Handle ICE candidates
        pc.onicecandidate = (event) => {
            if (event.candidate && hub) {
                const candidateJson = JSON.stringify({
                    candidate: event.candidate.candidate,
                    sdpMid: event.candidate.sdpMid,
                    sdpMLineIndex: event.candidate.sdpMLineIndex
                });
                hub.invoke('SendIceCandidate', deviceId, candidateJson);
            }
        };

        // Handle connection state changes
        pc.onconnectionstatechange = () => {
            console.log(`🔗 WebRTC state: ${pc.connectionState}`);
            if (pc.connectionState === 'connected') {
                setStatus('webrtc');
            } else if (pc.connectionState === 'failed') {
                console.log('⚠️ WebRTC failed, falling back to SignalR');
                setUseWebRTC(false);
            }
        };

        // Handle data channel for input
        pc.ondatachannel = (event) => {
            console.log('📡 Data channel established');
            dataChannelRef.current = event.channel;
        };

        // Listen for WebRTC answer
        hub.on('ReceiveAnswer', async (sdp: string) => {
            console.log('📥 Received WebRTC answer');
            if (pc && pc.signalingState !== 'closed') {
                await pc.setRemoteDescription({ type: 'answer', sdp });
            }
        });

        hub.on('ReceiveIceCandidate', async (candidateJson: string) => {
            if (pc && pc.signalingState !== 'closed') {
                const candidate = JSON.parse(candidateJson);
                await pc.addIceCandidate(candidate);
            }
        });

        // Add transceiver for receiving video
        pc.addTransceiver('video', { direction: 'recvonly' });

        // Create and send offer
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        await hub.invoke('SendOffer', deviceId, offer.sdp);
        console.log('📤 Sent WebRTC offer');

    }, [deviceId]);

    // Connection Logic
    useEffect(() => {
        if (!deviceId) return;

        const newConnection = new HubConnectionBuilder()
            .withUrl(`${API_BASE_URL}/hubs/desktop`)
            .withAutomaticReconnect()
            .build();

        newConnection.start()
            .then(async () => {
                console.log("Connected to Remote Desktop Hub");
                setStatus('connected');
                await newConnection.invoke("JoinSession", deviceId);

                // Try WebRTC if enabled
                if (useWebRTC) {
                    try {
                        await setupWebRTC(newConnection);
                    } catch (err) {
                        console.error('WebRTC setup failed:', err);
                        setUseWebRTC(false);
                    }
                }
            })
            .catch(err => {
                console.error("Connection failed: ", err);
                setStatus('error');
            });

        // Legacy frame receiver (fallback)
        newConnection.on("ReceiveFrame", (base64Content: string) => {
            if (!useWebRTC || status !== 'webrtc') {
                frameCountRef.current++;
                drawFrame(base64Content);
            }
        });

        newConnection.onclose(() => setStatus('disconnected'));
        setConnection(newConnection);

        return () => {
            peerConnectionRef.current?.close();
            newConnection.stop();
        };
    }, [deviceId, setupWebRTC, useWebRTC]);

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
        if (!controlEnabled) return;

        const payload = {
            Type: ['MouseMove', 'MouseDown', 'MouseUp', 'KeyDown', 'KeyUp', 'Scroll'].indexOf(type),
            ...data
        };

        // Prefer data channel if available
        if (dataChannelRef.current?.readyState === 'open') {
            dataChannelRef.current.send(JSON.stringify(payload));
        } else if (connection && connection.state === HubConnectionState.Connected) {
            connection.invoke("SendInput", deviceId, payload).catch(err => console.error(err));
        }
    };

    // Calculate proper coordinates accounting for object-contain scaling
    const getRelativeCoords = (e: React.MouseEvent) => {
        const element = useWebRTC && status === 'webrtc' ? videoRef.current : canvasRef.current;
        if (!element) return null;

        const rect = element.getBoundingClientRect();
        const elementWidth = element instanceof HTMLVideoElement ? element.videoWidth : (element as HTMLCanvasElement).width || 1;
        const elementHeight = element instanceof HTMLVideoElement ? element.videoHeight : (element as HTMLCanvasElement).height || 1;

        const displayAspect = rect.width / rect.height;
        const imageAspect = elementWidth / elementHeight;

        let imageDisplayWidth, imageDisplayHeight, offsetX, offsetY;

        if (displayAspect > imageAspect) {
            imageDisplayHeight = rect.height;
            imageDisplayWidth = rect.height * imageAspect;
            offsetX = (rect.width - imageDisplayWidth) / 2;
            offsetY = 0;
        } else {
            imageDisplayWidth = rect.width;
            imageDisplayHeight = rect.width / imageAspect;
            offsetX = 0;
            offsetY = (rect.height - imageDisplayHeight) / 2;
        }

        const relX = (e.clientX - rect.left - offsetX) / imageDisplayWidth;
        const relY = (e.clientY - rect.top - offsetY) / imageDisplayHeight;

        return {
            X: Math.max(0, Math.min(1, relX)),
            Y: Math.max(0, Math.min(1, relY))
        };
    };

    // Events
    const handleMouseMove = (e: React.MouseEvent) => {
        const now = Date.now();
        if (now - lastMouseMoveRef.current < 10) return;
        lastMouseMoveRef.current = now;
        const coords = getRelativeCoords(e);
        if (!coords) return;
        sendInput('MouseMove', { ...coords, Button: 0 });
    };

    const handleMouseDown = (e: React.MouseEvent) => {
        const coords = getRelativeCoords(e);
        if (!coords) return;
        sendInput('MouseDown', { ...coords, Button: e.button });
    };

    const handleMouseUp = (e: React.MouseEvent) => {
        const coords = getRelativeCoords(e);
        if (!coords) return;
        sendInput('MouseUp', { ...coords, Button: e.button });
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
        sendInput('KeyDown', { Key: e.key, Ctrl: e.ctrlKey, Alt: e.altKey, Shift: e.shiftKey, X: 0, Y: 0 });
        e.preventDefault();
    };

    const handleKeyUp = (e: React.KeyboardEvent) => {
        sendInput('KeyUp', { Key: e.key, Ctrl: e.ctrlKey, Alt: e.altKey, Shift: e.shiftKey, X: 0, Y: 0 });
        e.preventDefault();
    };

    const sendSpecialKey = (key: string) => {
        if (!controlEnabled) return;
        sendInput('KeyDown', { Key: key, X: 0, Y: 0 });
        setTimeout(() => sendInput('KeyUp', { Key: key, X: 0, Y: 0 }), 100);
    };

    const handleScroll = (e: React.WheelEvent) => {
        const coords = getRelativeCoords(e);
        if (!coords) return;
        const scrollY = e.deltaY > 0 ? -0.1 : 0.1;
        sendInput('Scroll', { X: coords.X, Y: scrollY });
    };

    // Monitor selection function
    const selectMonitor = (monitorIndex: number) => {
        setSelectedMonitor(monitorIndex);
        if (connection && connection.state === HubConnectionState.Connected) {
            connection.invoke("SelectMonitor", deviceId, monitorIndex).catch(err => console.error(err));
        }
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
                    <div className={`w-2 h-2 rounded-full ${status === 'webrtc' ? 'bg-blue-500 shadow-[0_0_8px_rgba(59,130,246,0.5)]' :
                        status === 'connected' ? 'bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.5)]' :
                            'bg-rose-500'
                        }`} />
                    <span className="text-slate-200 font-semibold tracking-wide text-sm">{deviceId}</span>
                    <span className={`text-xs font-mono px-2 py-0.5 rounded ${status === 'webrtc' ? 'bg-blue-900 text-blue-300' : 'bg-slate-800 text-slate-500'
                        }`}>
                        {status === 'webrtc' ? '🚀 P2P' : '📡 Relay'} | {fps} FPS
                    </span>
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

                    {/* Monitor Selection Group */}
                    <div className="flex items-center bg-slate-800 rounded-lg p-1 gap-1 mr-4 border border-slate-700">
                        <button
                            onClick={() => selectMonitor(-1)}
                            className={`px-3 py-1 text-xs font-mono rounded transition-colors ${selectedMonitor === -1 ? 'bg-blue-600 text-white' : 'text-slate-300 hover:bg-slate-700 hover:text-white'}`}
                            title="All Monitors"
                        >
                            ALL
                        </button>
                        <button
                            onClick={() => selectMonitor(0)}
                            className={`px-3 py-1 text-xs font-mono rounded transition-colors ${selectedMonitor === 0 ? 'bg-blue-600 text-white' : 'text-slate-300 hover:bg-slate-700 hover:text-white'}`}
                            title="Monitor 1"
                        >
                            MON1
                        </button>
                        <button
                            onClick={() => selectMonitor(1)}
                            className={`px-3 py-1 text-xs font-mono rounded transition-colors ${selectedMonitor === 1 ? 'bg-blue-600 text-white' : 'text-slate-300 hover:bg-slate-700 hover:text-white'}`}
                            title="Monitor 2"
                        >
                            MON2
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

            {/* Video/Canvas Area */}
            <div className="flex-1 relative flex items-center justify-center bg-zinc-900/50 overflow-hidden">
                {status === 'connecting' && (
                    <div className="absolute flex flex-col items-center gap-2 text-emerald-500">
                        <Loader2 className="w-8 h-8 animate-spin" />
                        <span className="text-sm font-medium">Connecting to secure stream...</span>
                    </div>
                )}

                {/* WebRTC Video Element (hidden when not using WebRTC) */}
                <video
                    ref={videoRef}
                    autoPlay
                    playsInline
                    muted
                    className="w-full h-full object-contain shadow-2xl"
                    style={{
                        cursor: 'default',
                        display: (useWebRTC && status === 'webrtc') ? 'block' : 'none'
                    }}
                    onMouseMove={handleMouseMove}
                    onMouseDown={handleMouseDown}
                    onMouseUp={handleMouseUp}
                    onWheel={handleScroll}
                    onContextMenu={(e) => e.preventDefault()}
                />

                {/* Legacy Canvas Element (fallback) */}
                <canvas
                    ref={canvasRef}
                    className="w-full h-full object-contain shadow-2xl"
                    style={{
                        cursor: 'default',
                        display: (useWebRTC && status === 'webrtc') ? 'none' : 'block'
                    }}
                    onMouseMove={handleMouseMove}
                    onMouseDown={handleMouseDown}
                    onMouseUp={handleMouseUp}
                    onWheel={handleScroll}
                    onContextMenu={(e) => e.preventDefault()}
                />
            </div>
        </div>
    );
};

export default RemoteDesktopPage;
