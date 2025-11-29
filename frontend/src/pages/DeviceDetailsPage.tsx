import React, { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import MetricChart from "../components/ui/MetricChart";
import { apiClient, type CommandResultRecord } from "../lib/apiClient";
import RunScriptPanel from "../components/devices/RunScriptPanel";
import type { Device, DeviceMetric, OsInfo } from "../types"; 

// ✅ UTC'yi Local Time'a çeviren fonksiyon (Aynı kalır)
const formatTimeLocal = (utcString: string | null) => {
    if (!utcString) return "N/A";
    const date = new Date(utcString.endsWith('Z') ? utcString : utcString + 'Z');
    const options: Intl.DateTimeFormatOptions = {
        year: 'numeric',
        month: 'numeric',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit', 
        hour12: false, 
    };
    return date.toLocaleString(undefined, options);
};

// 🏆 YENİ FONKSİYON: Kullanıcı dostu OS ismi çevirisi
const formatOsName = (osInfo?: OsInfo) => {
    // 🔴 KRİTİK KONTROL: osInfo veya osInfo.name yoksa N/A döndür
    if (!osInfo || !osInfo.name) return "N/A";
    
    const name = osInfo.name.trim(); // Boşlukları temizleyelim
    if (!name) return "N/A";

    // Yaygın NT Sürüm Kodlarının Kullanıcı Adlarına Çevrilmesi
    if (name.includes('NT 6.1')) return 'Windows 7 / Server 2008 R2';
    if (name.includes('NT 6.2')) return 'Windows 8 / Server 2012';
    if (name.includes('NT 6.3')) return 'Windows 8.1 / Server 2012 R2';
    if (name.includes('NT 10.0')) return 'Windows 10 / Server 2016+'; 
    
    // Eşleşme yoksa, ham ismini döndür
    return name; 
};


const DeviceDetailsPage: React.FC = () => {
    const { deviceId } = useParams<{ deviceId: string }>();
    const [deviceData, setDeviceData] = useState<Device | null>(null);
    const [latestCommandOutput, setLatestCommandOutput] = useState<CommandResultRecord | null>(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        if (!deviceId) {
            setLoading(false);
            return;
        }

        let cancelled = false;

        const loadLatestCommandResult = async () => {
            try {
                const result = await apiClient.get<CommandResultRecord>(
                    `agent/command-results/latest?deviceId=${deviceId}`
                );

                if (!cancelled) {
                    if (result && result.output && result.output !== "Henüz komut sonucu kaydedilmedi.") {
                        setLatestCommandOutput(result);
                        console.log("✅ COMMAND OUTPUT ALINDI:", result.output.substring(0, 50) + '...');
                    } else if (!latestCommandOutput) {
                        setLatestCommandOutput({ output: "Çalıştırma sonucu buraya gelecek.", completedAtUtc: null });
                    }
                }
            } catch (err) {
                console.error("❌ Failed to load command result:", err);
            }
        };


        const loadData = async () => {
            try {
                const fullDeviceData = await apiClient.getDevice(deviceId);

                if (!cancelled) {
                    setDeviceData(fullDeviceData);
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
        loadLatestCommandResult(); 

        const intervalId = setInterval(() => {
            loadData();
            loadLatestCommandResult();
        }, 30000);

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

    const metrics: DeviceMetric[] = deviceData.metrics || [];

    // Grafik verileri
    const cpuData = metrics.map(m => ({ name: formatTimeLocal(m.timestampUtc), value: m.cpuUsagePercent }));
    const ramData = metrics.map(m => ({ name: formatTimeLocal(m.timestampUtc), value: m.ramUsagePercent }));
    const diskData = metrics.map(m => ({ name: formatTimeLocal(m.timestampUtc), value: m.diskUsagePercent }));

    // Backend'den gelen doğru anlık alan adlarını kullanıyoruz.
    const latestCpu = deviceData.cpuUsage ?? 0; 
    const latestRam = deviceData.ramUsage ?? 0;
    const latestDisk = deviceData.diskUsage ?? 0;


    return (
        <div className="space-y-6 p-4">
            <h1 className="text-2xl font-semibold">{deviceData.hostname} Details</h1>

            {/* Cihaz Bilgileri */}
            <section className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-lg">
                <h2 className="text-xl font-medium mb-4">Device Information</h2>
                <div className="grid grid-cols-2 gap-y-1 text-sm">
                    <p><strong>Device ID:</strong> {deviceId}</p>
                    <p><strong>Hostname:</strong> {deviceData.hostname}</p>
                    <p><strong>IP Address:</strong> {deviceData.ipAddress}</p>
                    
                    {/* 🚀 SON DÜZELTME: Temizlenmiş OS gösterimi */}
                    <p><strong>OS:</strong> {formatOsName(deviceData.os)}</p>
                    
                    <p><strong>Status:</strong> {deviceData.online ? '🟢 Online' : '🔴 Offline'}</p>
                    
                    <p><strong>Last Seen:</strong> {formatTimeLocal(deviceData.lastSeen + 'Z')}</p>
                    
                    {/* Agent Version artık Backend'de DTO'ya eklendi, burada N/A fallback'i ile gösterilir */}
                    <p><strong>Agent Version:</strong> {deviceData.agentVersion || 'N/A'}</p>
                    
                    <p><strong>Store Code:</strong> {deviceData.storeCode ?? 'N/A'}</p>
                </div>
            </section>

            {/* Metrik Grafikleri */}
            <section>
                <h2 className="text-xl font-medium mb-4">Performance Metrics (Last 24 Hours)</h2>
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                    <div className="p-4 rounded-2xl">
                        <MetricChart title="CPU Usage" data={cpuData} value={latestCpu} color="#f87171" />
                    </div>
                    <div className="p-4 rounded-2xl">
                        <MetricChart title="RAM Usage" data={ramData} value={latestRam} color="#60a5fa" />
                    </div>
                    <div className="p-4 rounded-2xl">
                        <MetricChart title="Disk Usage" data={diskData} value={latestDisk} color="#34d399" />
                    </div>
                </div>
            </section>

            {/* Komut Çalıştırma Paneli */}
            <section className="mt-6">
                <h2 className="text-xl font-medium mb-4">Run Remote Script (PowerShell / Bash)</h2>
                <RunScriptPanel deviceId={deviceId} />
                
                <div 
                    key={latestCommandOutput?.completedAtUtc ? 'output-loaded' : 'output-empty'}
                    className="bg-ms-panel p-4 rounded-b-2xl border border-ms-border shadow-md mt-2 whitespace-pre-wrap"
                >
                    <p className="font-semibold mb-2">Çıktı:</p>
                    {latestCommandOutput?.completedAtUtc && (
                        <p className="text-xs text-ms-text-muted mb-2">
                            Son Başarılı Çalıştırma: {formatTimeLocal(latestCommandOutput.completedAtUtc)}
                        </p>
                    )}
                    <textarea 
                        className="w-full h-80 bg-transparent text-ms-text border-0 resize-none focus:outline-none font-mono text-sm"
                        value={latestCommandOutput?.output || "Çalıştırma sonucu buraya gelecek."}
                        readOnly
                    />
                </div>
            </section>
        </div>
    );
};

export default DeviceDetailsPage;