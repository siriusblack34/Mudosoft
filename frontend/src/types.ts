export type DeviceType = 'POS' | 'PC';

export interface Device {
  id: string;
  hostname: string;
  ipAddress: string;
  os: string;
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