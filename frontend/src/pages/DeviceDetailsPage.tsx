import React, { useEffect, useState } from "react";
import { useParams, Link } from "react-router-dom";
import { apiClient } from "../lib/apiClient";
import type { Device } from "../types";
import {
  LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer
} from "recharts";

interface DeviceDetailsState {
  device: Device | null;
  metrics: {
    cpu: number[];
    ram: number[];
    disk: number[];
  } | null;
  loading: boolean;
  error?: string;
}

const DeviceDetailsPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const [state, setState] = useState<DeviceDetailsState>({
    device: null,
    metrics: null,
    loading: true
  });

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      if (!id) return;
      try {
        const device = await apiClient.getDevice(id);
        const metrics = await apiClient.getDeviceMetrics(id);

        if (!cancelled) {
          setState({
            device,
            metrics,
            loading: false
          });
        }
      } catch (err: any) {
        if (!cancelled) {
          setState({
            device: null,
            metrics: null,
            loading: false,
            error: err.message ?? "Failed to load device"
          });
        }
      }
    };

    load();
    const timer = setInterval(load, 8000); // her 8 saniyede güncelle

    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [id]);

  const { device, metrics, loading, error } = state;

  if (loading) return <div className="p-4">Loading device...</div>;
  if (error) return <div className="p-4 text-red-400">{error}</div>;
  if (!device) return <div className="p-4">Device not found.</div>;

  // Grafik için format
  const chartData =
    metrics?.cpu.map((_, i) => ({
      name: i.toString(),
      cpu: metrics.cpu[i],
      ram: metrics.ram[i],
      disk: metrics.disk[i]
    })) ?? [];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">{device.hostname}</h1>
        <Link to="/devices" className="text-ms-primary underline">
          ← Back to Devices
        </Link>
      </div>

      {/* Device Info */}
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3 text-sm p-4 rounded-xl bg-ms-panel border border-ms-border">
        <div><strong>IP:</strong> {device.ipAddress}</div>
        <div><strong>OS:</strong> {device.os}</div>
        <div><strong>Store:</strong> {device.storeCode}</div>
        <div><strong>Type:</strong> {device.type}</div>
        <div><strong>SQL:</strong> {device.sqlVersion ?? "N/A"}</div>
        <div><strong>POS:</strong> {device.posVersion ?? "N/A"}</div>
        <div><strong>Online:</strong> {device.online ? "🟢 Online" : "🔴 Offline"}</div>
        <div><strong>Last Seen:</strong> {device.lastSeen}</div>
        <div><strong>Agent:</strong> {device.agentVersion ?? "N/A"}</div>
      </div>

      {/* Metrics Charts */}
      <div className="grid md:grid-cols-3 gap-4">
        {["CPU", "RAM", "Disk"].map((label, idx) => (
          <div key={label} className="p-4 rounded-xl bg-ms-panel border border-ms-border">
            <div className="text-sm font-medium mb-2">{label} Usage</div>
            <ResponsiveContainer width="100%" height={180}>
              <LineChart data={chartData}>
                <XAxis dataKey="name" hide />
                <YAxis domain={[0, 100]} />
                <Tooltip />
                <Line
                  type="monotone"
                  dataKey={label.toLowerCase()}
                  stroke="#4ade80"
                  strokeWidth={2}
                  dot={false}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        ))}
      </div>
    </div>
  );
};

export default DeviceDetailsPage;
