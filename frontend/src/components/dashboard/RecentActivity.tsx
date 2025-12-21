import React from 'react';
import { Monitor, AlertCircle } from 'lucide-react';

interface RecentOfflineDevice {
    hostname: string;
    ip: string;
    os: string;
    store: number;
    lastSeen: string;
}

interface RecentActivityProps {
    devices: RecentOfflineDevice[];
}

const RecentActivity: React.FC<RecentActivityProps> = ({ devices }) => {
    return (
        <div className="bg-slate-800 rounded-xl border border-slate-700 h-full flex flex-col overflow-hidden">
            <div className="px-5 py-4 border-b border-slate-700 flex justify-between items-center bg-slate-800/50">
                <h3 className="text-base font-semibold text-white">Recently Offline</h3>
                {devices.length > 0 && (
                    <span className="bg-rose-500/10 text-rose-400 text-xs px-2 py-0.5 rounded border border-rose-500/20">
                        {devices.length} Devices
                    </span>
                )}
            </div>

            <div className="overflow-auto flex-1 p-0">
                {devices.length === 0 ? (
                    <div className="flex flex-col items-center justify-center h-40 text-slate-500">
                        <Monitor className="w-8 h-8 mb-2 opacity-20" />
                        <p className="text-sm">No recent offline devices</p>
                    </div>
                ) : (
                    <table className="w-full text-left text-sm">
                        <thead className="bg-slate-900/40 text-slate-400 font-medium sticky top-0">
                            <tr>
                                <th className="px-5 py-3 font-medium">Hostname</th>
                                <th className="px-5 py-3 font-medium hidden sm:table-cell">IP Address</th>
                                <th className="px-5 py-3 font-medium">Store</th>
                                <th className="px-5 py-3 font-medium text-right">Last Seen</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-700/50">
                            {devices.map((device, i) => (
                                <tr key={i} className="hover:bg-slate-700/30 transition-colors">
                                    <td className="px-5 py-3">
                                        <div className="flex items-center gap-3">
                                            <div className="w-2 h-2 rounded-full bg-rose-500"></div>
                                            <span className="font-medium text-slate-200">{device.hostname}</span>
                                        </div>
                                    </td>
                                    <td className="px-5 py-3 text-slate-400 hidden sm:table-cell font-mono text-xs">
                                        {device.ip}
                                    </td>
                                    <td className="px-5 py-3 text-slate-300">
                                        {device.store}
                                    </td>
                                    <td className="px-5 py-3 text-right text-slate-400 text-xs">
                                        {device.lastSeen}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>
        </div>
    );
};

export default RecentActivity;
