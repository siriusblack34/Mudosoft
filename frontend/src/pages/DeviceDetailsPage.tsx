import React, { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import MetricChart from "../components/ui/MetricChart"; 
import { apiClient } from "../lib/apiClient"; 
import type { DeviceMetricDataPoint } from "../lib/apiClient"; 
import RunScriptPanel from "../components/devices/RunScriptPanel"; // RunScriptPanel import edildi

// Cihaz Detayları için yer tutucu arayüz
interface DeviceDetailsState {
    hostname: string;
    ipAddress: string;
    os: string;
}

// ARTIK GEREKSİZ: fetchDeviceMetrics fonksiyonu kaldırıldı.
// Hatanızın kaynağı bu fonksiyonun varlığı veya yanlış kullanımıydı.


const DeviceDetailsPage: React.FC = () => {
    const { deviceId } = useParams<{ deviceId: string }>();

    const [deviceDetails, setDeviceDetails] = useState<DeviceDetailsState | null>(null); 
    const [metrics, setMetrics] = useState<DeviceMetricDataPoint[]>([]); 
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        if (!deviceId) {
            setLoading(false);
            return;
        }
        
        let cancelled = false;

        const loadData = async () => {
            try {
                // 1. Cihaz Detaylarını Yükleme için yer tutucu
                if (!deviceDetails) {
                    setDeviceDetails({ hostname: deviceId, ipAddress: "N/A", os: "N/A" });
                }
                
                // DÜZELTME: Merkezi apiClient metodu kullanılıyor.
                // Bu metot, apiClient.ts'te doğru şekilde tanımlandı.
                const metricsRes = await apiClient.getDeviceMetrics(deviceId); 

                if (!cancelled) setMetrics(metricsRes);
            } catch (err) {
                console.error("Detaylar/Metrikler yüklenemedi:", err);
            } finally {
                if (!cancelled) setLoading(false);
            }
        };

        loadData();
        const intervalId = setInterval(loadData, 30000);

        return () => {
            cancelled = true;
            clearInterval(intervalId);
        };
    }, [deviceId, deviceDetails]);


    if (loading || !deviceId) return <div>Loading device details…</div>;
    if (!deviceDetails) 
        return <div className="text-red-500">Could not load device details for {deviceId}.</div>;

    const cpuData = metrics.map(m => ({ name: m.timestamp, value: m.cpu }));
    const ramData = metrics.map(m => ({ name: m.timestamp, value: m.ram }));
    const diskData = metrics.map(m => ({ name: m.timestamp, value: m.disk }));
    
    const latestCpu = cpuData[cpuData.length - 1]?.value ?? 0;
    const latestRam = ramData[ramData.length - 1]?.value ?? 0;
    const latestDisk = diskData[diskData.length - 1]?.value ?? 0;

    return (
        <div className="space-y-6">
            <h1 className="text-2xl font-semibold">{deviceDetails.hostname} Details</h1>

            <section>
                <h2 className="text-xl font-medium mb-4">Device Information</h2>
                <div className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-md text-sm">
                    <p><strong>Device ID:</strong> {deviceId}</p>
                    <p><strong>IP Address:</strong> {deviceDetails.ipAddress}</p>
                    <p><strong>OS:</strong> {deviceDetails.os}</p>
                </div>
            </section>

            {/* 📊 Metrik Grafikleri Bölümü */}
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

            {/* 💻 Uzaktan Betik Çalıştırma Paneli (YENİ) */}
            <section className="mt-6">
                <RunScriptPanel deviceId={deviceId} />
            </section>
        </div>
    );
};

export default DeviceDetailsPage;