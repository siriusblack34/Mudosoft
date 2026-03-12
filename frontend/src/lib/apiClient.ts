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

export interface ExecuteSqlQueryRequest {
    deviceId: string;
    query: string;
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
    lastSeen: string | null; // ISO datetime, last time device was confirmed online
}

// ✅ STORE MANAGERS
export interface StoreManager {
    id: number;
    storeCode: number;
    storeName: string;
    fullName: string;
    phone: string;
}


// ==========================
// BASE CONFIG
// ==========================
export const API_BASE_URL = `http://${window.location.hostname}:5102`;
const API_BASE = API_BASE_URL;

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
            let errorMessage = `GET ${url} failed: ${res.status}`;
            try {
                const errorBody = await res.json();
                if (errorBody?.error) errorMessage = errorBody.error;
            } catch { } // ignore
            throw new Error(errorMessage);
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
            let errorMessage = `POST ${url} failed: ${res.status}`;
            try {
                const errorBody = await res.json();
                if (errorBody?.error) errorMessage = errorBody.error;
            } catch { } // ignore
            throw new Error(errorMessage);
        }
        return res.json();
    },

    async put<T>(url: string, data?: any): Promise<T> {
        const res = await fetch(buildUrl(url), {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: data ? JSON.stringify(data) : undefined,
        });
        if (!res.ok) {
            let errorMessage = `PUT ${url} failed: ${res.status}`;
            try {
                const errorBody = await res.json();
                if (errorBody?.error) errorMessage = errorBody.error;
            } catch { } // ignore
            throw new Error(errorMessage);
        }
        if (res.status === 204) return null as T;
        return res.json();
    },

    async delete<T>(url: string): Promise<T> {
        const res = await fetch(buildUrl(url), {
            method: "DELETE",
        });
        if (!res.ok) {
            let errorMessage = `DELETE ${url} failed: ${res.status}`;
            try {
                const errorBody = await res.json();
                if (errorBody?.error) errorMessage = errorBody.error;
            } catch { } // ignore
            throw new Error(errorMessage);
        }
        if (res.status === 204) return null as T;
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

    getDeviceSystemInfo(deviceId: string): Promise<{ hostname: string | null; serialNumber: string | null; hostnameError?: string; serialError?: string }> {
        return this.get(`/api/sqlquery/devices/${deviceId}/system-info`);
    },

    // ==========================
    // SCRIPTS
    // ==========================
    runScript(deviceId: string, script: string): Promise<{ commandId: string }> {
        return this.post("/api/actions/run-script", { deviceId, script });
    },

    folderCleanup(deviceId: string, path: string): Promise<{ commandId: string }> {
        return this.post("/api/actions/folder-cleanup", { deviceId, path });
    },

    uploadFile(deviceId: string, file: File, remotePath: string): Promise<any> {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = async () => {
                try {
                    const base64Content = (reader.result as string).split(',')[1];
                    const p = this.post(`/api/agent/files/upload?deviceId=${deviceId}`, { path: remotePath + file.name, content: base64Content });
                    resolve(p);
                } catch (e) { reject(e); }
            };
            reader.onerror = error => reject(error);
            reader.readAsDataURL(file);
        });
    },

    // ==========================
    // COMMAND HISTORY
    // ==========================
    getCommandHistory(): Promise<CommandHistoryItem[]> {
        return this.get("/api/agent/command-history").catch(() => []); // Fallback to empty if endpoint missing
    },

    getCommandDetails(commandId: string): Promise<CommandResultRecord> {
        return this.get(`/api/agent/command-results/${commandId}`);
    },

    // ==========================
    // DASHBOARD
    // ==========================
    getDashboard(): Promise<any> {
        return this.get("/api/dashboard/summary");
    },

    // ==========================
    // STORE MANAGERS
    // ==========================
    getStoreManagers(): Promise<StoreManager[]> {
        return this.get("/api/storemanagers");
    },

    importStoreManagers(rawText: string): Promise<{ success: boolean; count: number }> {
        return this.post("/api/storemanagers/import", { rawText });
    },

    deleteStoreDevice(deviceId: string): Promise<{ success: boolean; deletedDeviceId: string }> {
        return this.delete(`/api/sqlquery/devices/${encodeURIComponent(deviceId)}`);
    },

    getBaseUrl(): string {
        return API_BASE_URL;
    }
};

