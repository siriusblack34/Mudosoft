import React from 'react';

// StatusPill'in kabul ettiği renk tipleri (Tone)
type StatusTone = 'success' | 'danger' | 'warning' | 'default';

interface Props {
    // ✅ online durumunu kabul et
    online: boolean; 
}

const StatusPill: React.FC<Props> = ({ online }) => {
    // Online durumuna göre metin ve tonu belirle
    const text = online ? "ONLINE" : "OFFLINE";
    // tone burada yalnızca 'success' veya 'danger' olabilir.
    const tone = online ? 'success' : 'danger';
    
    // Tailwind CSS sınıfını tonlara göre dinamik olarak seçme mantığı
    const baseClasses = "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium";

    let colorClasses = "";
    // ✅ DÜZELTME: Sadece olası durumlar (success ve danger) kontrol edilir.
    switch (tone) {
        case 'success':
            colorClasses = "bg-green-100 text-green-800";
            break;
        case 'danger':
            colorClasses = "bg-red-100 text-red-800";
            break;
        default:
            // Güvenli bir varsayılan durum olarak default/danger rengini kullanabiliriz.
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