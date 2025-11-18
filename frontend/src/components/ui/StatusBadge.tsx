
import React from 'react';
import { DeviceStatus } from '../../types';

interface StatusBadgeProps {
  status: DeviceStatus;
  showText?: boolean;
}

const StatusBadge: React.FC<StatusBadgeProps> = ({ status, showText = true }) => {
  const statusConfig = {
    [DeviceStatus.Online]: { color: 'bg-green-500', text: 'Online' },
    [DeviceStatus.Offline]: { color: 'bg-red-500', text: 'Offline' },
    [DeviceStatus.Warning]: { color: 'bg-yellow-500', text: 'Warning' },
  };

  const { color, text } = statusConfig[status];

  return (
    <div className="flex items-center space-x-2">
      <span className={`h-3 w-3 rounded-full ${color}`}></span>
      {showText && <span className="text-sm font-medium">{text}</span>}
    </div>
  );
};

export default StatusBadge;
