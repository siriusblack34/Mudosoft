// frontend/src/layout/sidebar.tsx
import React from "react";
import { NavLink } from "react-router-dom";
// Tüm ikonları bir namespace altında alıyoruz (Icons dosyanın doğru export'ları içerdiğinden emin ol)
import * as Icons from "../components/icons/Icons";

const Sidebar: React.FC = () => {
  const linkClasses = ({ isActive }: { isActive: boolean }) =>
    `flex items-center gap-2 px-4 py-2 rounded-xl text-sm transition ${
      isActive ? "bg-ms-accent-soft/10 text-ms-accent" : "text-ms-text-muted hover:bg-ms-panel"
    }`;

  return (
    <aside className="w-64 bg-ms-bg-soft border-r border-ms-border flex flex-col">
      <div className="px-6 py-5 text-lg font-semibold flex items-center gap-2">
        <span className="inline-flex h-8 w-8 rounded-xl bg-ms-accent-soft/20 items-center justify-center">MS</span>
        <div>
          <div>MudoSoft</div>
          <div className="text-xs text-ms-text-muted">RMM Platform</div>
        </div>
      </div>

      <nav className="flex-1 px-3 space-y-1">
        <NavLink to="/" className={linkClasses}>
          <Icons.HomeIcon className="w-5 h-5" />
          Dashboard
        </NavLink>

        <NavLink to="/devices" className={linkClasses}>
          <Icons.ServerIcon className="w-5 h-5" />
          Devices
        </NavLink>

        <NavLink to="/sql-query" className={linkClasses}>
          <Icons.DatabaseIcon className="w-5 h-5" />
          SQL Query
        </NavLink>

        <NavLink to="/actions" className={linkClasses}>
          <Icons.ClockIcon className="w-5 h-5" />
          Action History
        </NavLink>

        <NavLink to="/settings" className={linkClasses}>
          <Icons.SettingsIcon className="w-5 h-5" />
          Settings
        </NavLink>
      </nav>

      <div className="px-4 py-4 text-xs text-ms-text-muted">
        Logged in as <span className="text-ms-text">Administrator</span>
      </div>
    </aside>
  );
};

export default Sidebar;
