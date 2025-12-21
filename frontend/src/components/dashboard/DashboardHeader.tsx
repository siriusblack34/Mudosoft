import React from 'react';
import { Calendar, RefreshCw } from 'lucide-react';

interface DashboardHeaderProps {
    lastRefreshed: Date;
    onRefresh: () => void;
    isLoading: boolean;
}

const DashboardHeader: React.FC<DashboardHeaderProps> = ({ lastRefreshed, onRefresh, isLoading }) => {
    return (
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6">
            <div>
                <h1 className="text-2xl font-bold text-white">Dashboard</h1>
                <p className="text-slate-400 text-sm mt-1">Overview of your IT infrastructure</p>
            </div>

            <div className="flex items-center gap-3">
                <div className="hidden md:flex flex-col items-end mr-2">
                    <span className="text-xs text-slate-500 font-medium uppercase tracking-wider">Last Updated</span>
                    <div className="flex items-center text-slate-300 text-sm font-medium">
                        <Calendar className="w-3.5 h-3.5 mr-1.5 text-slate-500" />
                        {lastRefreshed.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </div>
                </div>

                <button
                    onClick={onRefresh}
                    disabled={isLoading}
                    className={`p-2 rounded-lg border border-slate-700 bg-slate-800 text-slate-400 hover:text-white hover:border-slate-600 transition-all ${isLoading ? 'animate-spin' : ''}`}
                    title="Refresh Data"
                >
                    <RefreshCw className="w-5 h-5" />
                </button>
            </div>
        </div>
    );
};

export default DashboardHeader;
