// types.ts
export type DeviceType = 'POS' | 'PC';

export enum ActionType {
    Reboot = 'Reboot',
    RunPS = 'RunPS',
    RunSQL = 'RunSQL',
}

export enum DeviceStatus {
    Online = 'Online',
    Offline = 'Offline',
    Warning = 'Warning',
}

export interface DeviceMetric {
    timestampUtc: string;
    cpuUsagePercent: number;
    ramUsagePercent: number;
    diskUsagePercent: number;
}

// Backend'deki OsInfoDto ile eşleşir
export interface OsInfo {
    name: string;
    version?: string;
}

export interface StoreDevice {
    deviceId: string;
    storeCode: number;
    storeName: string;
    deviceType: string;     // "PC", "KK1", "KK2", "KK3"
    calculatedIpAddress: string;
    dbConnectionString?: string;
    lastSeen?: string;
    isOnline?: boolean;
}

export interface Device {
    id: string;
    hostname: string;
    ipAddress: string;
    os?: OsInfo;
    storeCode: number;
    storeName?: string;
    type: DeviceType;
    sqlVersion?: string;
    posVersion?: string;
    online: boolean;
    lastSeen: string;
    firstSeen?: string;
    excludeFromOfflineList?: boolean;
    isTemporarilyClosed?: boolean;
    temporaryCloseReason?: string | null;

    // Live Metrics
    cpuUsage?: number;
    ramUsage?: number;
    diskUsage?: number;

    // Agent Info
    agentVersion?: string;
    metrics?: DeviceMetric[];

    // Hardware Inventory
    cpuModel?: string;
    totalRamMB?: number;
    totalDiskGB?: number;
    gpuModel?: string;

    // User & Session
    lastLoggedInUser?: string;

    // Uptime (boot time from server)
    systemBootTime?: string;
    vncInstalled?: boolean;
}

export interface SqlResult {
    columns: string[];
    rows: (string | number | boolean | null)[][];
}

// Dashboard
export interface RecentOfflineDevice {
    hostname: string;
    ip: string;
    os: string;
    store: number;
    lastSeen: string;
}

export interface DashboardSummary {
    totalDevices: number;
    online: number;
    offline: number;
    healthy: number;
    warning: number;
    critical: number;
    recentOffline: RecentOfflineDevice[];
}

// Offline Logs
export interface OfflineLogEntry {
    id: number;
    storeCode: number;
    storeName: string;
    offlineKasaCount: number;
    offlineAt: string;
    onlineAt: string | null;
    durationMinutes: number | null;
    isStillOffline: boolean;
}

export interface OfflineLogStats {
    storeCode: number;
    storeName: string;
    totalIncidents: number;
    totalOfflineMinutes: number;
    lastOfflineAt: string;
    isCurrentlyOffline: boolean;
}

// Outage Mail
export interface OutageMailTemplateItem {
    key: string;
    label: string;
    needsDays: boolean;
}
export interface OutageMailTemplateGroup {
    category: string;
    items: OutageMailTemplateItem[];
}

export interface OutageMailRequest {
    storeCodes: number[];
    issueKey: string;
    greeting?: string;
    additionalNotes?: string;
    days?: number;
    recipientOverride?: string;
    editedPlainText?: string;
}

export interface OutageMailStoreBlock {
    storeCode: number;
    storeName: string;
    routerIp: string;
    managerName?: string | null;
    managerPhone?: string | null;
}

export interface OutageMailPreview {
    subject: string;
    plainText: string;
    htmlBody: string;
    to: string;
    cc: string[];
    stores: OutageMailStoreBlock[];
}

export interface OutageMailSendResult {
    success: boolean;
    error?: string | null;
    to: string;
    cc: string[];
}

// Collector
export interface CollectorReportEntry {
    collectorName: string;
    timestampUtc: string;
    severity: string;
    jsonData: string;
    success: boolean;
    errorMessage: string | null;
}

// Event Logs
export interface EventLogEntry {
    logName: string;
    source: string;
    eventId: number;
    level: string;
    timeGenerated: string;
    message: string;
    translatedMessage: string | null;
    suggestedAction: string | null;
}
