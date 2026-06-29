import React, { useCallback, useEffect, useState } from "react";
import { Monitor } from "lucide-react";
import { apiClient, GeciciPcStatus } from "../../lib/apiClient";

interface Palette {
    card: string; border: string; borderStrong: string; text: string; muted: string; subtle: string;
    shadow: string; track: string; cardSoft: string;
    emerald: string; emeraldSoft: string; emeraldBorder: string; emeraldGlow: string;
    rose: string; roseSoft: string; roseBorder: string;
    violet: string; violetSoft: string; violetBorder: string;
    amber: string; amberSoft: string; amberBorder: string; slate: string;
}

interface Props { c: Palette; }

const POLL_MS = 60_000;

const GeciciPcStatusPanel: React.FC<Props> = ({ c }) => {
    const [devices, setDevices] = useState<GeciciPcStatus[]>([]);
    const [loading, setLoading] = useState(true);

    const load = useCallback(async () => {
        try { setDevices(await apiClient.getGeciciStatus()); }
        catch { /* sessizce geç */ }
        finally { setLoading(false); }
    }, []);

    useEffect(() => {
        void load();
        const t = window.setInterval(() => void load(), POLL_MS);
        return () => window.clearInterval(t);
    }, [load]);

    const activeCount = devices.filter((d) => d.isActive).length;
    const passiveCount = devices.filter((d) => !d.isActive).length;

    return (
        <div>
            <div className="mb-2 flex items-center gap-2">
                <Monitor className="h-3.5 w-3.5" style={{ color: c.violet }} />
                <span className="text-xs font-semibold" style={{ color: c.text }}>Geçici PC</span>
                <span className="text-[10px]" style={{ color: c.muted }}>
                    {loading && devices.length === 0
                        ? "yükleniyor..."
                        : `(${activeCount} aktif · ${passiveCount} boş)`}
                </span>
            </div>

            <div className="flex flex-wrap gap-1.5">
                {devices.map((d) => <Tile key={d.deviceId} device={d} c={c} />)}
                {loading && devices.length === 0 && (
                    <span className="text-[10px]" style={{ color: c.muted }}>Yükleniyor...</span>
                )}
            </div>
        </div>
    );
};

const Tile: React.FC<{ device: GeciciPcStatus; c: Palette }> = ({ device: d, c }) => {
    const isDown = !d.pingReachable;
    const isActive = d.isActive;

    const accent = isDown ? c.rose : isActive ? c.emerald : c.slate;
    const bg     = isDown ? c.roseSoft : isActive ? c.emeraldGlow : c.track;
    const border = isDown ? c.roseBorder : isActive ? c.emeraldBorder : c.border;

    const storeLine = isActive
        ? (d.installedStoreCode
            ? `[${d.installedStoreCode}] ${d.installedStoreName ?? ""}`
            : "Mağaza ?")
        : isDown ? "Erişilemiyor" : "Boş";

    return (
        <div
            className="flex flex-col rounded-xl border px-2.5 py-1.5 cursor-default"
            style={{ background: bg, borderColor: border, minWidth: 110 }}
            title={d.ipAddress}
        >
            <div className="flex items-center gap-1.5">
                <span
                    className={`h-1.5 w-1.5 shrink-0 rounded-full ${isActive ? "animate-pulse" : ""}`}
                    style={{ background: accent }}
                />
                <span className="text-[11px] font-bold" style={{ color: c.text }}>
                    {d.deviceName}
                </span>
            </div>
            <span
                className="mt-0.5 text-[9px] font-medium leading-tight truncate"
                style={{ color: isActive ? accent : c.muted }}
            >
                {storeLine}
            </span>
        </div>
    );
};

export default GeciciPcStatusPanel;
