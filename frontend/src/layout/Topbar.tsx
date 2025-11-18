import React from 'react';

const Topbar: React.FC = () => {
  return (
    <header className="h-14 border-b border-ms-border flex items-center justify-between px-6 bg-ms-bg-soft">
      <div className="text-lg font-semibold">Dashboard</div>
      <div className="flex items-center gap-4 text-sm text-ms-text-muted">
        <button className="rounded-full h-8 w-8 flex items-center justify-center bg-ms-panel border border-ms-border">
          🔔
        </button>
        <div className="flex items-center gap-2">
          <div className="h-8 w-8 rounded-full bg-ms-panel" />
          <div>
            <div className="text-ms-text text-sm">Administrator</div>
            <div className="text-[11px] text-ms-text-muted">Mudo IT</div>
          </div>
        </div>
      </div>
    </header>
  );
};

export default Topbar;
