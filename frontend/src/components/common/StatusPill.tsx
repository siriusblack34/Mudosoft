import React from 'react';

interface Props {
  online: boolean;
}

const StatusPill: React.FC<Props> = ({ online }) => {
  const color = online ? 'bg-ms-accent-soft' : 'bg-ms-danger';
  const label = online ? 'Online' : 'Offline';
  return (
    <span className="inline-flex items-center gap-1 text-xs px-2 py-1 rounded-full bg-ms-bg-soft border border-ms-border">
      <span className={`h-2 w-2 rounded-full ${color}`} />
      {label}
    </span>
  );
};

export default StatusPill;
