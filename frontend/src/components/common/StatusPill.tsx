import React from 'react';

type StatusTone = 'success' | 'danger' | 'warning' | 'info' | 'default';

interface Props {
  online?: boolean;
  tone?: StatusTone;
  text?: string;
}

const toneConfig: Record<StatusTone, { dot: string; badge: string }> = {
  success: { dot: 'bg-green-500',  badge: 'bg-green-500/10 text-green-400 border-green-500/20' },
  danger:  { dot: 'bg-red-500',   badge: 'bg-red-500/10 text-red-400 border-red-500/20' },
  warning: { dot: 'bg-amber-500', badge: 'bg-amber-500/10 text-amber-400 border-amber-500/20' },
  info:    { dot: 'bg-blue-500',  badge: 'bg-blue-500/10 text-blue-400 border-blue-500/20' },
  default: { dot: 'bg-zinc-500',  badge: 'bg-zinc-700/50 text-zinc-400 border-zinc-600/30' },
};

const StatusPill: React.FC<Props> = ({ online, tone, text }) => {
  const resolvedTone = tone ?? (online ? 'success' : online === false ? 'danger' : 'default');
  const resolvedText = text ?? (online ? 'Online' : online === false ? 'Offline' : 'Unknown');
  const cfg = toneConfig[resolvedTone];

  return (
    <span className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold border ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${cfg.dot}`} />
      {resolvedText}
    </span>
  );
};

export default StatusPill;
