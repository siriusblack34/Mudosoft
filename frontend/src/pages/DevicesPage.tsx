import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiClient } from '../lib/apiClient';
import type { Device } from '../types';
import { Monitor, Cpu, MemoryStick, HardDrive, Search } from 'lucide-react';

const DevicesPage: React.FC = () => {
  const [devices, setDevices] = useState<Device[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<'all' | 'online' | 'offline'>('all');
  const [searchTerm, setSearchTerm] = useState('');
  const navigate = useNavigate();

  useEffect(() => {
    const load = async () => {
      try {
        const res = await apiClient.getDevices();
        setDevices(res);
      } catch (err) {
        console.error(err);
      } finally {
        setLoading(false);
      }
    };
    load();
    const interval = setInterval(load, 10000);
    return () => clearInterval(interval);
  }, []);

  const filtered = devices.filter((d) => {
    const matchesFilter = filter === 'all' || (filter === 'online' ? d.online : !d.online);
    const matchesSearch = d.hostname.toLowerCase().includes(searchTerm.toLowerCase()) ||
      d.ipAddress?.toLowerCase().includes(searchTerm.toLowerCase()) ||
      String(d.storeCode || '').toLowerCase().includes(searchTerm.toLowerCase());
    return matchesFilter && matchesSearch;
  });

  const onlineCount = devices.filter(d => d.online).length;
  const offlineCount = devices.filter(d => !d.online).length;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold text-white">Devices</h1>
          <p className="text-sm text-gray-400 mt-1">
            Manage and monitor all connected devices across the network
          </p>
        </div>

        {/* Stats Pills */}
        <div className="flex items-center gap-3">
          <div className="px-4 py-2 rounded-xl glass-card border-none bg-emerald-500/10 shadow-[0_0_15px_rgba(16,185,129,0.1)]">
            <span className="text-emerald-400 font-semibold">{onlineCount}</span>
            <span className="text-slate-400 ml-2 text-sm">Online</span>
          </div>
          <div className="px-4 py-2 rounded-xl glass-card border-none bg-rose-500/10 shadow-[0_0_15px_rgba(244,63,94,0.1)]">
            <span className="text-rose-400 font-semibold">{offlineCount}</span>
            <span className="text-slate-400 ml-2 text-sm">Offline</span>
          </div>
        </div>
      </div>

      {/* Filters & Search */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div className="flex gap-2">
          {(['all', 'online', 'offline'] as const).map((f) => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              className={`px-4 py-2 rounded-xl text-sm font-medium transition-all duration-300 ${filter === f
                ? 'bg-sky-500 text-white shadow-[0_0_15px_rgba(14,165,233,0.3)] border-none'
                : 'glass-button hover:-translate-y-0.5'
                }`}
            >
              {f.charAt(0).toUpperCase() + f.slice(1)}
            </button>
          ))}
        </div>

        <div className="relative w-full sm:w-64">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
          <input
            type="text"
            placeholder="Search devices..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-10 pr-4 py-2 glass-panel rounded-xl text-sm text-white placeholder-slate-400 focus-ring transition-all"
          />
        </div>
      </div>

      {/* Device Cards Grid */}
      {loading ? (
        <div className="flex items-center justify-center py-20">
          <div className="w-8 h-8 border-2 border-emerald-500 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : filtered.length === 0 ? (
        <div className="text-center py-20 text-gray-500">
          <Monitor className="w-12 h-12 mx-auto mb-3 opacity-50" />
          <p>No devices found</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
          {filtered.map((device) => (
            <DeviceCard key={device.id} device={device} onClick={() => navigate(`/devices/${device.id}`)} />
          ))}
        </div>
      )}
    </div>
  );
};

// Device Card Component
const DeviceCard: React.FC<{ device: Device; onClick: () => void }> = ({ device, onClick }) => {
  return (
    <div
      onClick={onClick}
      className="glass-card-hover group relative p-5 rounded-2xl cursor-pointer"
    >
      {/* Status Indicator */}
      <div className="absolute top-4 right-4">
        <div className={`w-3 h-3 rounded-full ${device.online ? 'bg-emerald-400 shadow-[0_0_10px_rgba(52,211,153,0.8)]' : 'bg-rose-400 shadow-[0_0_10px_rgba(244,63,94,0.5)]'}`} />
      </div>

      {/* Header */}
      <div className="mb-4">
        <div className="flex items-center gap-3 mb-2">
          <div className="p-2 rounded-xl border border-white/5 bg-white/5 shadow-inner">
            <Monitor className="w-5 h-5 text-sky-400" />
          </div>
          <div>
            <h3 className="font-semibold text-white group-hover:text-sky-400 transition-colors truncate max-w-[180px]">
              {device.hostname}
            </h3>
            <p className="text-xs text-slate-400">{device.ipAddress}</p>
          </div>
        </div>
      </div>

      {/* Info Grid */}
      <div className="grid grid-cols-2 gap-3 mb-4">
        <div className="p-2 rounded-xl bg-white/5 border border-white/5">
          <div className="flex items-center gap-2 text-xs text-slate-400 mb-1">
            <Cpu className="w-3 h-3 text-indigo-400" />
            <span>CPU</span>
          </div>
          <p className="text-sm font-medium text-white">{device.cpuUsage ?? 0}%</p>
        </div>
        <div className="p-2 rounded-xl bg-white/5 border border-white/5">
          <div className="flex items-center gap-2 text-xs text-slate-400 mb-1">
            <MemoryStick className="w-3 h-3 text-violet-400" />
            <span>RAM</span>
          </div>
          <p className="text-sm font-medium text-white">{device.ramUsage ?? 0}%</p>
        </div>
      </div>

      {/* Footer */}
      <div className="flex items-center justify-between text-[11px] font-medium text-slate-400">
        <span className="px-2 py-1 rounded-md bg-white/5 border border-white/5 uppercase tracking-wider">{device.storeCode || 'N/A'}</span>
        <span className="truncate max-w-[100px] border border-white/5 rounded-md px-2 py-1 bg-white/5">{device.os?.name?.includes('Windows') ? 'Windows' : device.os?.name || 'Unknown'}</span>
      </div>

      {/* Hover Overlay */}
      <div className="absolute inset-0 rounded-2xl bg-gradient-to-t from-sky-500/5 to-transparent opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none" />
    </div>
  );
};

export default DevicesPage;