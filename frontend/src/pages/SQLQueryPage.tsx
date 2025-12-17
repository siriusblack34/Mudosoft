import React, { useEffect, useMemo, useState } from "react";
import { apiClient, SqlDeviceWithStatus } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import * as Icons from "../components/icons/Icons";

type FilterType = "ALL" | "PC" | "POS";

const SQLQueryPage: React.FC = () => {
    const [devices, setDevices] = useState<SqlDeviceWithStatus[]>([]);
    const [selectedDeviceId, setSelectedDeviceId] = useState<string>("");

    const [filterType, setFilterType] = useState<FilterType>("ALL");
    const [search, setSearch] = useState("");

    const [sqlQuery, setSqlQuery] = useState("SELECT * FROM STORE_USER");
    const [queryResult, setQueryResult] = useState<any[] | null>(null);

    const [isLoading, setIsLoading] = useState(true);
    const [isExecuting, setIsExecuting] = useState(false);
    const [error, setError] = useState<string | null>(null);

    // =====================================================
    // LOAD – TEK VE DOĞRU ENDPOINT
    // =====================================================
    useEffect(() => {
        const load = async () => {
            try {
                setError(null);
                const data = await apiClient.getSqlDevicesWithStatus({
                    timeoutMs: 500,
                    maxConcurrency: 40,
                });
                setDevices(data ?? []);
            } catch {
                setError("Cihaz listesi yüklenemedi.");
            } finally {
                setIsLoading(false);
            }
        };
        load();
    }, []);

    // =====================================================
    // HELPERS
    // =====================================================
    const isPc = (d: SqlDeviceWithStatus) =>
        (d.deviceType ?? "").toLowerCase() === "pc";

    const isPos = (d: SqlDeviceWithStatus) =>
        (d.deviceType ?? "").toLowerCase().includes("kasa");

    const matchesSegment = (d: SqlDeviceWithStatus) => {
        if (filterType === "PC") return isPc(d);
        if (filterType === "POS") return isPos(d);
        return true;
    };

    const matchesSearch = (d: SqlDeviceWithStatus) => {
        const s = search.trim().toLowerCase();
        if (!s) return true;
        return (
            d.storeName.toLowerCase().includes(s) ||
            String(d.storeCode).includes(s) ||
            d.calculatedIpAddress.includes(s) ||
            d.deviceType.toLowerCase().includes(s)
        );
    };

    // =====================================================
    // COUNTS (GERÇEK ENVANTER)
    // =====================================================
    const counts = useMemo(() => {
        const pcs = devices.filter(isPc);
        const pos = devices.filter(isPos);

        const count = (list: SqlDeviceWithStatus[]) => ({
            total: list.length,
            online: list.filter(d => d.isOnline).length,
            offline: list.filter(d => !d.isOnline).length,
        });

        return {
            ALL: count(devices),
            PC: count(pcs),
            POS: count(pos),
        };
    }, [devices]);

    // =====================================================
    // DISPLAY LIST
    // =====================================================
    const displayedDevices = useMemo(() => {
        return devices
            .filter(matchesSegment)
            .filter(matchesSearch)
            .sort((a, b) => {
                if (a.isOnline !== b.isOnline) return a.isOnline ? -1 : 1;
                return a.storeCode - b.storeCode;
            });
    }, [devices, filterType, search]);

    useEffect(() => {
        if (!selectedDeviceId && displayedDevices.length > 0) {
            setSelectedDeviceId(displayedDevices[0].deviceId);
        }
    }, [displayedDevices, selectedDeviceId]);

    // =====================================================
    // EXECUTE SQL
    // =====================================================
    const handleExecute = async () => {
        if (!selectedDeviceId) return;
        setIsExecuting(true);
        setError(null);
        setQueryResult(null);

        try {
            const result = await apiClient.runSqlQuery(selectedDeviceId, sqlQuery);
            setQueryResult(result);
        } catch {
            setError("SQL sorgusu başarısız oldu.");
        } finally {
            setIsExecuting(false);
        }
    };

    if (isLoading) {
        return (
            <div className="p-10 flex justify-center items-center">
                <Spinner size="lg" />
            </div>
        );
    }

    const c = counts[filterType];

    return (
        <div className="p-6">
            <h1 className="text-3xl font-extrabold mb-6 flex items-center text-green-300">
                <Icons.DatabaseIcon className="w-8 h-8 mr-3" />
                Uzak SQL Sorgu Yöneticisi
            </h1>

            {error && (
                <div className="bg-red-900/40 border border-red-600 text-red-300 p-4 rounded-md mb-4">
                    {error}
                </div>
            )}

            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                {/* LEFT */}
                <div className="bg-gray-900 p-6 rounded-xl border border-gray-700">
                    <p className="mb-4">
                        🟢 {c.online} online &nbsp; 🔴 {c.offline} offline &nbsp; ⚪ {c.total} toplam
                    </p>

                    <div className="flex gap-2 mb-4">
                        {(["ALL", "PC", "POS"] as FilterType[]).map(t => (
                            <button
                                key={t}
                                onClick={() => setFilterType(t)}
                                className={`px-4 py-2 rounded ${
                                    filterType === t ? "bg-blue-600" : "bg-gray-700"
                                }`}
                            >
                                {t}
                            </button>
                        ))}
                    </div>

                    <input
                        className="w-full p-2 mb-3 bg-gray-800 border border-gray-600"
                        placeholder="Ara..."
                        value={search}
                        onChange={e => setSearch(e.target.value)}
                    />

                    <select
                        size={12}
                        className="w-full bg-gray-800 border border-gray-600"
                        value={selectedDeviceId}
                        onChange={e => setSelectedDeviceId(e.target.value)}
                    >
                        {displayedDevices.map(d => (
                            <option
                                key={d.deviceId}
                                value={d.deviceId}
                                className={d.isOnline ? "text-green-400" : "text-red-400"}
                            >
                                {d.isOnline ? "🟢" : "🔴"} [{String(d.storeCode).padStart(3, "0")}]{" "}
                                {d.storeName} — {d.deviceType}
                            </option>
                        ))}
                    </select>
                </div>

                {/* RIGHT */}
                <div className="bg-gray-900 p-6 rounded-xl border border-gray-700">
                    <textarea
                        className="w-full h-40 bg-gray-800 border border-gray-600"
                        value={sqlQuery}
                        onChange={e => setSqlQuery(e.target.value)}
                    />

                    <button
                        onClick={handleExecute}
                        disabled={isExecuting || !selectedDeviceId}
                        className="mt-4 px-6 py-2 bg-green-700 rounded"
                    >
                        🚀 Sorguyu Çalıştır
                    </button>
                </div>
            </div>
        </div>
    );
};

export default SQLQueryPage;
