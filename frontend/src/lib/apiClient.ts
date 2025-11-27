import type { Device } from "../types"; // ✅ Import eklendi

const API_BASE = "http://localhost:5102/api";

// Cihaz Metrikleri için Arayüz
export interface DeviceMetricDataPoint {
  timestampUtc: string;
  cpuUsagePercent: number;
  ramUsagePercent: number;
  diskUsagePercent: number;
}

// Komut Geçmişi için Arayüz
export interface CommandHistoryItem {
  commandId: string;
  deviceId: string;
  hostname: string;
  type: number;
  typeName: string;
  success: boolean;
  completedAtUtc: string;
  outputSnippet: string;
}

export const apiClient = {
  // 1. Dashboard Bilgileri
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

  // 2. Tüm Cihaz Listesi
  async getDevices(): Promise<Device[]> {
    const res = await fetch(`${API_BASE}/devices/inventory`);
    if (!res.ok) throw new Error("Failed to load device list");
    return res.json();
  },

  // 3. Tek Cihaz Detayı (Metriklerle Birlikte)
  async getDevice(id: string): Promise<Device> { // ✅ Tip düzeltildi
    const res = await fetch(`${API_BASE}/devices/${id}`);
    if (!res.ok) throw new Error("Device not found");
    return res.json();
  },

  // 4. Metrik Geçmişi (Opsiyonel)
  async getDeviceMetrics(id: string): Promise<DeviceMetricDataPoint[]> {
    const res = await fetch(`${API_BASE}/devices/${id}/metrics`);
    
    if (res.status === 404) {
      return []; 
    }
    
    if (!res.ok) {
      throw new Error(`Failed to load device metrics: ${res.statusText}`);
    }

    return res.json();
  },

  // 5. Run Script (Uzaktan Betik Çalıştırma)
  async runScript(deviceId: string, scriptContent: string): Promise<{ commandId: string }> {
    const res = await fetch(`${API_BASE}/actions/run-script`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ deviceId: deviceId, script: scriptContent })
    });

    if (!res.ok) {
        const errorText = await res.text();
        throw new Error(`Script çalıştırma başarısız: ${res.status} ${errorText}`);
    }

    return res.json();
  },
  
  // 6. Komut Geçmişi Listesi
  async getCommandHistory(): Promise<CommandHistoryItem[]> {
    const res = await fetch(`${API_BASE}/actions/history`);
    
    if (!res.ok) {
      throw new Error(`Failed to load command history: ${res.statusText}`);
    }

    return res.json();
  },

  // 7. Komut Çıktı Detayları
  async getCommandDetails(commandId: string): Promise<any> {
    const res = await fetch(`${API_BASE}/actions/${commandId}/details`);
    
    if (!res.ok) {
      throw new Error(`Failed to load command details: ${res.statusText}`);
    }

    return res.json();
  }
};