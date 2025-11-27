import React, { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import MetricChart from "../components/ui/MetricChart"; 
import { apiClient } from "../lib/apiClient"; 
import RunScriptPanel from "../components/devices/RunScriptPanel"; 
// types dosyasındaki Device modelini içe aktarın
import type { Device } from "../types";

// Backend'den gelen Metric yapısını taklit eden minimal arayüz
interface DeviceMetricDataPoint {
    timestampUtc: string;
    cpuUsagePercent: number;
    ramUsagePercent: number;
    diskUsagePercent: number;
}

// 🏆 KESİN ZAMAN DÜZELTMESİ FONKSİYONU
const formatTimeLocal = (utcString: string) => {
    // Gelen dizeyi Date objesine dönüştür. (String UTC formatında ise, dönüştürmeyi garanti altına alır)
    // Eğer string sonunda Z yoksa, local olarak yorumlayıp tekrar kaydırma hatası yapmaması için
    // toLocaleString(undefined, options) kullanıyoruz.
    const date = new Date(utcString); 
    
    // Formatlama seçenekleri: Saat dilimi dönüştürmesini zorlar.
    const options: Intl.DateTimeFormatOptions = {
        year: 'numeric',
        month: 'numeric',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
    };
    
    // Tarayıcının yerel ayarlarını kullanarak formatla (Örn: UTC -> UTC+3)
    return date.toLocaleString(undefined, options); 
};


// 🚨 Bileşenin Yüklendiğini gösteren genel log
console.log("DeviceDetailsPage.tsx dosyası yüklendi.");

const DeviceDetailsPage: React.FC = () => {
    const { deviceId } = useParams<{ deviceId: string }>();
    
    // 🚨 Render başladığında log yaz
    console.log("DeviceDetailsPage render ediliyor. Current deviceId:", deviceId);

    const [deviceData, setDeviceData] = useState<Device | null>(null); 
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        // 🚨 useEffect bloğunun çalıştığını gösteren log
        console.log("useEffect çalışıyor, API çağrısı başlatılıyor...");
        
        if (!deviceId) {
            setLoading(false);
            return;
        }
        
        let cancelled = false;

        const loadData = async () => {
            try {
                // API çağrısı, tek bir cihazın tüm detaylarını ve metriklerini çeker.
                const fullDeviceData = await apiClient.getDevice(deviceId); 

                if (!cancelled) {
                    setDeviceData(fullDeviceData);
                    // 🚨 Başarılı Yükleme Logu
                    console.log("SUCCESS: Cihaz verileri yüklendi. Hostname:", fullDeviceData.hostname);
                }
            } catch (err) {
                // 🚨 Hata Logu
                console.error("Detaylar/Metrikler yüklenemedi:", err);
                if (!cancelled) setDeviceData(null); 
            } finally {
                // Veri çekme işlemi bittiğinde (başarılı veya hatalı) loading durumunu kapatıyoruz.
                if (!cancelled) {
                    // 🚨 Loading Kapatma Logu
                    console.log("FINALLY: Loading durumu kapatıldı.");
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


    // --- Render Mantığı ---

    // Bu blok, loading true ise çalışır.
    if (loading || !deviceId) {
        return <div className="p-4 text-ms-text">Loading device details…</div>;
    }
    
    // Bu blok, loading false olduktan sonra (veri gelmediyse) çalışır.
    if (!deviceData) 
        return <div className="p-4 text-red-500">Could not load device details for {deviceId}.</div>;

    // Metrik koleksiyonunu Device modelinden güvenli bir şekilde al
    const metrics: DeviceMetricDataPoint[] = (deviceData as any).metrics || []; 
    
    
    // Grafikler için metrik verilerinin hazırlanması
    const cpuData = metrics.map(m => ({ 
        // 🏆 ZAMAN DÜZELTME: formatTimeLocal fonksiyonunu kullanıyoruz.
        name: formatTimeLocal(m.timestampUtc), 
        value: m.cpuUsagePercent
    }));
    const ramData = metrics.map(m => ({ 
        // 🏆 ZAMAN DÜZELTME
        name: formatTimeLocal(m.timestampUtc), 
        value: m.ramUsagePercent
    }));
    const diskData = metrics.map(m => ({ 
        // 🏆 ZAMAN DÜZELTME
        name: formatTimeLocal(m.timestampUtc), 
        value: m.diskUsagePercent
    }));
    
    // Anlık değerler için en son metriği alma
    const latestCpu = cpuData[cpuData.length - 1]?.value ?? 0;
    const latestRam = ramData[ramData.length - 1]?.value ?? 0;
    const latestDisk = diskData[diskData.length - 1]?.value ?? 0;

    return (
        <div className="space-y-6 p-4">
            <h1 className="text-2xl font-semibold">{deviceData.hostname} Details</h1>

            <section>
                <h2 className="text-xl font-medium mb-4">Device Information</h2>
                <div className="bg-ms-panel p-6 rounded-2xl border border-ms-border shadow-md text-sm">
                    <p><strong>Device ID:</strong> {deviceId}</p>
                    <p><strong>IP Address:</strong> {deviceData.ipAddress}</p>
                    <p><strong>OS:</strong> {deviceData.os}</p>
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