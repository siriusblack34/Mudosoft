// frontend/src/pages/SQLQueryPage.tsx

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

    // ===============================
    // DEVICES FETCH
    // ===============================
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

    // ===============================
    // FİLTRELENMİŞ / ARANMIŞ LİSTE
    // ===============================
    const filteredDevices = useMemo(() => {
        let list = devices;

        // PC / POS filtresi
        if (filterType === "PC") {
            list = list.filter(d => d.deviceType === "PC");
        } else if (filterType === "POS") {
            list = list.filter(d => ["KK1", "KK2", "KK3"].includes(d.deviceType));
        }

        // Arama filtresi — mağaza adı, kodu veya IP
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

    // ===============================
    // SQL ÇALIŞTIR
    // ===============================
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

    // ===============================
    // PAGE CONTENT
    // ===============================
    if (isLoading)
        return (
            <div className="p-10 flex justify-center items-center">
                <Spinner size="lg" />
            </div>
        );

    return (
        <div className="p-6">

            {/* BAŞLIK */}
            <h1 className="text-3xl font-extrabold mb-6 flex items-center text-green-300">
                <Icons.DatabaseIcon className="w-8 h-8 mr-3" />
                Uzak SQL Sorgu Yöneticisi
            </h1>

            {/* HATA */}
            {error && (
                <div className="bg-red-900/40 border border-red-600 text-red-300 p-4 rounded-md mb-4">
                    {error}
                </div>
            )}

            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">

                {/* ====================================================== */}
                {/*            CİHAZ SEÇİMİ ALANI                         */}
                {/* ====================================================== */}
                <div className="bg-gray-900 p-6 rounded-xl border border-gray-700 shadow-xl">
                    <h2 className="text-xl font-semibold mb-4 text-gray-200">Hedef Cihaz</h2>

                    {/* ====== PC/POS FILTER BUTTONS ====== */}
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

                    {/* ====== SEARCH BAR ====== */}
                    <input
                        type="text"
                        placeholder="🔍 Mağaza adı / IP / kod ara..."
                        className="w-full p-3 rounded-md bg-gray-800 border border-gray-700 text-gray-300 mb-4"
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                    />

                    {/* ====== FILTERED DEVICE LIST ====== */}
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
                            <option disabled className="text-gray-500">
                                Sonuç bulunamadı
                            </option>
                        )}
                    </select>
                </div>

                {/* ====================================================== */}
                {/*              SQL SORGU ALANI                          */}
                {/* ====================================================== */}
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

            {/* ====================================================== */}
            {/*            SONUÇ TABLOSU                               */}
            {/* ====================================================== */}
            {queryResult && (
                <div className="mt-10 bg-gray-900 p-6 rounded-xl border border-gray-700 shadow-xl">
                    <h2 className="text-2xl font-bold mb-4 text-gray-200">Sonuçlar</h2>

                    <p className="text-green-400 mb-3">
                        {queryResult.length} satır döndürüldü.
                    </p>

                    {/* TABLO */}
                    <div className="overflow-x-auto shadow-lg rounded-lg border border-gray-700">
                        <table className="min-w-full text-gray-200">
                            <thead className="bg-gray-800 text-green-300">
                                <tr>
                                    {Object.keys(queryResult[0] || {}).map((col) => (
                                        <th key={col} className="px-4 py-2">{col}</th>
                                    ))}
                                </tr>
                            </thead>
                            <tbody>
                                {queryResult.map((row, i) => (
                                    <tr key={i} className="border-t border-gray-700">
                                        {Object.values(row).map((val, j) => (
                                            <td key={j} className="px-4 py-2">
                                                {val === null ? <i className="text-gray-500">NULL</i> : String(val)}
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
