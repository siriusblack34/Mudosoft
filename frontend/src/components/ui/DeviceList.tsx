import React from 'react';
import type { Device } from '../../types';
import StatusPill from '../common/StatusPill';
import { Monitor, CreditCard } from 'lucide-react';

interface DeviceListProps {
  devices: Device[];
  onSelectDevice: (device: Device) => void;
}

const DeviceList: React.FC<DeviceListProps> = ({ devices, onSelectDevice }) => {
  if (devices.length === 0) {
    return (
      <div className="text-center py-12 text-ms-text-muted text-sm">
        Filtreyle eşleşen cihaz bulunamadı.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm text-left">
        <thead>
          <tr className="border-b border-ms-border">
            <th className="px-4 py-3 text-xs font-semibold text-ms-text-muted uppercase tracking-wider">Durum</th>
            <th className="px-4 py-3 text-xs font-semibold text-ms-text-muted uppercase tracking-wider">Cihaz</th>
            <th className="px-4 py-3 text-xs font-semibold text-ms-text-muted uppercase tracking-wider">IP Adresi</th>
            <th className="px-4 py-3 text-xs font-semibold text-ms-text-muted uppercase tracking-wider hidden sm:table-cell">İşletim Sistemi</th>
            <th className="px-4 py-3 text-xs font-semibold text-ms-text-muted uppercase tracking-wider hidden sm:table-cell">CPU</th>
            <th className="px-4 py-3 text-xs font-semibold text-ms-text-muted uppercase tracking-wider hidden sm:table-cell">RAM</th>
            <th className="px-4 py-3 text-xs font-semibold text-ms-text-muted uppercase tracking-wider hidden md:table-cell">Mağaza</th>
            <th className="px-4 py-3 text-xs font-semibold text-ms-text-muted uppercase tracking-wider hidden lg:table-cell">Son Görülme</th>
          </tr>
        </thead>
        <tbody>
          {devices.map((device, idx) => (
            <tr
              key={device.id}
              onClick={() => onSelectDevice(device)}
              className={`border-b border-zinc-800/60 hover:bg-zinc-800/40 cursor-pointer transition-colors ${
                idx % 2 === 0 ? 'bg-transparent' : 'bg-zinc-900/30'
              }`}
            >
              <td className="px-4 py-3">
                <StatusPill online={device.online} />
              </td>
              <td className="px-4 py-3">
                <div className="flex items-center gap-2 font-medium text-ms-text">
                  {device.type === 'POS'
                    ? <CreditCard className="w-4 h-4 text-violet-400 shrink-0" />
                    : <Monitor className="w-4 h-4 text-zinc-400 shrink-0" />
                  }
                  <span>{device.hostname}</span>
                </div>
              </td>
              <td className="px-4 py-3 text-ms-text-muted font-mono text-xs">{device.ipAddress}</td>
              <td className="px-4 py-3 text-ms-text-muted hidden sm:table-cell">{device.os?.name || '-'}</td>
              <td className="px-4 py-3 hidden sm:table-cell">
                {device.cpuUsage != null ? (
                  <div className="flex items-center gap-2">
                    <div className="w-16 h-1.5 bg-zinc-800 rounded-full overflow-hidden">
                      <div
                        className={`h-full rounded-full ${device.cpuUsage > 80 ? 'bg-red-500' : device.cpuUsage > 50 ? 'bg-amber-500' : 'bg-green-500'}`}
                        style={{ width: `${device.cpuUsage}%` }}
                      />
                    </div>
                    <span className="text-ms-text-muted text-xs">{device.cpuUsage}%</span>
                  </div>
                ) : '-'}
              </td>
              <td className="px-4 py-3 hidden sm:table-cell">
                {device.ramUsage != null ? (
                  <div className="flex items-center gap-2">
                    <div className="w-16 h-1.5 bg-zinc-800 rounded-full overflow-hidden">
                      <div
                        className={`h-full rounded-full ${device.ramUsage > 80 ? 'bg-red-500' : device.ramUsage > 50 ? 'bg-amber-500' : 'bg-violet-500'}`}
                        style={{ width: `${device.ramUsage}%` }}
                      />
                    </div>
                    <span className="text-ms-text-muted text-xs">{device.ramUsage}%</span>
                  </div>
                ) : '-'}
              </td>
              <td className="px-4 py-3 text-ms-text-muted hidden md:table-cell">{device.storeCode}</td>
              <td className="px-4 py-3 text-ms-text-muted text-xs hidden lg:table-cell">{device.lastSeen}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default DeviceList;
