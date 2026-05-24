// frontend/src/lib/apiClient.ts

import type {
    Device, DashboardSummary, OfflineLogEntry, OfflineLogStats, CollectorReportEntry, EventLogEntry, EventLogAnalysisResult, EventLogPullResult,
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
    address?: string | null;
}

export interface StoreAddressSyncResult {
    fetched: number;
    updated: number;
    missingCount: number;
    missingCodes: number[];
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

// ✅ REMOTE DESKTOP (VNC)
export interface ActiveVncSession {
    sessionId: string;
    deviceId: string;
    username: string | null;
    targetIp: string;
    startedAt: string;
    durationMinutes: number;
}

export interface VncSessionLog {
    id: number;
    sessionId: string;
    deviceId: string;
    username: string | null;
    targetIp: string;
    startedAtUtc: string;
    endedAtUtc: string | null;
    durationSeconds: number | null;
    disconnectReason: string | null;
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

export interface StoreServiceIncident {
    id: number;
    deviceId: string;
    storeCode: number;
    storeName: string;
    deviceName: string;
    ipAddress: string;
    serviceName: string;
    displayName: string;
    status: string;
    severity: string;
    message: string;
    lastStartMode: string | null;
    lastError: string | null;
    consecutiveFailures: number;
    firstDetectedAt: string;
    lastDetectedAt: string;
    resolvedAt: string | null;
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

