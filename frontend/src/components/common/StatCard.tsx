import React from 'react';

interface Props {
  label: string;
  value: string | number;
  icon?: React.ReactNode;
  tone?: 'default' | 'success' | 'danger';
}

const toneClasses: Record<Props['tone'], string> = {
  default: 'border-ms-border',
  success: 'border-emerald-500/50',
  danger: 'border-red-500/50'
};

const StatCard: React.FC<Props> = ({ label, value, icon, tone = 'default' }) => {
  return (
    <div
      className={`flex items-center gap-3 rounded-2xl border px-4 py-3 bg-ms-panel ${toneClasses[tone]}`}
    >
      {icon && (
        <div className="h-10 w-10 rounded-xl bg-ms-bg-soft flex items-center justify-center text-xl">
          {icon}
        </div>
      )}
      <div>
        <div className="text-xs uppercase tracking-wide text-ms-text-muted">{label}</div>
        <div className="text-xl font-semibold">{value}</div>
      </div>
    </div>
  );
};

export default StatCard;
