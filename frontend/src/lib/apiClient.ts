// frontend/src/lib/apiClient.ts

import type {
    Device, DashboardSummary, OfflineLogEntry, OfflineLogStats, CollectorReportEntry, EventLogEntry,
    OutageMailTemplateGroup, OutageMailRequest, OutageMailPreview, OutageMailSendResult,
} from "../types";

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

export interface DeviceOfflineExclusionResponse {
    deviceId: string;
    excludeFromOfflineList: boolean;
}

export interface DeviceTemporaryCloseResponse {
    deviceId: string;
    isTemporarilyClosed: boolean;
    temporaryCloseReason?: string | null;
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
    /** ICMP Ping sonucu (null = kontrol yapilmadi) */
    pingReachable: boolean | null;
    /** TCP 1433 SQL sonucu (null = kontrol yapilmadi, ornegin router) */
    sqlReachable: boolean | null;
    lastSeen: string | null;
    isTemporarilyClosed: boolean;
    temporaryCloseReason: string | null;
}

// ✅ NETWORK DIAGNOSTICS
export interface StoreDiagnostic {
    storeCode: number;
    storeName: string;
    type: 'FullOutage' | 'InternalNetwork' | 'RouterFlapping' | 'DeviceFlapping' | 'PartialOutage' | 'StoreFlapping';
    severity: 'Critical' | 'Warning' | 'Info';
    title: string;
    message: string;
    detectedAt: string;
    routerOnline: boolean;
    totalDevices: number;
    onlineDevices: number;
    offlineDevices: number;
    flappingCount: number;
    affectedDevices: string[];
}

export interface DiagnosticsResponse {
    summary: {
        analyzedAt: string;
        totalStores: number;
        issues: number;
        critical: number;
        warning: number;
        byType: { type: string; count: number }[];
    };
    diagnostics: StoreDiagnostic[];
}

// ✅ ROUTER LINE DIAGNOSTICS (karasal / 4.5G tespiti)
export type RouterLineClass = 'Unknown' | 'Terrestrial' | 'MobileSuspected' | 'MobileLikely' | 'Unstable';

export interface RouterClassification {
    deviceId: string;
    storeCode: number;
    storeName: string;
    ip: string;
    class: RouterLineClass;
    reason: string;
    sampleCount: number;
    successRate: number;
    avgRttMs: number | null;
    p50Ms: number;
    p95Ms: number;
    stdDevMs: number;
    lastSampleAt: string | null;
    prevAvgRttMs: number | null;
    switchoverDetected: boolean;
    terrestrialMbps: number | null;
    lineType: string | null;
}

export interface RouterDiagnosticsSummary {
    analyzedAt: string;
    windowMinutes: number;
    total: number;
    terrestrial: number;
    mobileSuspected: number;
    mobileLikely: number;
    unstable: number;
    unknown: number;
    switchovers: number;
}

export interface RouterDiagnosticsResponse {
    summary: RouterDiagnosticsSummary;
    routers: RouterClassification[];
}

export interface RouterLatencyPoint {
    sampledAt: string;
    rttMs: number | null;
    success: boolean;
}

export interface RouterLatencyHistory {
    storeCode: number;
    hours: number;
    points: RouterLatencyPoint[];
}

export interface StoreNetworkInfo {
    storeCode: number;
    terrestrialMbps: number;
    lineType: string | null;
    notes: string | null;
    updatedAt: string;
}

export interface StoreTimelineResponse {
    summary: {
        storeCode: number;
        periodHours: number;
        totalEvents: number;
        deviceCount: number;
        routerEvents: number;
        pcEvents: number;
        kasaEvents: number;
    };
    timeline: {
        deviceId: string;
        deviceType: string;
        changes: { isOnline: boolean; changedAt: string }[];
        totalChanges: number;
        lastStatus: boolean;
    }[];
}

