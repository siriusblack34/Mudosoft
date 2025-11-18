import React, { useEffect, useState } from "react";
import StatCard from "../components/common/StatCard";
import { apiClient } from "../lib/apiClient";

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

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      try {
        const res = await apiClient.getDashboard();

        if (!cancelled) {
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
        }
      } catch (err) {
        console.error(err);
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    load();
    const id = setInterval(load, 30000);

    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);

  // 🔎 Store Discovery Trigger
  const runStoreDiscovery = async () => {
    const storeCode = prompt("Store Code gir (Örn: 7)");
    if (!storeCode) return;

    setScanning(true);
    try {
      const res = await fetch("http://localhost:5102/api/discovery/store", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ storeCode: Number(storeCode) })
      });

      if (!res.ok) throw new Error("Discovery request failed.");

      const result = await res.json();
      alert(
        `🔎 Discovery tamam!\n\nStore: ${result.storeCode}\nOnline: ${result.onlineDevices.length}\nOffline: ${result.offlineDevices.length}`
      );
    } catch (err) {
      console.error(err);
      alert("Discovery başarısız ❌");
    } finally {
      setScanning(false);
    }
  };

  if (loading && !data) return <div>Loading dashboard…</div>;
  if (!data) return <div>Could not load dashboard.</div>;

  return (
    <div className="space-y-6">
      {/* Header + Discovery Button */}
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Dashboard</h1>

        <button
          onClick={runStoreDiscovery}
          disabled={scanning}
          className="px-3 py-2 text-xs rounded-lg border border-ms-border hover:bg-ms-bg-soft disabled:opacity-50"
        >
          {scanning ? "⏳ Tarama yapılıyor..." : "🔎 Store Tarama (Discovery)"}
        </button>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-1 md:grid-cols-6 gap-4">
        <StatCard label="Total Devices" value={data.totalDevices} icon={<span>🖥️</span>} />
        <StatCard label="Online" value={data.online} tone="success" icon={<span>✅</span>} />
        <StatCard label="Offline" value={data.offline} tone="danger" icon={<span>⛔</span>} />

        <StatCard label="Healthy" value={data.healthy} tone="success" icon={<span>🟢</span>} />
        <StatCard label="Warning" value={data.warning} tone="default" icon={<span>🟡</span>} />
        <StatCard label="Critical" value={data.critical} tone="danger" icon={<span>🔴</span>} />
      </div>

      {/* Recently Offline */}
      <section className="mt-4">
        <div className="mb-3 text-sm font-medium text-ms-text-muted">
          Recently Offline Devices
        </div>

        <div className="overflow-hidden rounded-2xl border border-ms-border bg-ms-panel">
          <table className="w-full text-sm">
            <thead className="bg-ms-bg-soft text-ms-text-muted">
              <tr>
                <th className="text-left px-4 py-2">Hostname</th>
                <th className="text-left px-4 py-2">IP Address</th>
                <th className="text-left px-4 py-2">OS</th>
                <th className="text-left px-4 py-2">Store</th>
                <th className="text-left px-4 py-2">Last Seen</th>
              </tr>
            </thead>

            <tbody>
              {data.recentOffline.length > 0 ? (
                data.recentOffline.map((d, i) => (
                  <tr key={i} className="border-t border-ms-border/60">
                    <td className="px-4 py-2 font-medium">{d.hostname}</td>
                    <td className="px-4 py-2">{d.ip}</td>
                    <td className="px-4 py-2">{d.os}</td>
                    <td className="px-4 py-2">{d.store}</td>
                    <td className="px-4 py-2 text-ms-text-muted">{d.lastSeen}</td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={5} className="px-4 py-6 text-center text-ms-text-muted text-sm">
                    No recently offline devices 🎉
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
};

export default DashboardPage;
