import React, { useEffect, useState } from 'react';
import StatusBand from './StatusBand';
import Sidebar from './sidebar';
import { apiClient } from '../lib/apiClient';
import { Device } from '../types';

interface Props {
  children: React.ReactNode;
}

const ShellLayout: React.FC<Props> = ({ children }) => {
  const [devices, setDevices] = useState<Device[]>([]);

  useEffect(() => {
    const load = () => { apiClient.getDevices().then(setDevices).catch(() => {}); };
    load();
    const interval = setInterval(load, 30_000);
    return () => clearInterval(interval);
  }, []);

  return (
    <div className="h-screen flex flex-col overflow-hidden" style={{ background: 'var(--ms-bg)' }}>
      <StatusBand devices={devices} />
      <div className="flex flex-1 overflow-hidden min-h-0">
        <Sidebar />
        <main className="flex-1 overflow-y-auto overflow-x-hidden min-w-0">
          <div className="px-6 py-5 animate-fade-in">
            {children}
          </div>
        </main>
      </div>
    </div>
  );
};

export default ShellLayout;
