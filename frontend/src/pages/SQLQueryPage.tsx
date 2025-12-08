import React, { useState, useEffect, useMemo } from "react";
import { apiClient } from "../lib/apiClient";
import Spinner from "../components/ui/Spinner";
import * as Icons from "../components/icons/Icons";
import { StoreDevice } from "../types";

const SQLQueryPage: React.FC = () => {
    const [devices, setDevices] = useState<StoreDevice[]>([]);
    const [selectedDeviceId, setSelectedDeviceId] = useState<string>("");
    const [filterType, setFilterType] = useState<"ALL" | "PC" | "POS">("ALL");
    const [search, setSearch] = useState("");

    const [sqlQuery, setSqlQuery] = useState("SELECT * FROM STORE_USER");
    const [queryResult, setQueryResult] = useState<any[] | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [isExecuting, setIsExecuting] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const load = async () => {
            try {
                const data = await apiClient.getSqlDevices();
                setDevices(data);
                if (data.length > 0) setSelectedDeviceId(data[0].deviceId);
            } catch {
                setError("Cihaz listesi yüklenemedi.");
            } finally {
                setIsLoading(false);
            }
        };
        load();
    }, []);

    const filteredDevices = useMemo(() => {
        let list = devices;

        if (filterType === "PC") {
            list = list.filter(d => d.deviceType === "PC");
        } else if (filterType === "POS") {
            list = list.filter(d => ["KK1", "KK2", "KK3"].includes(d.deviceType));
        }

        if (search.trim().length > 0) {
            const s = search.toLowerCase();
            list = list.filter(
                d =>
                    d.storeName.toLowerCase().includes(s) ||
                    d.storeCode.toString().includes(s) ||
                    d.calculatedIpAddress.includes(s)
            );
        }

        return list;
    }, [devices, filterType, search]);

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

    if (isLoading)
        return (
            <div className="p-10 flex justify-center items-center">
                <Spinner size="lg" />
            </div>
        );

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
                <div className="bg-gray-900 p-6 rounded-xl border border-gray-700 shadow-xl">
                    <h2 className="text-xl font-semibold mb-4 text-gray-200">Hedef Cihaz</h2>

                    <div className="flex gap-2 mb-4">
                        <button
                            onClick={() => setFilterType("ALL")}
                            className={`px-4 py-2 rounded-md font-bold ${
                                filterType === "ALL"
                                    ? "bg-blue-600 text-white"
                                    : "bg-gray-800 text-gray-400 border border-gray-700"
                            }`}
                        >
                            Tümü
                        </button>

                        <button
                            onClick={() => setFilterType("PC")}
                            className={`px-4 py-2 rounded-md font-bold ${
                                filterType === "PC"
                                    ? "bg-green-600 text-white"
                                    : "bg-gray-800 text-gray-400 border border-gray-700"
                            }`}
                        >
                            💻 PC
                        </button>

                        <button
                            onClick={() => setFilterType("POS")}
                            className={`px-4 py-2 rounded-md font-bold ${
                                filterType === "POS"
                                    ? "bg-yellow-600 text-white"
                                    : "bg-gray-800 text-gray-400 border border-gray-700"
                            }`}
                        >
                            🖥 POS
                        </button>
                    </div>

                    <input
                        type="text"
                        placeholder="🔍 Mağaza adı / IP / kod ara..."
                        className="w-full p-3 rounded-md bg-gray-800 border border-gray-700 text-gray-300 mb-4"
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                    />

                    <select
                        className="w-full p-3 bg-gray-800 border border-gray-600 rounded-lg text-gray-200"
                        size={8}
                        value={selectedDeviceId}
                        onChange={(e) => setSelectedDeviceId(e.target.value)}
                    >
                        {filteredDevices.map((d) => (
                            <option key={d.deviceId} value={d.deviceId}>
                                [{d.storeCode.toString().padStart(3, "0")}] {d.storeName} — {d.deviceType} ({d.calculatedIpAddress})
                            </option>
                        ))}

                        {filteredDevices.length === 0 && (
                            <option disabled>Sonuç bulunamadı</option>
                        )}
                    </select>
                </div>

                <div className="bg-gray-900 p-6 rounded-xl border border-gray-700 shadow-xl">
                    <h2 className="text-xl font-semibold mb-4 text-gray-200">SQL Sorgusu</h2>
                    <textarea
                        className="w-full h-40 p-3 border bg-gray-800 border-gray-600 text-gray-300 rounded-md font-mono text-sm"
                        value={sqlQuery}
                        onChange={(e) => setSqlQuery(e.target.value)}
                    ></textarea>

                    <button
                        onClick={handleExecute}
                        className={`mt-4 px-8 py-3 rounded-md font-bold transition ${
                            isExecuting
                                ? "bg-gray-700 text-gray-400 cursor-not-allowed"
                                : "bg-green-700 hover:bg-green-600 text-white"
                        }`}
                    >
                        {isExecuting ? "Sorgu Çalıştırılıyor..." : "🚀 Sorguyu Çalıştır (SELECT Only)"}
                    </button>
                </div>
            </div>

            {queryResult && (
                <div className="mt-10 bg-gray-900 p-6 rounded-xl border border-gray-700 shadow-xl">
                    <h2 className="text-2xl font-bold mb-4 text-gray-200">Sonuçlar</h2>

                    <p className="text-green-400 mb-3">{queryResult.length} satır döndürüldü.</p>

                    <div className="w-full overflow-x-auto rounded-lg border border-gray-700 shadow-lg">
                        <table className="min-w-[1400px] text-gray-200 table-auto">
                            <thead className="bg-gray-800 text-green-300 sticky top-0 z-10">
                                <tr>
                                    {Object.keys(queryResult[0] || {}).map((col) => (
                                        <th key={col} className="px-4 py-2 whitespace-nowrap">{col}</th>
                                    ))}
                                </tr>
                            </thead>

                            <tbody>
                                {queryResult.map((row, i) => (
                                    <tr
                                        key={i}
                                        className={`${
                                            i % 2 === 0 ? "bg-gray-800/40" : "bg-gray-900/40"
                                        } border-t border-gray-700`}
                                    >
                                        {Object.values(row).map((val, j) => (
                                            <td
                                                key={j}
                                                className="px-4 py-2 max-w-[200px] overflow-hidden truncate whitespace-nowrap text-sm"
                                                title={val === null ? "NULL" : String(val)}
                                            >
                                                {val === null ? (
                                                    <i className="text-gray-500">NULL</i>
                                                ) : (
                                                    String(val)
                                                )}
                                            </td>
                                        ))}
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </div>
            )}
        </div>
    );
};

export default SQLQueryPage;
