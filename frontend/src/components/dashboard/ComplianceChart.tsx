import React from 'react';
import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip, Legend } from 'recharts';

interface ComplianceChartProps {
    healthy: number;
    warning: number;
    critical: number;
}

const ComplianceChart: React.FC<ComplianceChartProps> = ({ healthy, warning, critical }) => {
    const data = [
        { name: 'Healthy', value: healthy, color: '#10b981' },
        { name: 'Warning', value: warning, color: '#f59e0b' },
        { name: 'Critical', value: critical, color: '#f43f5e' },
    ].filter(d => d.value > 0);

    return (
        <div className="bg-slate-800 rounded-xl border border-slate-700 p-5 h-full flex flex-col">
            <h3 className="text-base font-semibold text-white mb-4">Health Status</h3>

            {data.length > 0 ? (
                <div className="flex-1 min-h-[250px] relative">
                    <ResponsiveContainer width="100%" height="100%">
                        <PieChart>
                            <Pie
                                data={data}
                                innerRadius={60}
                                outerRadius={80}
                                paddingAngle={2}
                                dataKey="value"
                                stroke="none"
                            >
                                {data.map((entry, index) => (
                                    <Cell key={`cell-${index}`} fill={entry.color} />
                                ))}
                            </Pie>
                            <Tooltip
                                contentStyle={{ backgroundColor: '#1e293b', borderColor: '#334155', borderRadius: '8px', color: '#fff' }}
                                itemStyle={{ color: '#fff' }}
                            />
                            <Legend
                                verticalAlign="bottom"
                                height={36}
                                iconType="circle"
                                formatter={(value) => <span className="text-slate-300 text-sm ml-1">{value}</span>}
                            />
                        </PieChart>
                    </ResponsiveContainer>

                    {/* Center Text */}
                    <div className="absolute top-1/2 left-1/2 transform -translate-x-1/2 -translate-y-1/2 -mt-5 text-center pointer-events-none">
                        <div className="text-2xl font-bold text-white">{healthy + warning + critical}</div>
                        <div className="text-xs text-slate-500 uppercase">Total</div>
                    </div>
                </div>
            ) : (
                <div className="flex-1 flex items-center justify-center text-slate-500 text-sm">
                    No data available
                </div>
            )}
        </div>
    );
};

export default ComplianceChart;
