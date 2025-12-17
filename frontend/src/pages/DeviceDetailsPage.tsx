import React, { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import MetricChart from "../components/ui/MetricChart";
import { apiClient, type CommandResultRecord } from "../lib/apiClient";
import RunScriptPanel from "../components/devices/RunScriptPanel";
import type { Device, DeviceMetric, OsInfo } from "../types";

// ✅ UTC'yi Local Time'a çeviren fonksiyon (Aynı kalır)
const formatTimeLocal = (utcString: string | null) => {
    if (!utcString) return "N/A";

    // YENİ: Zaten Z varsa tekrar ekleme, Z yoksa ekle.
    const cleanString = utcString.endsWith('Z') ? utcString : utcString + 'Z';

    const date = new Date(cleanString);
    if (isNaN(date.getTime())) return "Invalid Date"; // Fallback

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

            <div className="flex flex-col lg:flex-row gap-6">

                {/* SOL KOLON: Cihaz Bilgileri (Dikey Liste) */}
                <section className="lg:w-1/3 bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-lg h-fit">
                    <h2 className="text-xl font-medium mb-4">Device Information</h2>
                    <div className="space-y-3 text-sm">
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">Hostname:</span>
                            <span className="font-medium">{deviceData.hostname}</span>
                        </div>
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">IP Address:</span>
                            <span>{deviceData.ipAddress}</span>
                        </div>
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">OS:</span>
                            <span className="text-right">
                                {deviceData.os?.version && deviceData.os.version !== '-'
                                    ? deviceData.os.version
                                    : formatOsName(deviceData.os)}
                            </span>
                        </div>
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">Status:</span>
                            <span>{deviceData.online ? '🟢 Online' : '🔴 Offline'}</span>
                        </div>
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">Last Seen:</span>
                            {/* Double Z issue fixed by trusting the ISO string from backend */}
                            <span className="text-right">{formatTimeLocal(deviceData.lastSeen)}</span>
                        </div>
                        <div className="flex justify-between">
                            <span className="text-ms-text-muted">Store Code:</span>
                            <span>{deviceData.storeCode ?? 'N/A'}</span>
                        </div>
                    </div>
                </section>

                {/* SAĞ KOLON: Metrik Grafikleri (Küçültülmüş) */}
                <section className="lg:w-2/3">
                    <h2 className="text-xl font-medium mb-4">Performance Metrics (Last 24 Hours)</h2>
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                        <div className="p-3 bg-ms-panel rounded-2xl border border-ms-border shadow-sm">
                            <MetricChart title="CPU Usage" data={cpuData} value={latestCpu} color="#f87171" height={150} />
                        </div>
                        <div className="p-3 bg-ms-panel rounded-2xl border border-ms-border shadow-sm">
                            <MetricChart title="RAM Usage" data={ramData} value={latestRam} color="#60a5fa" height={150} />
                        </div>
                        <div className="p-3 bg-ms-panel rounded-2xl border border-ms-border shadow-sm">
                            <MetricChart title="Disk Usage" data={diskData} value={latestDisk} color="#34d399" height={150} />
                        </div>
                    </div>
                </section>
            </div>

            {/* Komut Çalıştırma Paneli */}
            <section className="mt-6">
                {/* H. Yazısı Kaldırıldı (Component içinde var) */}
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