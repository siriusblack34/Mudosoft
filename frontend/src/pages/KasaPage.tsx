import React, { useEffect, useMemo, useState } from "react";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import { Monitor, RefreshCw, Search, Wifi, WifiOff } from "lucide-react";

interface StoreRow {
    storeCode: number;
    storeName: string;
    kasa1: SqlDeviceWithStatus | null;
    kasa2: SqlDeviceWithStatus | null;
    kasa3: SqlDeviceWithStatus | null;
}

const KasaPage: React.FC = () => {
    const [devices, setDevices] = useState<SqlDeviceWithStatus[]>([]);
    const [search, setSearch] = useState("");
    const [isLoading, setIsLoading] = useState(true);
    const [lastUpdated, setLastUpdated] = useState<Date>(new Date());

    useEffect(() => {
        let isMounted = true;

        const load = async (silent = false) => {
            try {
                if (!silent) setIsLoading(true);

                const data = await apiClient.getSqlDevicesWithStatus({
                    timeoutMs: 500,
                    maxConcurrency: 40,
                });

                if (isMounted) {
                    const kasaDevices = (data ?? []).filter(d =>
                        d.deviceType?.toLowerCase().includes("kasa")
                    );
                    setDevices(kasaDevices);
                    setLastUpdated(new Date());
                }
            } catch (err) {
                console.error("Kasa devices load failed:", err);
            } finally {
                if (isMounted && !silent) setIsLoading(false);
            }
        };

        load();
        const intervalId = setInterval(() => load(true), 30000);

        return () => {
            isMounted = false;
            clearInterval(intervalId);
        };
    }, []);

    const storeRows = useMemo(() => {
        const storeMap = new Map<number, StoreRow>();

        devices.forEach(d => {
            if (!storeMap.has(d.storeCode)) {
                storeMap.set(d.storeCode, {
                    storeCode: d.storeCode,
                    storeName: d.storeName,
                    kasa1: null,
                    kasa2: null,
                    kasa3: null
                });
            }

            const row = storeMap.get(d.storeCode)!;
            const type = d.deviceType?.toLowerCase() || "";

            if (type.includes("kasa-1") || type === "kasa-1") {
                row.kasa1 = d;
            } else if (type.includes("kasa-2") || type === "kasa-2") {
                row.kasa2 = d;
            } else if (type.includes("kasa-3") || type === "kasa-3") {
                row.kasa3 = d;
            }
        });

        return Array.from(storeMap.values()).sort((a, b) => a.storeCode - b.storeCode);
    }, [devices]);

    const filteredRows = useMemo(() => {
        const s = search.trim().toLowerCase();
        if (!s) return storeRows;
        return storeRows.filter(row =>
            row.storeName.toLowerCase().includes(s) ||
            String(row.storeCode).includes(s)
        );
    }, [storeRows, search]);

    const onlineCount = devices.filter(d => d.isOnline).length;
    const offlineCount = devices.length - onlineCount;

    if (isLoading) {
        return (
            <div className="flex h-[80vh] items-center justify-center">
                <Spinner size="lg" />
            </div>
        );
    }

    // LED Indicator
    const Led = ({ device }: { device: SqlDeviceWithStatus | null }) => {
        if (!device) {
            return <div className="w-5 h-5 rounded-full bg-slate-700 mx-auto" />;
        }
        return (
            <div
                title={device.calculatedIpAddress}
                className={`w-5 h-5 rounded-full mx-auto ${device.isOnline
                    ? "bg-emerald-500 shadow-[0_0_10px_rgba(16,185,129,0.8)]"
                    : "bg-rose-500 animate-pulse shadow-[0_0_10px_rgba(225,29,72,0.8)]"
                    }`}
            />
        );
    };

    return (
        <div className="p-6 h-[calc(100vh-2rem)] flex flex-col gap-5">
            {/* Header */}
            <div className="flex items-center justify-between">
                <h1 className="text-2xl font-bold text-white flex items-center gap-3">
                    <Monitor className="w-7 h-7 text-amber-500" />
                    KASA Dashboard
                </h1>
                <div className="flex items-center gap-4">
                    <span className="text-xs text-slate-500">
                        Son güncelleme: {lastUpdated.toLocaleTimeString("tr-TR")}
                    </span>
                    <button
                        onClick={() => window.location.reload()}
                        className="p-2 bg-slate-700/50 hover:bg-slate-600/50 rounded-lg transition-colors"
                    >
                        <RefreshCw className="w-4 h-4 text-slate-400" />
                    </button>
                </div>
            </div>

            {/* Stats Cards */}
            <div className="flex gap-4">
                <div className="flex-1 bg-emerald-900/20 border border-emerald-500/30 rounded-xl p-4 flex items-center gap-4">
                    <Wifi className="w-8 h-8 text-emerald-400" />
                    <div>
                        <div className="text-xs text-emerald-400/80 uppercase">Online Kasalar</div>
                        <div className="text-3xl font-bold text-emerald-400">{onlineCount}</div>
                    </div>
                </div>
                <div className="flex-1 bg-rose-900/20 border border-rose-500/30 rounded-xl p-4 flex items-center gap-4">
                    <WifiOff className="w-8 h-8 text-rose-400" />
                    <div>
                        <div className="text-xs text-rose-400/80 uppercase">Offline Kasalar</div>
                        <div className="text-3xl font-bold text-rose-400">{offlineCount}</div>
                    </div>
                </div>
            </div>

            {/* Search */}
            <div className="flex items-center gap-4 bg-slate-800/40 p-3 rounded-xl border border-slate-700/50">
                <div className="flex-1 relative">
                    <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
                    <input
                        type="text"
                        placeholder="Mağaza adı veya kodu ara..."
                        value={search}
                        onChange={e => setSearch(e.target.value)}
                        className="w-full pl-10 pr-4 py-2 bg-slate-900 border border-slate-700 rounded-lg text-sm text-white placeholder-slate-500 focus:outline-none focus:border-amber-500"
                    />
                </div>
                <span className="text-sm text-slate-400">{filteredRows.length} mağaza</span>
            </div>

            {/* Table */}
            <div className="flex-1 overflow-auto">
                <table className="border-collapse rounded-xl border border-slate-700 bg-slate-900/60">
                    <thead className="sticky top-0 z-20">
                        <tr className="bg-slate-800">
                            <th className="px-3 py-2 text-left text-xs font-semibold text-slate-300 border-b border-slate-700 w-14">
                                Kod
                            </th>
                            <th className="px-3 py-2 text-left text-xs font-semibold text-slate-300 border-b border-slate-700 w-48">
                                Mağaza Adı
                            </th>
                            <th className="px-1 py-2 text-center text-xs font-semibold text-amber-400 border-b border-slate-700 w-10">
                                K1
                            </th>
                            <th className="px-1 py-2 text-center text-xs font-semibold text-amber-400 border-b border-slate-700 w-10">
                                K2
                            </th>
                            <th className="px-1 py-2 text-center text-xs font-semibold text-amber-400 border-b border-slate-700 w-10">
                                K3
                            </th>
                        </tr>
                    </thead>
                    <tbody>
                        {filteredRows.map((row, idx) => (
                            <tr
                                key={row.storeCode}
                                className={`${idx % 2 === 0 ? "bg-slate-900/30" : "bg-slate-800/20"} hover:bg-slate-700/30 transition-colors`}
                            >
                                <td className="px-3 py-1.5 border-b border-slate-800">
                                    <span className="text-amber-400 font-bold text-xs">[{row.storeCode}]</span>
                                </td>
                                <td className="px-3 py-1.5 border-b border-slate-800">
                                    <span className="text-white text-xs">{row.storeName}</span>
                                </td>
                                <td className="px-1 py-1.5 border-b border-slate-800">
                                    <Led device={row.kasa1} />
                                </td>
                                <td className="px-1 py-1.5 border-b border-slate-800">
                                    <Led device={row.kasa2} />
                                </td>
                                <td className="px-1 py-1.5 border-b border-slate-800">
                                    <Led device={row.kasa3} />
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>

                {filteredRows.length === 0 && (
                    <div className="flex flex-col items-center justify-center h-64 text-slate-500">
                        <Monitor className="w-12 h-12 mb-3 opacity-50" />
                        <p>Mağaza bulunamadı</p>
                    </div>
                )}
            </div>
        </div>
    );
};

export default KasaPage;
