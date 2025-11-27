// frontend/src/components/ui/DeviceList.tsx - GÜNCELLEME 2/2
import React from 'react';
import type { Device } from '../../types';
import StatusBadge from './StatusBadge';
import { PosIcon, PcIcon } from '../icons/Icons';

interface DeviceListProps {
  devices: Device[];
  onSelectDevice: (device: Device) => void;
}

const DeviceList: React.FC<DeviceListProps> = ({ devices, onSelectDevice }) => {
  if (devices.length === 0) {
    return <p className="text-center text-gray-400 py-4">No devices match the current filters.</p>;
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm text-left text-gray-300">
        <thead className="text-xs text-gray-400 uppercase bg-gray-800/50">
          <tr>
            <th scope="col" className="px-6 py-3">Status</th>
            <th scope="col" className="px-6 py-3">Hostname</th>
            <th scope="col" className="px-6 py-3">IP Address</th>
            <th scope="col" className="px-6 py-3">OS</th>
            {/* ✅ YENİ SÜTUN: CPU */}
            <th scope="col" className="px-6 py-3 hidden sm:table-cell">CPU</th>
            {/* ✅ YENİ SÜTUN: RAM */}
            <th scope="col" className="px-6 py-3 hidden sm:table-cell">RAM</th>
            <th scope="col" className="px-6 py-3 hidden md:table-cell">Store</th>
            <th scope="col" className="px-6 py-3 hidden lg:table-cell">Last Seen</th>
          </tr>
        </thead>
        <tbody>
          {devices.map(device => (
            <tr
              key={device.id}
              onClick={() => onSelectDevice(device)}
              className="bg-gray-900/50 border-b border-gray-700 hover:bg-gray-700/50 cursor-pointer"
            >
              {/* STATUS (online alanından) */}
              <td className="px-6 py-4">
                {/* StatusBadge, device.online alanını kullanarak durumu gösterir. */}
                <StatusBadge status={device.online ? 'Online' : 'Offline'} showText={false} />
              </td>
              {/* HOSTNAME & TYPE */}
              <td className="px-6 py-4 font-medium text-white flex items-center space-x-2">
                {device.type === 'POS' ? <PosIcon className="w-4 h-4 text-cyan-400" /> : <PcIcon className="w-4 h-4 text-gray-400" />}
                <span>{device.hostname}</span>
              </td>
              <td className="px-6 py-4">{device.ipAddress}</td>
              {/* OS */}
              <td className="px-6 py-4">{device.os.name}</td>
              
              {/* ✅ GÜNCELLEME: CPU Kullanımı */}
              <td className="px-6 py-4 hidden sm:table-cell">
                {device.cpuUsage ? `${device.cpuUsage}%` : '-'}
              </td>
              
              {/* ✅ GÜNCELLEME: RAM Kullanımı */}
              <td className="px-6 py-4 hidden sm:table-cell">
                {device.ramUsage ? `${device.ramUsage}%` : '-'}
              </td>

              {/* STORE */}
              <td className="px-6 py-4 hidden md:table-cell">{device.storeCode}</td>
              <td className="px-6 py-4 hidden lg:table-cell">{device.lastSeen}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default DeviceList;