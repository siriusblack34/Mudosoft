// types.ts
export type DeviceType = 'POS' | 'PC';

export interface DeviceMetric {
    timestampUtc: string;
    cpuUsagePercent: number;
    ramUsagePercent: number;
    diskUsagePercent: number;
}

export interface OsInfo {
    name: string;
    version?: string;
}

export interface Device {
    id: string;
    hostname: string;
    ipAddress: string;
    os: OsInfo;
    storeCode: number;
    storeName?: string;
    type: DeviceType;
    sqlVersion?: string;
    posVersion?: string;
    online: boolean;
    lastSeen: string;
    
    // ✅ Backend'den gelen doğru alan adları bunlar.
    cpuUsage?: number; 
    ramUsage?: number; 
    diskUsage?: number; 
    
    agentVersion?: string;
    metrics?: DeviceMetric[]; 
    
    // (Eski/Fazla alanları sildiğiniz varsayılmıştır.)
}

// ... ActionRecord ve SqlResult tipleri devam eder ...