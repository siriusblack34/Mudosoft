import React, { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import MetricChart from "../components/ui/MetricChart";
import { apiClient } from "../lib/apiClient";
import RunScriptPanel from "../components/devices/RunScriptPanel";
import type { Device, DeviceMetric } from "../types"; // ✅ Import eklendi

// ✅ UTC'yi Local Time'a çeviren fonksiyon
const formatTimeLocal = (utcString: string) => {
    const date = new Date(utcString);
    const options: Intl.DateTimeFormatOptions = {
        month: 'numeric',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
    };
    return date.toLocaleString(undefined, options);
};

const DeviceDetailsPage: React.FC = () => {
    const { deviceId } = useParams<{ deviceId: string }>();
    const [deviceData, setDeviceData] = useState<Device | null>(null); // ✅ Tip düzeltildi
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        if (!deviceId) {
            setLoading(false);
            return;
        }

        let cancelled = false;

        const loadData = async () => {
            try {
                const fullDeviceData = await apiClient.getDevice(deviceId);

                if (!cancelled) {
                    setDeviceData(fullDeviceData);
                    console.log("✅ Device data loaded:", fullDeviceData);
                }
            } catch (err) {
                console.error("❌ Failed to load device:", err);
                if (!cancelled) setDeviceData(null);
            } finally {
                if (!cancelled) {
                    setLoading(false);
                }
            }
        };

        loadData();
        const intervalId = setInterval(loadData, 30000);

        return () => {
            cancelled = true;
            clearInterval(intervalId);
        };
    }, [deviceId]);

    if (loading || !deviceId) {
        return <div className="p-4 text-ms-text">Loading device details…</div>;
    }

    if (!deviceData) {
        return <div className="p-4 text-red-500">Could not load device details for {deviceId}.</div>;
    }

    // ✅ Backend'den gelen metrics dizisi
    const metrics: DeviceMetric[] = deviceData.metrics || [];

    // Grafik için veri hazırlama
    const cpuData = metrics.map(m => ({
        name: formatTimeLocal(m.timestampUtc),
        value: m.cpuUsagePercent
    }));
    const ramData = metrics.map(m => ({
        name: formatTimeLocal(m.timestampUtc),
        value: m.ramUsagePercent
    }));
    const diskData = metrics.map(m => ({
        name: formatTimeLocal(m.timestampUtc),
        value: m.diskUsagePercent
    }));

    // Anlık değerler
    const latestCpu = deviceData.cpuUsage ?? 0;
    const latestRam = deviceData.ramUsage ?? 0;
    const latestDisk = deviceData.diskUsage ?? 0;

    return (
        <div className="space-y-6 p-4">
            <h1 className="text-2xl font-semibold">{deviceData.hostname} Details</h1>

            <section>
                <h2 className="text-xl font-medium mb-4">Device Information</h2>
                <div className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-md text-sm">
                    <p><strong>Device ID:</strong> {deviceId}</p>
                    <p><strong>IP Address:</strong> {deviceData.ipAddress}</p>
                    <p><strong>OS:</strong> {deviceData.os}</p>
                    <p><strong>Type:</strong> {deviceData.type}</p>
                    <p><strong>Store:</strong> {deviceData.storeCode}</p>
                    <p><strong>Status:</strong> {deviceData.online ? '🟢 Online' : '🔴 Offline'}</p>
                </div>
            </section>

            <section>
                <h2 className="text-xl font-medium mb-4">Performance Metrics (Last 24 Hours)</h2>
                {metrics.length === 0 ? (
                    <div className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-md text-center text-ms-text-muted">
                        Son 24 saate ait metrik verisi bulunamadı.
                    </div>
                ) : (
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                        <div className="p-4 rounded-2xl">
                            <MetricChart
                                title="CPU Usage"
                                data={cpuData}
                                value={latestCpu}
                                color="#f87171"
                            />
                        </div>
                        <div className="p-4 rounded-2xl">
                            <MetricChart
                                title="RAM Usage"
                                data={ramData}
                                value={latestRam}
                                color="#60a5fa"
                            />
                        </div>
                        <div className="p-4 rounded-2xl">
                            <MetricChart
                                title="Disk Usage"
                                data={diskData}
                                value={latestDisk}
                                color="#34d399"
                            />
                        </div>
                    </div>
                )}
            </section>

            <section className="mt-6">
                <RunScriptPanel deviceId={deviceId} />
            </section>
        </div>
    );
};

export default DeviceDetailsPage;