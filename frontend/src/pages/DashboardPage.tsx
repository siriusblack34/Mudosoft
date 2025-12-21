import React, { useEffect, useState } from "react";
import { apiClient } from "../lib/apiClient";
import DashboardHeader from "../components/dashboard/DashboardHeader";
import DashboardStats from "../components/dashboard/DashboardStats";
import ComplianceChart from "../components/dashboard/ComplianceChart";
import RecentActivity from "../components/dashboard/RecentActivity";
import { ScanSearch } from "lucide-react";

interface RecentOfflineDevice {
    hostname: string;
    ip: string;
    os: string;
    store: number;
    lastSeen: string;
}

interface DashboardState {
    totalDevices: number;
    online: number;
    offline: number;
    healthy: number;
    warning: number;
    critical: number;
    recentOffline: RecentOfflineDevice[];
}

const DashboardPage: React.FC = () => {
    const [data, setData] = useState<DashboardState | null>(null);
    const [loading, setLoading] = useState(true);
    const [scanning, setScanning] = useState(false);
    const [lastUpdated, setLastUpdated] = useState<Date>(new Date());

    const load = async () => {
        setLoading(true);
        try {
            const res = await apiClient.getDashboard();
            setData({
                totalDevices: res.totalDevices,
                online: res.online,
                offline: res.offline,
                healthy: res.healthy,
                warning: res.warning,
                critical: res.critical,
                recentOffline: res.recentOffline.map((d: any) => ({
                    hostname: d.hostname,
                    ip: d.ip ?? d.ipAddress,
                    os: d.os,
                    store: d.store ?? d.storeCode,
                    lastSeen: d.lastSeen
                }))
            });
            setLastUpdated(new Date());
        } catch (err) {
            console.error("Dashboard load failed:", err);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        load();
        const id = setInterval(load, 30000);
        return () => clearInterval(id);
    }, []);

    const runStoreDiscovery = async () => {
        const storeCode = prompt("Store Code gir (Örn: 7)");
        if (!storeCode) return;

        setScanning(true);
        try {
            // Using fetch directly as this specific endpoint might not be in apiClient yet, 
            // or we could add it. Sticking to existing logic for safety.
            const res = await fetch("http://localhost:5102/api/discovery/store", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ storeCode: Number(storeCode) })
            });

            if (!res.ok) throw new Error("Discovery failed");

            const result = await res.json();
            alert(
                `Discovery tamamlandı\n\nStore: ${result.storeCode}\nOnline: ${result.onlineDevices.length}\nOffline: ${result.offlineDevices.length}`
            );
            load(); // Reload dashboard after discovery
        } catch (err) {
            console.error(err);
            alert("Discovery başarısız");
        } finally {
            setScanning(false);
        }
    };

    if (!data && loading) {
        return (
            <div className="flex h-[80vh] items-center justify-center">
                <div className="text-center">
                    <div className="w-16 h-16 border-4 border-indigo-500 border-t-transparent rounded-full animate-spin mx-auto mb-4"></div>
                    <p className="text-slate-400 animate-pulse">Loading dashboard environment...</p>
                </div>
            </div>
        );
    }

    if (!data) {
        return (
            <div className="flex h-[80vh] items-center justify-center">
                <div className="text-center max-w-md p-8 bg-slate-800 rounded-2xl border border-slate-700">
                    <h2 className="text-xl font-semibold text-white mb-2">Could not load dashboard</h2>
                    <p className="text-slate-400 mb-6">The connection to the backend server failed. Please check if the API is running.</p>
                    <button onClick={load} className="px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-500 transition-colors">
                        Retry Connection
                    </button>
                </div>
            </div>
        );
    }

    return (
        <div className="min-h-screen pb-10 animate-fade-in">
            <div className="flex flex-col md:flex-row md:items-start justify-between gap-4">
                <div className="flex-1">
                    <DashboardHeader
                        lastRefreshed={lastUpdated}
                        onRefresh={load}
                        isLoading={loading && !!data}
                    />
                </div>

                <div className="mt-1 md:mt-0">
                    <button
                        onClick={runStoreDiscovery}
                        disabled={scanning}
                        className="flex items-center gap-2 px-4 py-2 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white text-sm font-medium shadow-sm transition-all disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                        <ScanSearch className={`w-4 h-4 ${scanning ? 'animate-spin' : ''}`} />
                        <span>{scanning ? "Scanning..." : "Run Discovery"}</span>
                    </button>
                </div>
            </div>

            <DashboardStats data={data} />

            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 h-[400px]">
                <div className="lg:col-span-1 h-full">
                    <ComplianceChart
                        healthy={data.healthy}
                        warning={data.warning}
                        critical={data.critical}
                    />
                </div>
                <div className="lg:col-span-2 h-full">
                    <RecentActivity devices={data.recentOffline} />
                </div>
            </div>
        </div>
    );
};

export default DashboardPage;
