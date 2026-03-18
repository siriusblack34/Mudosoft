import React from 'react';
import { NavLink } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import {
  LayoutDashboard, Monitor, ShoppingCart, Users, Database,
  Inbox, Trash2, FileBarChart2, HardDrive, StickyNote,
  RefreshCw, Settings, History, LogOut, ChevronRight, WifiOff, Printer, KeyRound,
} from 'lucide-react';

interface NavItem {
  to: string;
  label: string;
  icon: React.ReactNode;
  exact?: boolean;
  adminOnly?: boolean;
}

interface NavGroup {
  title: string;
  items: NavItem[];
}

const navGroups: NavGroup[] = [
  {
    title: 'Genel',
    items: [
      { to: '/',        label: 'Kontrol Paneli', icon: <LayoutDashboard className="w-4 h-4" />, exact: true },
      { to: '/devices', label: 'Cihazlar',        icon: <Monitor className="w-4 h-4" /> },
      { to: '/kasa',    label: 'Kasalar',          icon: <ShoppingCart className="w-4 h-4" /> },
      { to: '/offline-logs', label: 'Offline Geçmişi', icon: <WifiOff className="w-4 h-4" /> },
      { to: '/fiscal-errors', label: 'Mali Hata Kodları', icon: <Printer className="w-4 h-4" /> },
      { to: '/printer-licenses', label: 'Yazıcı Lisansları', icon: <KeyRound className="w-4 h-4" /> },
    ],
  },
  {
    title: 'Yönetim',
    items: [
      { to: '/store-managers', label: 'Mağaza Müdürleri', icon: <Users className="w-4 h-4" />, adminOnly: true },
      { to: '/sql-query',      label: 'SQL Sorgu',         icon: <Database className="w-4 h-4" />, adminOnly: true },
      { to: '/actions',        label: 'İşlem Geçmişi',     icon: <History className="w-4 h-4" /> },
      { to: '/notes',          label: 'Notlar',            icon: <StickyNote className="w-4 h-4" /> },
    ],
  },
  {
    title: 'Temizlik',
    items: [
      { to: '/inbox-cleanup',  label: 'PLU Cache',    icon: <Inbox className="w-4 h-4" /> },
      { to: '/stock-cleanup',  label: 'PLU SQL',      icon: <Trash2 className="w-4 h-4" /> },
      { to: '/db-log-cleanup', label: 'DB Log',       icon: <FileBarChart2 className="w-4 h-4" />, adminOnly: true },
      { to: '/disk-status',    label: 'Disk Durumu',  icon: <HardDrive className="w-4 h-4" /> },
    ],
  },
  {
    title: 'Sistem',
    items: [
      { to: '/agent-update', label: 'Agent Güncelleme', icon: <RefreshCw className="w-4 h-4" />, adminOnly: true },
      { to: '/settings',     label: 'Ayarlar',           icon: <Settings className="w-4 h-4" /> },
    ],
  },
];

const Sidebar: React.FC = () => {
  const { isAdmin, fullName, role } = useAuth();

  const linkClasses = ({ isActive }: { isActive: boolean }) =>
    `flex items-center gap-2.5 pl-2.5 pr-3 py-2 rounded-lg text-sm font-medium transition-all duration-150 border-l-2 ${
      isActive
        ? 'bg-violet-600/15 text-violet-400 border-violet-500'
        : 'text-ms-text-muted hover:text-ms-text hover:bg-ms-border/60 border-transparent'
    }`;

  const initials = (fullName || 'AD').split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2);

  return (
    <aside className="w-60 bg-ms-bg-soft border-r border-ms-border flex flex-col h-screen sticky top-0 shrink-0 z-40">

      {/* Logo */}
      <div className="px-4 py-4 flex items-center gap-3 border-b border-ms-border">
        <div className="h-9 w-9 rounded-xl bg-violet-600 flex items-center justify-center shadow-lg shadow-violet-900/40 shrink-0">
          <span className="text-white font-bold text-sm">MS</span>
        </div>
        <div className="min-w-0">
          <div className="text-ms-text font-bold text-sm tracking-tight">MudoSoft</div>
          <div className="text-[10px] text-violet-400 font-medium tracking-widest uppercase">RMM Platform</div>
        </div>
      </div>

      {/* Navigasyon */}
      <nav className="flex-1 overflow-y-auto py-3 px-2 space-y-4">
        {navGroups.map((group) => {
          const visibleItems = group.items.filter(item => !item.adminOnly || isAdmin);
          if (visibleItems.length === 0) return null;
          return (
            <div key={group.title}>
              <div className="px-3 mb-1 text-[10px] font-semibold text-ms-text-muted/60 uppercase tracking-widest">
                {group.title}
              </div>
              <div className="space-y-0.5">
                {visibleItems.map((item) => (
                  <NavLink
                    key={item.to}
                    to={item.to}
                    end={item.exact}
                    className={linkClasses}
                  >
                    {item.icon}
                    <span className="truncate">{item.label}</span>
                    <ChevronRight className="w-3 h-3 ml-auto shrink-0 opacity-0 group-[.active]:opacity-60" />
                  </NavLink>
                ))}
              </div>
            </div>
          );
        })}
      </nav>

      {/* Kullanıcı */}
      <div className="p-3 border-t border-ms-border">
        <div className="flex items-center gap-2.5 px-3 py-2.5 rounded-lg bg-ms-bg border border-ms-border">
          <div className="h-7 w-7 rounded-full bg-violet-700 flex items-center justify-center text-white font-semibold text-xs shrink-0">
            {initials}
          </div>
          <div className="min-w-0 flex-1">
            <div className="text-ms-text text-xs font-semibold truncate">{fullName || 'Administrator'}</div>
            <div className="text-ms-text-muted text-[10px] truncate">{role === 'Admin' ? 'Admin' : 'Teknisyen'}</div>
          </div>
          <button
            onClick={() => {
              localStorage.removeItem('isAuthenticated');
              localStorage.removeItem('token');
              localStorage.removeItem('tokenExpiresAt');
              localStorage.removeItem('username');
              localStorage.removeItem('role');
              localStorage.removeItem('fullName');
              window.location.href = '/login';
            }}
            className="p-1.5 text-ms-text-muted hover:text-red-400 hover:bg-red-500/10 rounded-md transition-colors shrink-0"
            title="Çıkış Yap"
          >
            <LogOut className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </aside>
  );
};

export default Sidebar;
