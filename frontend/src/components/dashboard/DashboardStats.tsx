import React from 'react';
import { Server, Wifi, AlertTriangle, Activity, ArrowUp, ArrowDown } from 'lucide-react';

interface DashboardData {
    totalDevices: number;
    online: number;
    offline: number;
    healthy: number;
    warning: number;
    critical: number;
}

interface DashboardStatsProps {
    data: DashboardData;
}

const StatCard = ({ title, value, icon: Icon, trend, trendLabel, colorClass, bgClass }: any) => (
    <div className="bg-slate-800 rounded-xl border border-slate-700 p-5 shadow-sm hover:shadow-md transition-shadow duration-200">
        <div className="flex items-start justify-between mb-4">
            <div>
                <p className="text-slate-400 text-sm font-medium mb-1">{title}</p>
                <h3 className="text-2xl font-bold text-white tracking-tight">{value}</h3>
            </div>
            <div className={`p-2.5 rounded-lg ${bgClass} ${colorClass}`}>
                <Icon className="w-5 h-5" />
            </div>
        </div>

        {trend && (
            <div className="flex items-center text-sm">
                <span className={`flex items-center font-medium ${trend === 'up' ? 'text-emerald-400' : 'text-rose-400'}`}>
                    {trend === 'up' ? <ArrowUp className="w-3 h-3 mr-1" /> : <ArrowDown className="w-3 h-3 mr-1" />}
                    {trendLabel}
                </span>
                <span className="text-slate-500 ml-2">vs last month</span>
            </div>
        )}
        {!trend && (
            <div className="text-sm text-slate-500">
                Updated just now
            </div>
        )}
    </div>
);

const DashboardStats: React.FC<DashboardStatsProps> = ({ data }) => {
    // Calculate percentages
    const onlineRate = data.totalDevices > 0 ? Math.round((data.online / data.totalDevices) * 100) : 0;

    return (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
            <StatCard
                title="Total Devices"
                value={data.totalDevices}
                icon={Server}
                colorClass="text-indigo-400"
                bgClass="bg-indigo-400/10"
                trend="up"
                trendLabel="+12%"
            />

            <StatCard
                title="Online Rate"
                value={`${onlineRate}%`}
                icon={Wifi}
                colorClass="text-emerald-400"
                bgClass="bg-emerald-400/10"
                trend={onlineRate > 90 ? "up" : "down"}
                trendLabel={`${data.online} Online`}
            />

            <StatCard
                title="Critical Alerts"
                value={data.critical}
                icon={AlertTriangle}
                colorClass="text-rose-400"
                bgClass="bg-rose-400/10"
                trend={data.critical > 0 ? "down" : "up"}
                trendLabel={data.critical > 0 ? "Action Needed" : "All Good"}
            />

            <StatCard
                title="System Health"
                value={`${data.healthy}/${data.totalDevices}`}
                icon={Activity}
                colorClass="text-cyan-400"
                bgClass="bg-cyan-400/10"
                trend="up"
                trendLabel="Stable"
            />
        </div>
    );
};

export default DashboardStats;
