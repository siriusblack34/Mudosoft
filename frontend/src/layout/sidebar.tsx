// frontend/src/layout/sidebar.tsx
import React from "react";
import { NavLink } from "react-router-dom";
// Tüm ikonları bir namespace altında alıyoruz (Icons dosyanın doğru export'ları içerdiğinden emin ol)
import * as Icons from "../components/icons/Icons";
import { LogOut, Trash2 } from "lucide-react";

const Sidebar: React.FC = () => {
  const linkClasses = ({ isActive }: { isActive: boolean }) =>
    `flex items-center gap-2 px-4 py-2 rounded-xl text-sm transition ${isActive ? "bg-ms-accent-soft/10 text-ms-accent" : "text-ms-text-muted hover:bg-ms-panel"
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

        <NavLink to="/kasa" className={linkClasses}>
          <Icons.ServerIcon className="w-5 h-5" />
          KASA
        </NavLink>

        <NavLink to="/inbox-cleanup" className={linkClasses}>
          <Trash2 className="w-5 h-5" />
          Inbox Temizlik
        </NavLink>

        <NavLink to="/settings" className={linkClasses}>
          <Icons.SettingsIcon className="w-5 h-5" />
          Settings
        </NavLink>

        <NavLink to="/agent-update" className={linkClasses}>
          <Icons.ReloadIcon className="w-5 h-5" />
          Agent Update
        </NavLink>
      </nav>

      <div className="p-4 border-t border-ms-border">
        <div className="flex items-center justify-between">
          <div className="text-xs text-ms-text-muted">
            Logged in as <span className="text-ms-text font-medium block">Administrator</span>
          </div>
          <button
            onClick={() => {
              localStorage.removeItem('isAuthenticated');
              window.location.href = '/login';
            }}
            className="p-2 text-slate-400 hover:text-rose-400 hover:bg-rose-500/10 rounded-lg transition-colors"
            title="Sign Out"
          >
            <LogOut className="w-4 h-4" />
          </button>
        </div>
      </div>
    </aside>
  );
};

export default Sidebar;