// ✅ STORE MANAGERS
export interface StoreManager {
    id: number;
    storeCode: number;
    actualStoreCode?: number | null;
    storeName: string;
    fullName: string;
    phone: string;
}

export interface StoreOutageSummary {
    storeCode: number;
    storeName: string;
    incidentCount: number;
    totalOfflineMinutes: number;
    averageOfflineMinutes: number;
    lastOfflineAt: string | null;
    lastOnlineAt: string | null;
    isCurrentlyOffline: boolean;
    maxOfflineKasaCount: number;
}

export interface StoreOutageIncident {
    id: number;
    storeCode: number;
    storeName: string;
    offlineKasaCount: number;
    offlineAt: string;
    onlineAt: string | null;
    durationMinutes: number | null;
    isStillOffline: boolean;
}

export interface StoreOutageReport {
    periodDays: number;
    generatedAtUtc: string;
    totalIncidents: number;
    currentlyOfflineStoreCount: number;
    summary: StoreOutageSummary[];
    incidents: StoreOutageIncident[];
}

export interface HardwareInventoryRow {
    deviceId: string;
    hostname: string;
    storeCode: number;
    storeName: string | null;
    type: string;
    ipAddress: string;
    os: string;
    agentVersion: string | null;
    cpuModel: string | null;
    gpuModel: string | null;
    totalRamMB: number;
    totalDiskGB: number;
    totalDiskDGB: number | null;
    cpuUsagePercent: number;
    ramUsagePercent: number;
    diskUsagePercent: number;
    diskDUsagePercent: number | null;
    healthStatus: string;
    healthScore: number;
    online: boolean;
    lastSeen: string | null;
    lastLoggedInUser: string | null;
    systemBootTime: string | null;
    vncInstalled: boolean;
}

export interface HardwareInventoryReport {
    generatedAtUtc: string;
    totalDevices: number;
    onlineDevices: number;
    criticalDevices: number;
    rows: HardwareInventoryRow[];
}

export interface FaultDensityStore {
    storeCode: number;
    storeName: string;
    deviceCount: number;
    currentOfflineDevices: number;
    criticalDevices: number;
    warningDevices: number;
    closedDevices: number;
    devicesWithIssues: number;
    incidentCount: number;
    totalOfflineMinutes: number;
    lastOfflineAt: string | null;
    faultScore: number;
}

export interface FaultDensityDevice {
    deviceId: string;
    hostname: string;
    storeCode: number;
    storeName: string;
    type: string;
    online: boolean;
    isTemporarilyClosed: boolean;
    healthStatus: string;
    healthScore: number;
    cpuUsagePercent: number;
    ramUsagePercent: number;
    diskUsagePercent: number;
    lastSeen: string | null;
    issueScore: number;
    issueReasons: string[];
}

export interface FaultDensityReport {
    periodDays: number;
    generatedAtUtc: string;
    stores: FaultDensityStore[];
    devices: FaultDensityDevice[];
}

export interface StartOfflineServicesResponse {
    jobId?: string;
    totalOffline: number;
    attempted: number;
    pingReachable: number;
    startIssued: number;
    runningConfirmed: number;
    completedAtUtc?: string;
    results: StartOfflineServiceResult[];
}

export interface StartOfflineServiceResult {
    deviceId: string;
    hostname: string;
    ipAddress: string;
    storeCode: number;
    pingReachable: boolean;
    startIssued: boolean;
    runningConfirmed: boolean;
    status: string;
    message: string;
}


// ==========================
// BASE CONFIG
// ==========================
export const API_BASE_URL = import.meta.env.VITE_API_BASE
    || `http://${window.location.hostname}:5102`;
const API_BASE = API_BASE_URL;
const DEFAULT_TIMEOUT_MS = 30_000;

function buildUrl(url: string) {
    const cleanUrl = url.startsWith("/") ? url.substring(1) : url;
    const base = API_BASE.endsWith("/") ? API_BASE.slice(0, -1) : API_BASE;
    return `${base}/${cleanUrl}`;
}

