export type DeviceType = 'POS' | 'PC';

// ✅ KRİTİK DÜZELTME: DeviceMetric arayüzü doğru şekilde dışa aktarılmalıdır.
export interface DeviceMetric {
  timestampUtc: string;
  cpuUsagePercent: number;
  ramUsagePercent: number;
  diskUsagePercent: number;
}

// DeviceList ve DeviceDetails sayfalarının beklediği OS yapısı.
export interface OsInfo {
  name: string;
  version?: string;
}

export interface Device {
  id: string;
  hostname: string;
  ipAddress: string;
  os: OsInfo; // List ve Detail sayfaları için OsInfo nesnesi bekleniyor.
  storeCode: number;
  storeName?: string;
  type: DeviceType;
  sqlVersion?: string;
  posVersion?: string;
  online: boolean;
  lastSeen: string;
  cpuUsage?: number;
  ramUsage?: number;
  diskUsage?: number;
  agentVersion?: string;
  metrics?: DeviceMetric[]; // ✅ Tarihsel Metrikler doğru tipe bağlanıyor.
}

export interface ActionRecord {
  id: string;
  deviceId: string;
  deviceHostname: string;
  storeCode: number;
  type:
    | 'reboot'
    | 'run_ps'
    | 'run_batch'
    | 'sql_query'
    | 'file_push'
    | 'file_pull'
    | 'service_restart';
  status: 'pending' | 'running' | 'success' | 'failed';
  requestedBy: string;
  createdAt: string;
  finishedAt?: string;
  summary?: string;
}

export interface SqlResult {
  columns: string[];
  rows: (string | number | null)[][];
}