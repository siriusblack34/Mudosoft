// frontend/src/lib/apiClient.ts — FINAL VERSION

import type { Device } from "../types";

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

const API_BASE = "http://localhost:5102";

function buildUrl(url: string) {
    const cleanUrl = url.startsWith("/") ? url.substring(1) : url;
    const base = API_BASE.endsWith("/") ? API_BASE.slice(0, -1) : API_BASE;
    return `${base}/${cleanUrl}`;
}

export const apiClient = {
    async get<T>(url: string): Promise<T> {
        const res = await fetch(buildUrl(url));
        if (!res.ok) throw new Error(`GET failed: ${res.status}`);
        return res.json();
    },

    async post<T>(url: string, data?: any): Promise<T> {
        const res = await fetch(buildUrl(url), {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: data ? JSON.stringify(data) : undefined,
        });
        if (!res.ok) throw new Error(`POST failed: ${res.status}`);
        return res.json();
    },

    // ==========================
    // DEVICES
    // ==========================
    getDevices(): Promise<Device[]> {
        return this.get("/api/devices/inventory");
    },

    getDevice(id: string): Promise<Device> {
        return this.get(`/api/devices/${id}`);
    },

    getDeviceMetrics(id: string): Promise<DeviceMetricDataPoint[]> {
        return this.get(`/api/devices/${id}/metrics`);
    },

    // ==========================
    // COMMANDS
    // ==========================
    runScript(deviceId: string, scriptContent: string) {
        return this.post("/api/actions/run-script", {
            deviceId,
            script: scriptContent,
        });
    },

    getCommandHistory(): Promise<CommandHistoryItem[]> {
        return this.get("/api/actions/history");
    },

    getCommandDetails(commandId: string): Promise<CommandResultRecord> {
        return this.get(`/api/actions/${commandId}/details`);
    },

    // ==========================
    // SQL QUERY
    // ==========================
    getSqlDevices() {
        return this.get("/api/sqlquery/devices");
    },

    runSqlQuery(deviceId: string, query: string) {
        return this.post("/api/sqlquery/execute", { deviceId, query });
    },

    // ⭐ Yeni eklendi: Offline teşhis
    getDeviceStatus(deviceId: string) {
        return this.get(`/api/sqlquery/device-status/${deviceId}`);
    }
};