function getAuthHeaders(): Record<string, string> {
    const token = localStorage.getItem("token");
    if (!token) return {};

    // Check token expiry
    const expiresAt = localStorage.getItem("tokenExpiresAt");
    if (expiresAt && new Date(expiresAt) < new Date()) {
        localStorage.removeItem("token");
        localStorage.removeItem("tokenExpiresAt");
        localStorage.removeItem("isAuthenticated");
        window.location.href = "/login";
        return {};
    }

    return { Authorization: `Bearer ${token}` };
}

async function fetchWithTimeout(
    url: string,
    options: RequestInit,
    timeoutMs: number = DEFAULT_TIMEOUT_MS
): Promise<Response> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), timeoutMs);
    try {
        const res = await fetch(url, { ...options, signal: controller.signal });
        // Handle 401 - redirect to login
        if (res.status === 401) {
            localStorage.removeItem("token");
            localStorage.removeItem("tokenExpiresAt");
            localStorage.removeItem("isAuthenticated");
            window.location.href = "/login";
        }
        return res;
    } finally {
        clearTimeout(timeout);
    }
}

// ==========================
// API CLIENT
// ==========================
export const apiClient = {
    async get<T>(url: string, timeoutMs?: number): Promise<T> {
        const res = await fetchWithTimeout(buildUrl(url), {
            headers: { ...getAuthHeaders() },
        }, timeoutMs);
        if (!res.ok) {
            let errorMessage = `GET ${url} failed: ${res.status}`;
            try {
                const errorBody = await res.json();
                if (errorBody?.error) errorMessage = errorBody.error;
            } catch { /* response body not JSON */ }
            throw new Error(errorMessage);
        }
        return res.json();
    },

    async post<T>(url: string, data?: unknown, timeoutMs?: number): Promise<T> {
        const res = await fetchWithTimeout(buildUrl(url), {
            method: "POST",
            headers: { "Content-Type": "application/json", ...getAuthHeaders() },
            body: data ? JSON.stringify(data) : undefined,
        }, timeoutMs);
        if (!res.ok) {
            let errorMessage = `POST ${url} failed: ${res.status}`;
            try {
                const errorBody = await res.json();
                if (errorBody?.error) errorMessage = errorBody.error;
            } catch { /* response body not JSON */ }
            throw new Error(errorMessage);
        }
        return res.json();
    },

    async put<T>(url: string, data?: unknown): Promise<T> {
        const res = await fetchWithTimeout(buildUrl(url), {
            method: "PUT",
            headers: { "Content-Type": "application/json", ...getAuthHeaders() },
            body: data ? JSON.stringify(data) : undefined,
        });
        if (!res.ok) {
            let errorMessage = `PUT ${url} failed: ${res.status}`;
            try {
                const errorBody = await res.json();
                if (errorBody?.error) errorMessage = errorBody.error;
            } catch { /* response body not JSON */ }
            throw new Error(errorMessage);
        }
        if (res.status === 204) return null as T;
        return res.json();
    },

    async delete<T>(url: string): Promise<T> {
        const res = await fetchWithTimeout(buildUrl(url), {
            method: "DELETE",
            headers: { ...getAuthHeaders() },
        });
        if (!res.ok) {
            let errorMessage = `DELETE ${url} failed: ${res.status}`;
            try {
                const errorBody = await res.json();
                if (errorBody?.error) errorMessage = errorBody.error;
            } catch { /* response body not JSON */ }
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

    getDeviceMetrics(id: string): Promise<DeviceMetricDataPoint[]> {
        return this.get(`/api/devices/${id}/metrics`);
    },

    deleteDevice(id: string): Promise<{ success: boolean; deletedDeviceId: string }> {
        return this.delete(`/api/devices/${encodeURIComponent(id)}`);
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
        return this.get(`/api/sqlquery/devices/with-status${suffix}`, 90_000);
    },

    runSqlQuery(deviceId: string, query: string) {
        return this.post("/api/sqlquery/execute", { deviceId, query }, 120_000);
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

    listServices(deviceId: string): Promise<{ commandId: string }> {
        return this.post("/api/actions/list-services", { deviceId });
    },

    folderCleanup(deviceId: string, path: string): Promise<{ commandId: string }> {
        return this.post("/api/actions/folder-cleanup", { deviceId, path });
    },

    uploadFile(deviceId: string, file: File, remotePath: string): Promise<{ commandId: string }> {
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
    getDashboard(): Promise<DashboardSummary> {
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

    // ==========================
    // OUTAGE MAIL (arıza bildirimi)
    // ==========================
    getOutageMailTemplates(): Promise<OutageMailTemplateGroup[]> {
        return this.get("/api/outage-mail/templates");
    },
    previewOutageMail(req: OutageMailRequest): Promise<OutageMailPreview> {
        return this.post("/api/outage-mail/preview", req);
    },
    sendOutageMail(req: OutageMailRequest): Promise<OutageMailSendResult> {
        return this.post("/api/outage-mail/send", req);
    },

    getOfflineLogs(days = 7, storeCode?: number): Promise<OfflineLogEntry[]> {
        const qs = new URLSearchParams({ days: String(days) });
        if (storeCode) qs.append("storeCode", String(storeCode));
        return this.get(`/api/sqlquery/offline-logs?${qs}`);
    },

    getOfflineStats(days = 30): Promise<OfflineLogStats[]> {
        return this.get(`/api/sqlquery/offline-logs/stats?days=${days}`);
    },

    getStoreOutageReport(days = 30): Promise<StoreOutageReport> {
        return this.get(`/api/reports/store-outages?days=${days}`);
    },

    getHardwareInventoryReport(): Promise<HardwareInventoryReport> {
        return this.get("/api/reports/hardware-inventory", 60_000);
    },

    getFaultDensityReport(days = 30): Promise<FaultDensityReport> {
        return this.get(`/api/reports/fault-density?days=${days}`);
    },

    toggleTemporaryClose(deviceId: string, isClosed: boolean, reason?: string): Promise<{ success: boolean; deviceId: string; isTemporarilyClosed: boolean }> {
        return this.put(`/api/sqlquery/devices/${encodeURIComponent(deviceId)}/temporary-close`, { isClosed, reason });
    },

    setDeviceOfflineExclusion(deviceId: string, excludeFromOfflineList: boolean): Promise<DeviceOfflineExclusionResponse> {
        return this.put(`/api/devices/${encodeURIComponent(deviceId)}/offline-exclusion`, { excludeFromOfflineList });
    },

    setDeviceTemporaryClose(deviceId: string, isClosed: boolean, reason?: string): Promise<DeviceTemporaryCloseResponse> {
        return this.put(`/api/devices/${encodeURIComponent(deviceId)}/temporary-close`, { isClosed, reason });
    },

    deleteStoreDevice(deviceId: string): Promise<{ success: boolean; deletedDeviceId: string }> {
        return this.delete(`/api/sqlquery/devices/${encodeURIComponent(deviceId)}`);
    },

    // ==========================
    // NETWORK DIAGNOSTICS
    // ==========================
    getActiveDiagnostics(windowMinutes = 30, flappingThreshold = 4): Promise<DiagnosticsResponse> {
        return this.get(`/api/diagnostics/active?windowMinutes=${windowMinutes}&flappingThreshold=${flappingThreshold}`);
    },

    getStoreTimeline(storeCode: number, hours = 24): Promise<StoreTimelineResponse> {
        return this.get(`/api/diagnostics/store/${storeCode}/timeline?hours=${hours}`);
    },

    // ==========================
    // ROUTER LINE DIAGNOSTICS (karasal / 4.5G)
    // ==========================
    getRouterDiagnostics(windowMinutes = 10): Promise<RouterDiagnosticsResponse> {
        return this.get(`/api/router-diagnostics/current?windowMinutes=${windowMinutes}`);
    },

    getRouterLatencyHistory(storeCode: number, hours = 24): Promise<RouterLatencyHistory> {
        return this.get(`/api/router-diagnostics/store/${storeCode}/history?hours=${hours}`);
    },

    getStoreNetworkInfo(): Promise<StoreNetworkInfo[]> {
        return this.get(`/api/router-diagnostics/network-info`);
    },

    updateStoreNetworkInfo(storeCode: number, data: { terrestrialMbps: number; lineType?: string | null; notes?: string | null }): Promise<StoreNetworkInfo> {
        return this.put(`/api/router-diagnostics/network-info/${storeCode}`, data);
    },

    // ==========================
    // COLLECTOR / HEALTH
    // ==========================
    getCollectorData(deviceId: string, collector?: string, limit = 50): Promise<CollectorReportEntry[]> {
        const qs = new URLSearchParams({ limit: String(limit) });
        if (collector) qs.append("collector", collector);
        return this.get(`/api/agent/collector-report/${encodeURIComponent(deviceId)}?${qs}`);
    },

    getCollectorLatest(deviceId: string): Promise<CollectorReportEntry[]> {
        return this.get(`/api/agent/collector-report/${encodeURIComponent(deviceId)}/latest`);
    },

    getEventLogs(deviceId: string, limit = 100): Promise<EventLogEntry[]> {
        return this.get(`/api/agent/collector-report/${encodeURIComponent(deviceId)}/eventlogs?limit=${limit}`);
    },

    // ==========================
    // OFFLINE SERVICES
    // ==========================
    startOfflineServices(): Promise<StartOfflineServicesResponse> {
        return this.post("/api/devices/start-offline-services");
    },

    getOfflineServiceStartStatus(jobId: string): Promise<StartOfflineServicesResponse> {
        return this.get(`/api/devices/start-offline-services/${encodeURIComponent(jobId)}`);
    },

    getBaseUrl(): string {
        return API_BASE_URL;
    },

    /** Backend health check — herhangi bir HTTP yanıtı = sunucu ayakta. 401 redirect yapmaz. */
    async checkBackendHealth(): Promise<boolean> {
        try {
            await fetch(buildUrl("/api/dashboard/summary"), {
                headers: { ...getAuthHeaders() },
                signal: AbortSignal.timeout(5000),
            });
            return true;
        } catch {
            return false;
        }
    },

    // ==========================
    // AGENT UPDATES
    // ==========================
    getLatestVersion(): Promise<{ version: string; fileName?: string; uploadedAt?: string; sizeBytes?: number; message?: string }> {
        return this.get("/api/updates/latest");
    },

    getDeviceVersions(): Promise<{ id: string; hostname: string; online: boolean; agentVersion: string | null; storeCode: string | null; lastSeen: string | null }[]> {
        return this.get("/api/updates/device-versions");
    },

    getUpdateHistory(): Promise<{ version: string; fileName: string; uploadedAt: string; sizeBytes: number }[]> {
        return this.get("/api/updates/history");
    },

    getBuildStatus(): Promise<{ isBuilding: boolean; status: string; error: string }> {
        return this.get("/api/updates/build-status");
    },

    async uploadAgentPackage(file: File, version: string): Promise<{ message: string }> {
        const formData = new FormData();
        formData.append('file', file);
        formData.append('version', version);
        const res = await fetchWithTimeout(buildUrl("/api/updates/upload"), {
            method: 'POST',
            headers: { ...getAuthHeaders() },
            body: formData,
        });
        if (!res.ok) throw new Error(`Upload failed: ${res.status}`);
        return res.json();
    },

    triggerAllUpdates(backendUrl: string): Promise<{ message: string }> {
        return this.post(`/api/updates/trigger-all?backendUrl=${encodeURIComponent(backendUrl)}`);
    },

    buildNewAgent(): Promise<void> {
        return this.post("/api/updates/build");
    },
};
