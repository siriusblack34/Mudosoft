import React from 'react';

// StatusPill'in kabul ettiği renk tipleri (Tone)
type StatusTone = 'success' | 'danger' | 'warning' | 'default';

interface Props {
    text: string;
    // ÇÖZÜM: 'tone' prop'u eklendi
    tone: StatusTone; 
}

const StatusPill: React.FC<Props> = ({ text, tone }) => {
    // Tailwind CSS sınıfını tonlara göre dinamik olarak seçme mantığı
    const baseClasses = "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium";

    let colorClasses = "";
    switch (tone) {
        case 'success':
            colorClasses = "bg-green-100 text-green-800";
            break;
        case 'danger':
            colorClasses = "bg-red-100 text-red-800";
            break;
        case 'warning':
            colorClasses = "bg-yellow-100 text-yellow-800";
            break;
        case 'default':
        default:
            colorClasses = "bg-gray-100 text-gray-800";
            break;
    }

    return (
        <span className={`${baseClasses} ${colorClasses}`}>
            {text}
        </span>
    );
};

export default StatusPill;