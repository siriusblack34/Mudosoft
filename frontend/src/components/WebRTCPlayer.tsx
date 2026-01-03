import { useEffect, useRef, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';

interface WebRTCPlayerProps {
    deviceId: string;
    backendUrl: string;
    onConnectionStateChange?: (state: RTCPeerConnectionState) => void;
    onFpsUpdate?: (fps: number) => void;
}

// ICE Servers configuration
const ICE_SERVERS: RTCIceServer[] = [
    { urls: 'stun:stun.l.google.com:19302' },
    { urls: 'stun:stun1.l.google.com:19302' },
    // TURN server will be added here for production
];

export function WebRTCPlayer({
    deviceId,
    backendUrl,
    onConnectionStateChange,
    onFpsUpdate
}: WebRTCPlayerProps) {
    const videoRef = useRef<HTMLVideoElement>(null);
    const peerConnectionRef = useRef<RTCPeerConnection | null>(null);
    const dataChannelRef = useRef<RTCDataChannel | null>(null);
    const hubConnectionRef = useRef<signalR.HubConnection | null>(null);
    const [connectionState, setConnectionState] = useState<RTCPeerConnectionState>('new');
    const [isControlActive, setIsControlActive] = useState(false);
    const frameCountRef = useRef(0);
    const lastFpsUpdateRef = useRef(Date.now());

    // Setup SignalR connection
    const setupSignaling = useCallback(async () => {
        const hubConnection = new signalR.HubConnectionBuilder()
            .withUrl(`${backendUrl}/hubs/desktop`)
            .withAutomaticReconnect()
            .build();

        // Handle incoming answer from agent
        hubConnection.on('ReceiveAnswer', async (sdp: string) => {
            console.log('📥 Received WebRTC answer');
            if (peerConnectionRef.current) {
                await peerConnectionRef.current.setRemoteDescription({
                    type: 'answer',
                    sdp: sdp
                });
                console.log('✅ Remote description set');
            }
        });

        // Handle incoming ICE candidates
        hubConnection.on('ReceiveIceCandidate', async (candidateJson: string) => {
            console.log('🧊 Received ICE candidate');
            if (peerConnectionRef.current) {
                const candidate = JSON.parse(candidateJson);
                await peerConnectionRef.current.addIceCandidate(candidate);
            }
        });

        // Legacy frame receiver (fallback while WebRTC is being established)
        hubConnection.on('ReceiveFrame', (base64: string) => {
            // This will be replaced by WebRTC video track
            frameCountRef.current++;
            const now = Date.now();
            if (now - lastFpsUpdateRef.current >= 1000) {
                onFpsUpdate?.(frameCountRef.current);
                frameCountRef.current = 0;
                lastFpsUpdateRef.current = now;
            }
        });

        await hubConnection.start();
        console.log('✅ SignalR connected for signaling');

        hubConnectionRef.current = hubConnection;
        return hubConnection;
    }, [backendUrl, onFpsUpdate]);

    // Create WebRTC peer connection
    const createPeerConnection = useCallback(() => {
        console.log('🔧 Creating WebRTC peer connection...');

        const pc = new RTCPeerConnection({ iceServers: ICE_SERVERS });

        // Handle incoming video track
        pc.ontrack = (event) => {
            console.log('📺 Received video track');
            if (videoRef.current && event.streams[0]) {
                videoRef.current.srcObject = event.streams[0];
            }
        };

        // Handle ICE candidates
        pc.onicecandidate = (event) => {
            if (event.candidate && hubConnectionRef.current) {
                const candidateJson = JSON.stringify({
                    candidate: event.candidate.candidate,
                    sdpMid: event.candidate.sdpMid,
                    sdpMLineIndex: event.candidate.sdpMLineIndex
                });
                hubConnectionRef.current.invoke('SendIceCandidate', deviceId, candidateJson);
                console.log('📤 Sent ICE candidate');
            }
        };

        // Handle connection state changes
        pc.onconnectionstatechange = () => {
            console.log(`🔗 Connection state: ${pc.connectionState}`);
            setConnectionState(pc.connectionState);
            onConnectionStateChange?.(pc.connectionState);
        };

        // Handle data channel for input
        pc.ondatachannel = (event) => {
            console.log('📡 Data channel established');
            dataChannelRef.current = event.channel;
            setIsControlActive(true);
        };

        peerConnectionRef.current = pc;
        return pc;
    }, [deviceId, onConnectionStateChange]);

    // Start WebRTC connection
    const startConnection = useCallback(async () => {
        try {
            // Setup signaling first
            const hub = await setupSignaling();

            // Join session (this notifies the agent)
            await hub.invoke('JoinSession', deviceId);
            console.log('👁️ Joined session for device:', deviceId);

            // Create peer connection
            const pc = createPeerConnection();

            // Add transceivers for receiving video
            pc.addTransceiver('video', { direction: 'recvonly' });

            // Create offer
            const offer = await pc.createOffer();
            await pc.setLocalDescription(offer);

            // Send offer to agent via signaling server
            await hub.invoke('SendOffer', deviceId, offer.sdp);
            console.log('📤 Sent WebRTC offer');

        } catch (error) {
            console.error('❌ WebRTC connection error:', error);
        }
    }, [deviceId, setupSignaling, createPeerConnection]);

    // Send input via data channel
    const sendInput = useCallback((input: {
        type: string;
        x: number;
        y: number;
        button?: number;
        key?: string;
        ctrl?: boolean;
        shift?: boolean;
        alt?: boolean;
    }) => {
        if (dataChannelRef.current?.readyState === 'open') {
            dataChannelRef.current.send(JSON.stringify(input));
        } else {
            // Fallback to SignalR if data channel not available
            hubConnectionRef.current?.invoke('SendInput', deviceId, input);
        }
    }, [deviceId]);

    // Mouse event handlers
    const handleMouseEvent = useCallback((
        e: React.MouseEvent<HTMLVideoElement>,
        type: 'MouseMove' | 'MouseDown' | 'MouseUp'
    ) => {
        if (!isControlActive && type !== 'MouseMove') return;

        const rect = e.currentTarget.getBoundingClientRect();
        const x = (e.clientX - rect.left) / rect.width;
        const y = (e.clientY - rect.top) / rect.height;

        sendInput({
            type,
            x,
            y,
            button: e.button
        });
    }, [isControlActive, sendInput]);

    // Keyboard event handlers
    const handleKeyEvent = useCallback((
        e: React.KeyboardEvent,
        type: 'KeyDown' | 'KeyUp'
    ) => {
        if (!isControlActive) return;

        e.preventDefault();

        sendInput({
            type,
            x: 0,
            y: 0,
            key: e.key,
            ctrl: e.ctrlKey,
            shift: e.shiftKey,
            alt: e.altKey
        });
    }, [isControlActive, sendInput]);

    // Scroll handler
    const handleScroll = useCallback((e: React.WheelEvent<HTMLVideoElement>) => {
        if (!isControlActive) return;

        const rect = e.currentTarget.getBoundingClientRect();
        const x = (e.clientX - rect.left) / rect.width;
        const y = e.deltaY > 0 ? -0.1 : 0.1; // Normalize scroll

        sendInput({
            type: 'Scroll',
            x,
            y
        });
    }, [isControlActive, sendInput]);

    // Control toggle
    const toggleControl = useCallback(() => {
        setIsControlActive(!isControlActive);
    }, [isControlActive]);

    // Initialize on mount
    useEffect(() => {
        startConnection();

        return () => {
            // Cleanup
            peerConnectionRef.current?.close();
            hubConnectionRef.current?.stop();
        };
    }, [startConnection]);

    return (
        <div className="webrtc-player" style={{ position: 'relative', width: '100%', height: '100%' }}>
            {/* Connection State Badge */}
            <div style={{
                position: 'absolute',
                top: 8,
                left: 8,
                padding: '4px 12px',
                borderRadius: '4px',
                backgroundColor: connectionState === 'connected' ? '#10b981' :
                    connectionState === 'connecting' ? '#f59e0b' : '#ef4444',
                color: 'white',
                fontSize: '12px',
                zIndex: 10
            }}>
                {connectionState === 'connected' ? '🟢 P2P Connected' :
                    connectionState === 'connecting' ? '🟡 Connecting...' :
                        '🔴 ' + connectionState}
            </div>

            {/* Control Toggle Button */}
            <button
                onClick={toggleControl}
                style={{
                    position: 'absolute',
                    top: 8,
                    right: 8,
                    padding: '8px 16px',
                    borderRadius: '4px',
                    backgroundColor: isControlActive ? '#ef4444' : '#10b981',
                    color: 'white',
                    border: 'none',
                    cursor: 'pointer',
                    zIndex: 10
                }}
            >
                {isControlActive ? '🛑 RELEASE CONTROL' : '🎮 TAKE CONTROL'}
            </button>

            {/* Video Element */}
            <video
                ref={videoRef}
                autoPlay
                playsInline
                muted
                style={{
                    width: '100%',
                    height: '100%',
                    objectFit: 'contain',
                    backgroundColor: '#1a1a2e',
                    cursor: isControlActive ? 'none' : 'default'
                }}
                tabIndex={0}
                onMouseMove={(e) => handleMouseEvent(e, 'MouseMove')}
                onMouseDown={(e) => handleMouseEvent(e, 'MouseDown')}
                onMouseUp={(e) => handleMouseEvent(e, 'MouseUp')}
                onWheel={handleScroll}
                onKeyDown={(e) => handleKeyEvent(e, 'KeyDown')}
                onKeyUp={(e) => handleKeyEvent(e, 'KeyUp')}
                onContextMenu={(e) => e.preventDefault()}
            />
        </div>
    );
}
