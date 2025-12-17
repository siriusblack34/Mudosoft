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
    
    // 🚀 DÜZELTME: Backend'den null gelebileceği için opsiyonel yaptık.
    os?: OsInfo; 
    
    storeCode: number;
    storeName?: string;
    type: DeviceType;
    sqlVersion?: string;
    posVersion?: string;
    online: boolean;
    lastSeen: string;
    
    // ✅ Backend'den gelen doğru anlık alan adları
    cpuUsage?: number; 
    ramUsage?: number; 
    diskUsage?: number; 
    
    agentVersion?: string;
    metrics?: DeviceMetric[]; 
}
// ... ActionRecord ve SqlResult tipleri devam eder ...