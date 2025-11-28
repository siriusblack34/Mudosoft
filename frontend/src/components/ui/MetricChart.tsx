import React from 'react';
import { AreaChart, Area, XAxis, YAxis, Tooltip, ResponsiveContainer } from 'recharts';
// import type { DeviceMetricDataPoint } from '../../lib/apiClient'; // Kullanılmadığı için kaldırılabilir

// Data artık { name: string, value: number } yapısında olacak
interface ChartDataPoint {
    name: string;
    value: number;
}

interface MetricChartProps {
    title: string;
    data: ChartDataPoint[]; // Düzeltilmiş tip
    value: number;
    color: string;
}

const MetricChart: React.FC<MetricChartProps> = ({ title, data, value, color }) => {

    // İyileştirme: Anlık değer 0'ın altındaysa veya 100'ün üzerindeyse kontrol edilebilir
    // Ancak toFixed(1) ile gösterim şu anda doğru çalışıyor.
    // Eğer DeviceDetailsPage'den gelen value 0 ise, burada 0.0% görünür.
    const displayValue = `${value.toFixed(1)}%`;

    return (
        <div className="bg-gray-800/70 p-4 rounded-xl shadow-lg border border-gray-700 h-full flex flex-col">
            <div className="flex justify-between items-start">
                <p className="text-sm text-gray-400">{title}</p>
                {/* Gösterim doğru çalışıyor, 0 gelirse 0.0% gösterir. */}
                <p className="text-2xl font-bold text-white">{displayValue}</p>
            </div>
            <div className="flex-grow w-full h-32 mt-2">
                <ResponsiveContainer width="100%" height="100%">
                    <AreaChart data={data} margin={{ top: 5, right: 0, left: -25, bottom: 0 }}>
                       <defs>
                            <linearGradient id={`color-${title.replace(/\s+/g, '')}`} x1="0" y1="0" x2="0" y2="1">
                            <stop offset="5%" stopColor={color} stopOpacity={0.4}/>
                            <stop offset="95%" stopColor={color} stopOpacity={0}/>
                            </linearGradient>
                        </defs>
                        {/* dataKey="name" zaten eklenmiş. */}
                        <XAxis dataKey="name" stroke="#9ca3af" fontSize={12} hide={true}/> 
                        <Tooltip
                            contentStyle={{
                                backgroundColor: 'rgba(31, 41, 55, 0.8)',
                                border: '1px solid #4b5563', 
                                borderRadius: '0.5rem',
                                color: '#d1d5db' 
                            }}
                            labelStyle={{ fontWeight: 'bold' }}
                            itemStyle={{ color: color }}
                        />
                        <YAxis stroke="#9ca3af" fontSize={12} domain={[0, 100]} tickFormatter={(v) => `${v}%`} />
                        <Area 
                            type="monotone" 
                            dataKey="value" 
                            stroke={color} 
                            strokeWidth={2} 
                            fillOpacity={1} 
                            fill={`url(#color-${title.replace(/\s+/g, '')})`} 
                        />
                    </AreaChart>
                </ResponsiveContainer>
            </div>
        </div>
    );
};

export default MetricChart;