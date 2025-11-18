import type { Device } from "../types";

const API_BASE = "http://localhost:5102/api";

export const apiClient = {
  async getDashboard(): Promise<{
    totalDevices: number;
    online: number;
    offline: number;
    healthy: number;
    warning: number;
    critical: number;
    recentOffline: {
      hostname: string;
      ip: string;
      os: string;
      store: number;
      lastSeen: string;
    }[];
  }> {
    const res = await fetch(`${API_BASE}/dashboard`);
    if (!res.ok) throw new Error("Failed to load dashboard");
    return res.json();
  },

  async getDevices(): Promise<Device[]> {
    const res = await fetch(`${API_BASE}/devices/inventory`);
    if (!res.ok) throw new Error("Failed to load device list");
    return res.json();
  },

  async getDevice(id: string): Promise<Device> {
    const res = await fetch(`${API_BASE}/devices/${id}`);
    if (!res.ok) throw new Error("Device not found");
    return res.json();
  },

  async getDeviceMetrics(id: string): Promise<{
    cpu: number[];
    ram: number[];
    disk: number[];
  }> {
    // Backend hazır değilse fake data dön (şimdilik)
    return {
      cpu: Array.from({ length: 20 }, () => Math.floor(Math.random() * 100)),
      ram: Array.from({ length: 20 }, () => Math.floor(Math.random() * 100)),
      disk: Array.from({ length: 20 }, () => Math.floor(Math.random() * 100))
    };
  }
};
