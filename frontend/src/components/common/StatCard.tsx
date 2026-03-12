import React from 'react';

interface Props {
  label: string;
  value: string | number;
  icon?: React.ReactNode;
  tone?: 'default' | 'success' | 'danger' | 'warning' | 'info';
  sublabel?: string;
}

const toneConfig = {
  default: { border: 'border-ms-border',      icon: 'bg-zinc-800 text-zinc-400' },
  success: { border: 'border-green-500/30',   icon: 'bg-green-500/10 text-green-400' },
  danger:  { border: 'border-red-500/30',     icon: 'bg-red-500/10 text-red-400' },
  warning: { border: 'border-amber-500/30',   icon: 'bg-amber-500/10 text-amber-400' },
  info:    { border: 'border-blue-500/30',    icon: 'bg-blue-500/10 text-blue-400' },
};

const StatCard: React.FC<Props> = ({ label, value, icon, tone = 'default', sublabel }) => {
  const cfg = toneConfig[tone];

  return (
    <div className={`bg-ms-bg-soft border ${cfg.border} rounded-xl px-4 py-4 flex items-center gap-4`}>
      {icon && (
        <div className={`h-10 w-10 rounded-xl flex items-center justify-center shrink-0 ${cfg.icon}`}>
          {icon}
        </div>
      )}
      <div className="min-w-0">
        <div className="text-xs font-medium text-ms-text-muted uppercase tracking-wider truncate">{label}</div>
        <div className="text-2xl font-bold text-ms-text mt-0.5 leading-none">{value}</div>
        {sublabel && <div className="text-xs text-ms-text-muted mt-1">{sublabel}</div>}
      </div>
    </div>
  );
};

export default StatCard;
