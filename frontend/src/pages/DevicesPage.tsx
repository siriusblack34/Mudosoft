import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import StatusPill from '../components/common/StatusPill';
import { apiClient } from '../lib/apiClient';
import type { Device } from '../types';

const DevicesPage: React.FC = () => {
  const [devices, setDevices] = useState<Device[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<'all' | 'online' | 'offline'>('all');
  const navigate = useNavigate();

  useEffect(() => {
    const load = async () => {
      try {
        const res = await apiClient.getDevices();
        setDevices(() => res);

      } catch (err) {
        console.error(err);
      } finally {
        setLoading(false);
      }
    };
    load();
  }, []);

  const filtered = devices.filter((d) => {
    if (filter === 'online') return d.online;
    if (filter === 'offline') return !d.online;
    return true;
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Devices</h1>
        <div className="flex gap-2 text-xs">
          {(['all', 'online', 'offline'] as const).map((f) => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              className={`px-3 py-1 rounded-full border ${
                filter === f ? 'border-ms-accent text-ms-accent' : 'border-ms-border'
              }`}
            >
              {f.toUpperCase()}
            </button>
          ))}
        </div>
      </div>

      {loading ? (
        <div>Loading devices…</div>
      ) : (
        <div className="overflow-hidden rounded-2xl border border-ms-border bg-ms-panel">
          <table className="w-full text-sm">
            <thead className="bg-ms-bg-soft text-ms-text-muted">
              <tr>
                <th className="text-left px-4 py-2">Status</th>
                <th className="text-left px-4 py-2">Hostname</th>
                <th className="text-left px-4 py-2">IP Address</th>
                <th className="text-left px-4 py-2">Type</th>
                <th className="text-left px-4 py-2">Store</th>
                <th className="text-left px-4 py-2">OS</th>
                <th className="text-left px-4 py-2">CPU</th>
                <th className="text-left px-4 py-2">RAM</th>
                <th className="text-left px-4 py-2"></th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((d) => (
                <tr
                  key={d.id}
                  className="border-t border-ms-border/60 hover:bg-ms-bg-soft/70 cursor-pointer"
                  onClick={() => navigate(`/devices/${d.id}`)}
                >
                  <td className="px-4 py-2">
                    {/* ✅ DÜZELTME: StatusPill artık 'online' prop'unu doğru şekilde kabul ediyor. */}
                    <StatusPill online={d.online} />
                  </td>
                  <td className="px-4 py-2 font-medium">{d.hostname}</td>
                  <td className="px-4 py-2">{d.ipAddress}</td>
                  <td className="px-4 py-2">{d.type}</td>
                  <td className="px-4 py-2">{d.storeCode}</td>
                  {/* ✅ KRİTİK DÜZELTME: d.os nesnesinden name alanı çekildi. */}
                  <td className="px-4 py-2">{d.os.name}</td>
                  <td className="px-4 py-2">{d.cpuUsage ?? '-'}%</td>
                  <td className="px-4 py-2">{d.ramUsage ?? '-'}%</td>
                  <td className="px-4 py-2 text-right text-ms-text-muted text-xs">
                    Details →
                  </td>
                </tr>
              ))}
              {filtered.length === 0 && (
                <tr>
                  <td colSpan={9} className="px-4 py-6 text-center text-ms-text-muted">
                    No devices found.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};

export default DevicesPage;