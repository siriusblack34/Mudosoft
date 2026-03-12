// types.ts
export type DeviceType = 'POS' | 'PC';

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
}

export interface SqlResult {
    columns: string[];
    rows: any[][];
}