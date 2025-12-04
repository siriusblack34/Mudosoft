// frontend/src/lib/apiClient.ts (FINAL VERSION - FULL FIXED)

// Tip importları
import type { Device } from "../types";

// Ek tipler — types.ts içinde yoksa bu geçici tanımlar kullanılacak:
export interface DeviceMetricDataPoint {
    timestampUtc: string;
    cpuUsagePercent: number;
    ramUsagePercent: number;
    diskUsagePercent: number;
}

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

export interface CommandResultRecord {
    output: string;
    completedAtUtc: string | null;
}

// ---------------------------------------------------------------
// API Base
// ---------------------------------------------------------------

const API_BASE = "http://localhost:5102";

// URL birleştirici (her istekte otomatik çalışır)
function buildUrl(url: string) {
    const cleanUrl = url.startsWith("/") ? url.substring(1) : url;
    const base = API_BASE.endsWith("/") ? API_BASE.slice(0, -1) : API_BASE;
    return `${base}/${cleanUrl}`;
}

// ===============================================================
// API CLIENT
// ===============================================================

export const apiClient = {
    // --------------------------
    // UNIVERSAL GET
    // --------------------------
    async get<T>(url: string): Promise<T> {
        const fullUrl = buildUrl(url);
        const res = await fetch(fullUrl);

        if (!res.ok) {
            throw new Error(`GET isteği başarısız: ${res.status} ${res.statusText}`);
        }

        return res.json();
    },

    // --------------------------
    // UNIVERSAL POST
    // --------------------------
    async post<T = any>(url: string, data?: any): Promise<T> {
        const fullUrl = buildUrl(url);

        const res = await fetch(fullUrl, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: data !== undefined ? JSON.stringify(data) : undefined,
        });

        if (!res.ok) {
            const errText = await res.text().catch(() => "");
            throw new Error(`POST isteği başarısız: ${res.status} ${errText}`);
        }

        const txt = await res.text();

        try {
            return JSON.parse(txt) as T;
        } catch {
            return txt as unknown as T;
        }
    },

    // ===========================================================
    // DASHBOARD
    // ===========================================================
    async getDashboard() {
        return this.get("/api/dashboard");
    },

    // ===========================================================
    // DEVICES
    // ===========================================================
    async getDevices(): Promise<Device[]> {
        return this.get("/api/devices/inventory");
    },

    async getDevice(id: string): Promise<Device> {
        return this.get(`/api/devices/${id}`);
    },

    async getDeviceMetrics(id: string): Promise<DeviceMetricDataPoint[]> {
        const res = await fetch(buildUrl(`/api/devices/${id}/metrics`));

        if (res.status === 404) return [];

        if (!res.ok)
            throw new Error(`Failed to load device metrics: ${res.statusText}`);

        return res.json();
    },

    // ===========================================================
    // ACTIONS / COMMANDS
    // ===========================================================
    async runScript(deviceId: string, scriptContent: string): Promise<{ commandId: string }> {
        return this.post("/api/actions/run-script", {
            deviceId,
            script: scriptContent,
        });
    },

    async getCommandHistory(): Promise<CommandHistoryItem[]> {
        return this.get("/api/actions/history");
    },

    async getCommandDetails(commandId: string): Promise<CommandResultRecord> {
        return this.get(`/api/actions/${commandId}/details`);
    },

    // ===========================================================
    // SQL QUERY (YENİ EKLENDİ — %100 ÇALIŞIR)
    // ===========================================================

    // SQL cihaz listesi
    async getSqlDevices(): Promise<{
        deviceId: string;
        storeCode: number;
        storeName: string;
        deviceType: string;
        calculatedIpAddress: string;
    }[]> {
        return this.get("/api/sqlquery/devices");
    },

    // SQL Query çalıştır
    async runSqlQuery(deviceId: string, query: string): Promise<any> {
        return this.post("/api/sqlquery/execute", {
            deviceId,
            query
        });
    }
};