    uninstallAgent(deviceId: string): Promise<{ commandId: string; message: string }> {
        return this.post(`/api/agent/uninstall/${encodeURIComponent(deviceId)}`);
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

    getDeviceSystemInfo(deviceId: string): Promise<{ hostname: string | null; serialNumber: string | null; hostnameError?: string; serialError?: string; printerSerialNumber: string | null }> {
        return this.get(`/api/sqlquery/devices/${deviceId}/system-info`);
    },

    updatePrinterSerial(deviceId: string, printerSerialNumber: string | null): Promise<{ deviceId: string; printerSerialNumber: string | null }> {
        return this.put(`/api/sqlquery/devices/${deviceId}/printer-serial`, { printerSerialNumber });
    },

    refreshPrinterSerial(deviceId: string): Promise<{ deviceId: string; printerSerialNumber: string | null; message?: string }> {
        return this.post(`/api/sqlquery/devices/${deviceId}/refresh-printer-serial`, undefined, 30_000);
    },

    updateDeviceManualInfo(deviceId: string, patch: { hostname?: string | null; serialNumber?: string | null; printerSerialNumber?: string | null }): Promise<{ deviceId: string; hostname: string | null; serialNumber: string | null; printerSerialNumber: string | null }> {
        return this.put(`/api/sqlquery/devices/${deviceId}/manual-info`, patch);
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
    syncStoreAddresses(): Promise<StoreAddressSyncResult> {
        return this.post("/api/storemanagers/sync-addresses", {});
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

    setDeviceVisibility(deviceId: string, hidden: boolean): Promise<{ id: string; hiddenForNonAdmins: boolean }> {
        return this.put(`/api/devices/${encodeURIComponent(deviceId)}/visibility`, { hidden });
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

    getEventLogAnalysis(deviceId: string, hours = 24, limit = 200): Promise<EventLogAnalysisResult> {
        return this.get(`/api/agent/collector-report/${encodeURIComponent(deviceId)}/eventlogs/analysis?hours=${hours}&limit=${limit}`);
    },

    pullEventLogsFromDevice(deviceId: string, hours = 24): Promise<EventLogPullResult> {
        return this.post(`/api/agent/collector-report/${encodeURIComponent(deviceId)}/eventlogs/pull?hours=${hours}`, undefined, 120_000);
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

    getActiveServiceIncidents(): Promise<StoreServiceIncident[]> {
        return this.get("/api/service-monitor/incidents/active");
    },

    getBaseUrl(): string {
        return API_BASE_URL;
    },

    /** Backend liveness probe — hafif `/api/health` endpoint'i. 401 redirect yapmaz, auth header gerekmez. */
    async checkBackendHealth(): Promise<boolean> {
        try {
            await fetch(buildUrl("/api/health"), {
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

    // ==========================
    // REMOTE DESKTOP (VNC)
    // ==========================
    getActiveVncSessions(): Promise<ActiveVncSession[]> {
        return this.get("/api/rdp/sessions");
    },

    terminateVncSession(sessionId: string): Promise<{ message: string }> {
        return this.delete(`/api/rdp/sessions/${encodeURIComponent(sessionId)}`);
    },

    getVncSessionLogs(deviceId?: string, limit = 200): Promise<VncSessionLog[]> {
        const qs = new URLSearchParams({ limit: String(limit) });
        if (deviceId) qs.append("deviceId", deviceId);
        return this.get(`/api/rdp/logs?${qs}`);
    },

    triggerAllUpdates(backendUrl: string): Promise<{ message: string }> {
        return this.post(`/api/updates/trigger-all?backendUrl=${encodeURIComponent(backendUrl)}`);
    },

    triggerUpdate(deviceId: string, backendUrl: string): Promise<{ commandId: string; message: string }> {
        return this.post(`/api/updates/trigger?deviceId=${encodeURIComponent(deviceId)}&backendUrl=${encodeURIComponent(backendUrl)}`);
    },

    buildNewAgent(): Promise<void> {
        return this.post("/api/updates/build");
    },

    // ==========================
    // INVENTORY (SDP envanter modulu)
    // ==========================
    getInventoryAssets(params: {
        search?: string;
        storeCode?: number;
        productType?: string;
        state?: string;
        fizikselDurum?: string;
        unmatchedOnly?: boolean;
        sortBy?: string;
        sortDir?: "asc" | "desc";
        page?: number;
        pageSize?: number;
    }): Promise<{ items: InventoryAsset[]; total: number; page: number; pageSize: number }> {
        const qs = new URLSearchParams();
        Object.entries(params).forEach(([k, v]) => {
            if (v !== undefined && v !== null && v !== "") qs.append(k, String(v));
        });
        return this.get(`/api/inventory/assets?${qs}`);
    },

    getInventoryAsset(id: number): Promise<InventoryAsset> {
        return this.get(`/api/inventory/assets/${id}`);
    },

    getInventorySummary(): Promise<InventorySummary> {
        return this.get("/api/inventory/summary");
    },

    getInventoryStats(): Promise<InventoryStats> {
        return this.get("/api/inventory/stats");
    },

    getInventoryFilterOptions(): Promise<InventoryFilterOptions> {
        return this.get("/api/inventory/filter-options");
    },

    getInventoryBatches(): Promise<InventoryImportBatch[]> {
        return this.get("/api/inventory/batches");
    },

    getInventoryUnmappedStores(): Promise<StoreNameMapping[]> {
        return this.get("/api/inventory/unmapped-stores");
    },

    getInventoryAllStores(): Promise<Array<{ storeCode: number; storeName: string | null }>> {
        return this.get("/api/inventory/all-stores");
    },

    updateInventoryStoreMapping(id: number, storeCode: number | null): Promise<{ mapping: StoreNameMapping; affectedAssets: number }> {
        return this.put(`/api/inventory/store-mappings/${id}`, { storeCode });
    },

    rematchInventoryUnmapped(): Promise<{ updatedMappings: number; updatedAssets: number }> {
        return this.post("/api/inventory/rematch-unmapped", {});
    },

    // ==========================
    // ACTIVITY LOG (audit)
    // ==========================
    getActivityLog(params: {
        category?: string;
        username?: string;
        successOnly?: boolean;
        failuresOnly?: boolean;
        search?: string;
        page?: number;
        pageSize?: number;
    } = {}): Promise<{ items: ActivityLogEntry[]; total: number; page: number; pageSize: number }> {
        const qs = new URLSearchParams();
        Object.entries(params).forEach(([k, v]) => {
            if (v !== undefined && v !== null && v !== "") qs.append(k, String(v));
        });
        return this.get(`/api/activity-log?${qs}`);
    },

    getActivityLogCategories(): Promise<Array<{ name: string; count: number }>> {
        return this.get("/api/activity-log/categories");
    },

    // ==========================
    // STORE OPENINGS (Mağaza Açılış Checklist)
    // ==========================
    getStoreOpenings(status?: string): Promise<StoreOpeningListItem[]> {
        const qs = status ? `?status=${encodeURIComponent(status)}` : "";
        return this.get(`/api/store-openings${qs}`);
    },

    getStoreOpening(id: number): Promise<StoreOpeningDetail> {
        return this.get(`/api/store-openings/${id}`);
    },

    createStoreOpening(data: {
        storeCode: number;
        storeName: string;
        city?: string;
        address?: string;
        targetOpeningDate: string;
        templateId?: number;
        notes?: string;
        roleAssignments?: Record<string, string>;
    }): Promise<StoreOpeningDetail> {
        return this.post("/api/store-openings", data);
    },

    updateStoreOpening(id: number, data: {
        storeName?: string;
        city?: string;
        address?: string;
        targetOpeningDate?: string;
        actualOpeningDate?: string;
        status?: string;
        notes?: string;
        roleAssignments?: Record<string, string>;
    }): Promise<void> {
        return this.put(`/api/store-openings/${id}`, data);
    },

    deleteStoreOpening(id: number): Promise<void> {
        return this.delete(`/api/store-openings/${id}`);
    },

    updateOpeningItem(openingId: number, itemId: number, data: {
        status?: string;
        serialNumber?: string;
        assetNumber?: string;
        notes?: string;
    }): Promise<void> {
        return this.put(`/api/store-openings/${openingId}/items/${itemId}`, data);
    },

    addOpeningItem(openingId: number, data: {
        categoryName: string;
        assignedRole?: string;
        itemName: string;
        parentName?: string;
        hasSerialNumber: boolean;
        hasAssetNumber: boolean;
        sortOrder?: number;
    }): Promise<StoreOpeningItem> {
        return this.post(`/api/store-openings/${openingId}/items`, data);
    },

    deleteOpeningItem(openingId: number, itemId: number): Promise<void> {
        return this.delete(`/api/store-openings/${openingId}/items/${itemId}`);
    },

    async uploadOpeningItemPhoto(openingId: number, itemId: number, file: File): Promise<{ photoUrl: string }> {
        const formData = new FormData();
        formData.append("file", file);
        const res = await fetchWithTimeout(buildUrl(`/api/store-openings/${openingId}/items/${itemId}/photo`), {
            method: "POST",
            headers: { ...getAuthHeaders() },
            body: formData,
        });
        if (!res.ok) throw new Error(`Foto yükleme başarısız: ${res.status}`);
        return res.json();
    },

    deleteOpeningItemPhoto(openingId: number, itemId: number): Promise<void> {
        return this.delete(`/api/store-openings/${openingId}/items/${itemId}/photo`);
    },

    getOpeningItemPhotoUrl(openingId: number, itemId: number): string {
        return buildUrl(`/api/store-openings/${openingId}/items/${itemId}/photo`);
    },

    getStoreOpeningActivity(openingId: number): Promise<StoreOpeningActivity[]> {
        return this.get(`/api/store-openings/${openingId}/activity`);
    },

    getStoreOpeningExportUrl(openingId: number): string {
        return buildUrl(`/api/store-openings/${openingId}/export.xlsx`);
    },

    // ----- Templates -----
    getStoreOpeningTemplates(): Promise<StoreOpeningTemplateSummary[]> {
        return this.get("/api/store-opening-templates");
    },

    getStoreOpeningTemplate(id: number): Promise<StoreOpeningTemplateDetail> {
        return this.get(`/api/store-opening-templates/${id}`);
    },

    createStoreOpeningTemplate(data: {
        name: string;
        description?: string;
        isDefault: boolean;
        items: StoreOpeningTemplateItemInput[];
    }): Promise<StoreOpeningTemplateDetail> {
        return this.post("/api/store-opening-templates", data);
    },

    updateStoreOpeningTemplate(id: number, data: {
        name: string;
        description?: string;
        isDefault: boolean;
        items: StoreOpeningTemplateItemInput[];
    }): Promise<void> {
        return this.put(`/api/store-opening-templates/${id}`, data);
    },

    deleteStoreOpeningTemplate(id: number): Promise<void> {
        return this.delete(`/api/store-opening-templates/${id}`);
    },

    async importInventoryXlsx(file: File): Promise<InventoryImportResult> {
        const formData = new FormData();
        formData.append("file", file);
        const res = await fetchWithTimeout(buildUrl("/api/inventory/import"), {
            method: "POST",
            headers: { ...getAuthHeaders() },
            body: formData,
        }, 5 * 60_000); // 5 dk — 7400 satir icin
        if (!res.ok) {
            let msg = `Import failed: ${res.status}`;
            try { const j = await res.json(); if (j?.error) msg = j.error; } catch { /* ignore */ }
            throw new Error(msg);
        }
        return res.json();
    },
};

// ==========================
// INVENTORY TYPES
// ==========================
export interface InventoryAsset {
    id: number;
    assetName: string;
    storeCode: number | null;
    storeNameRaw: string | null;
    department: string | null;
    productType: string | null;
    product: string | null;
    productCode: string | null;
    orgSerialNumber: string | null;
    computerName: string | null;
    macAddress: string | null;
    assetTag: string | null;
    acquisitionDate: string | null;
    expiryDate: string | null;
    yazarkasaSicilNo: string | null;
    baseSeriNo: string | null;
    printerSeriNo: string | null;
    ikinciMonitorSeriNo: string | null;
    ipAddress: string | null;
    assetState: string | null;
    fizikselDurum: string | null;
    purchaseCost: number | null;
    faturaNo: string | null;
    talepNo: string | null;
    importedAt: string;
    importBatchId: number | null;
}

export interface InventorySummary {
    totalAssets: number;
    matchedAssets: number;
    unmatchedAssets: number;
    unmappedStoreNames: number;
}

export interface InventoryStats {
    byProductType: Array<{ name: string; count: number }>;
    byState: Array<{ name: string; count: number }>;
    byFizikselDurum: Array<{ name: string; count: number }>;
    byStoreTop20: Array<{ storeCode: number; count: number }>;
    acquisitionByYear: Array<{ year: number; count: number }>;
    totalPurchaseCost: number;
}

export interface InventoryFilterOptions {
    productTypes: string[];
    states: string[];
    fizikselDurumlar: string[];
    stores: Array<{ storeCode: number; storeName: string | null; count: number }>;
}

export interface InventoryImportBatch {
    id: number;
    fileName: string | null;
    fileSizeBytes: number;
    importedBy: string | null;
    importedAt: string;
    totalRows: number;
    insertedCount: number;
    updatedCount: number;
    skippedCount: number;
    unmatchedStoreCount: number;
    status: string;
    errorMessage: string | null;
}

export interface InventoryImportResult {
    batchId: number;
    totalRows: number;
    insertedCount: number;
    updatedCount: number;
    skippedCount: number;
    unmatchedStoreCount: number;
    unmatchedStoreNames: string[];
    errors: string[];
}

export interface ActivityLogEntry {
    id: number;
    username: string | null;
    category: string;
    action: string;
    target: string | null;
    details: string | null;
    success: boolean;
    errorMessage: string | null;
    createdAt: string;
}

export interface StoreNameMapping {
    id: number;
    rawName: string;
    storeCode: number | null;
    autoMatched: boolean;
    createdAt: string;
    updatedAt: string;
}

// ==========================
// STORE OPENING TYPES
// ==========================
export type StoreOpeningStatus = "Planned" | "InProgress" | "Completed" | "Cancelled";
export type StoreOpeningItemStatus = "Pending" | "Completed" | "NotApplicable";

export interface StoreOpeningListItem {
    id: number;
    storeCode: number;
    storeName: string;
    city: string | null;
    targetOpeningDate: string;
    actualOpeningDate: string | null;
    status: StoreOpeningStatus;
    totalItems: number;
    completedItems: number;
    notApplicableItems: number;
    progressPercent: number;
    createdAt: string;
    createdBy: string | null;
    isOverdue: boolean;
}

export interface StoreOpeningItem {
    id: number;
    storeOpeningId: number;
    categoryName: string;
    assignedRole: string | null;
    itemName: string;
    parentName: string | null;
    hasSerialNumber: boolean;
    hasAssetNumber: boolean;
    serialNumber: string | null;
    assetNumber: string | null;
    status: StoreOpeningItemStatus;
    photoUrl: string | null;
    notes: string | null;
    sortOrder: number;
    completedBy: string | null;
    completedAt: string | null;
}

export interface StoreOpeningCategoryGroup {
    categoryName: string;
    assignedRole: string | null;
    totalItems: number;
    completedItems: number;
    notApplicableItems: number;
    progressPercent: number;
    items: StoreOpeningItem[];
}

export interface StoreOpeningDetail {
    id: number;
    storeCode: number;
    storeName: string;
    city: string | null;
    address: string | null;
    targetOpeningDate: string;
    actualOpeningDate: string | null;
    status: StoreOpeningStatus;
    templateId: number | null;
    notes: string | null;
    roleAssignments: Record<string, string>;
    createdBy: string | null;
    createdAt: string;
    updatedBy: string | null;
    updatedAt: string | null;
    completedBy: string | null;
    completedAt: string | null;
    categories: StoreOpeningCategoryGroup[];
    progressPercent: number;
}

export interface StoreOpeningActivity {
    id: number;
    username: string;
    action: string;
    details: string | null;
    createdAt: string;
}

export interface StoreOpeningTemplateSummary {
    id: number;
    name: string;
    description: string | null;
    isDefault: boolean;
    itemCount: number;
    createdAt: string;
}

export interface StoreOpeningTemplateItemInput {
    categoryName: string;
    assignedRole?: string;
    itemName: string;
    parentName?: string;
    hasSerialNumber: boolean;
    hasAssetNumber: boolean;
    sortOrder: number;
}

export interface StoreOpeningTemplateItem extends StoreOpeningTemplateItemInput {
    id: number;
    templateId: number;
}

export interface StoreOpeningTemplateDetail {
    id: number;
    name: string;
    description: string | null;
    isDefault: boolean;
    createdAt: string;
    items: StoreOpeningTemplateItem[];
}
