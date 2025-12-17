// frontend/src/lib/apiClient.ts

import type { Device } from "../types";

// ==========================
// TYPES
// ==========================
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

// ✅ SQL QUERY – ENVANTER + STATUS
export interface SqlDeviceWithStatus {
    deviceId: string;
    storeCode: number;
    storeName: string;
    deviceType: string;
    deviceName: string;
    calculatedIpAddress: string;
    isOnline: boolean;
}

// ==========================
// BASE CONFIG
// ==========================
const API_BASE = "http://localhost:5102";

function buildUrl(url: string) {
    const cleanUrl = url.startsWith("/") ? url.substring(1) : url;
    const base = API_BASE.endsWith("/") ? API_BASE.slice(0, -1) : API_BASE;
    return `${base}/${cleanUrl}`;
}

// ==========================
// API CLIENT
// ==========================
export const apiClient = {
    async get<T>(url: string): Promise<T> {
        const res = await fetch(buildUrl(url));
        if (!res.ok) {
            throw new Error(`GET ${url} failed: ${res.status}`);
        }
        return res.json();
    },

    async post<T>(url: string, data?: any): Promise<T> {
        const res = await fetch(buildUrl(url), {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: data ? JSON.stringify(data) : undefined,
        });
        if (!res.ok) {
            throw new Error(`POST ${url} failed: ${res.status}`);
        }
        return res.json();
    },

    // ==========================
    // DEVICES (ENVANTER)
    // ==========================
    getDevices(): Promise<Device[]> {
        return this.get("/api/devices/inventory");
    },
    
    getDevice(id: string): Promise<Device> {
        return this.get(`/api/devices/${id}`);
    },

    // ==========================
    // SQL QUERY – ✅ TEK DOĞRU KAYNAK
    // ==========================
    getSqlDevicesWithStatus(params?: {
        timeoutMs?: number;
        maxConcurrency?: number;
    }): Promise<SqlDeviceWithStatus[]> {
        const qs = new URLSearchParams();
        if (params?.timeoutMs) qs.append("timeoutMs", String(params.timeoutMs));
        if (params?.maxConcurrency) qs.append("maxConcurrency", String(params.maxConcurrency));

        const suffix = qs.toString() ? `?${qs}` : "";
        return this.get(`/api/sqlquery/devices/with-status${suffix}`);
    },

    runSqlQuery(deviceId: string, query: string) {
        return this.post("/api/sqlquery/execute", { deviceId, query });
    },
};
