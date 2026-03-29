import React, { useEffect } from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import {
  LayoutDashboard, Monitor, ShoppingCart, Users, Database,
  Inbox, Trash2, FileBarChart2, HardDrive, StickyNote,
  RefreshCw, Settings, History, LogOut, WifiOff, Printer, KeyRound, CalendarDays, Download,
} from 'lucide-react';

interface NavItem {
  to: string;
  label: string;
  icon: React.ReactNode;
  exact?: boolean;
  adminOnly?: boolean;
  shortcut?: string;
}

interface NavGroup {
  title: string;
  items: NavItem[];
}

const navGroups: NavGroup[] = [
  {
    title: 'Genel',
    items: [
      { to: '/',        label: 'Kontrol Paneli', icon: <LayoutDashboard className="w-4 h-4" />, exact: true, shortcut: 'Alt+1' },
      { to: '/devices', label: 'Cihazlar',        icon: <Monitor className="w-4 h-4" />, shortcut: 'Alt+2' },
      { to: '/kasa',    label: 'Kasalar',          icon: <ShoppingCart className="w-4 h-4" />, shortcut: 'Alt+3' },
      { to: '/bilgisayarlar', label: 'Bilgisayarlar', icon: <Monitor className="w-4 h-4" />, shortcut: 'Alt+4' },
      { to: '/offline-logs', label: 'Offline Geçmişi', icon: <WifiOff className="w-4 h-4" /> },
      { to: '/fiscal-errors', label: 'Mali Hata Kodları', icon: <Printer className="w-4 h-4" /> },
      { to: '/printer-licenses', label: 'Yazıcı Lisansları', icon: <KeyRound className="w-4 h-4" /> },
      { to: '/holidays', label: 'Resmi Tatiller', icon: <CalendarDays className="w-4 h-4" /> },
      { to: '/pos-log-analyzer', label: 'Kasa Log Analizi', icon: <FileBarChart2 className="w-4 h-4" /> },
    ],
  },
  {
    title: 'Yönetim',
    items: [
      { to: '/store-managers', label: 'Mağaza Müdürleri', icon: <Users className="w-4 h-4" />, adminOnly: true },
      { to: '/sql-query',      label: 'SQL Sorgu',         icon: <Database className="w-4 h-4" />, adminOnly: true, shortcut: 'Alt+Q' },
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
      { to: '/remote-install', label: 'Uzaktan Kurulum', icon: <Download className="w-4 h-4" />, adminOnly: true },
      { to: '/settings',     label: 'Ayarlar',           icon: <Settings className="w-4 h-4" />, shortcut: 'Alt+S' },
    ],
  },
];

const Sidebar: React.FC = () => {
  const { isAdmin, fullName, role } = useAuth();
  const navigate = useNavigate();

  // Keyboard shortcuts
  useEffect(() => {
    const shortcutMap: Record<string, string> = {};
    for (const group of navGroups) {
      for (const item of group.items) {
        if (item.shortcut) shortcutMap[item.shortcut.toLowerCase()] = item.to;
      }
    }
    const handleKeyDown = (e: KeyboardEvent) => {
      if (!e.altKey) return;
      const key = `alt+${e.key.toLowerCase()}`;
      const target = shortcutMap[key];
      if (target) { e.preventDefault(); navigate(target); }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [navigate]);

  const linkClasses = ({ isActive }: { isActive: boolean }) =>
    `flex items-center gap-2.5 pl-2.5 pr-3 py-2 rounded-lg text-[13px] font-medium transition-all duration-150 border-l-2 ${
      isActive
        ? 'bg-violet-600/15 text-violet-400 border-violet-500'
        : 'text-[#94a3b8] hover:text-[#e2e8f0] hover:bg-white/[0.03] border-transparent'
    }`;

  const initials = (fullName || 'AD').split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2);

  return (
    <aside
      className="w-56 flex flex-col h-full shrink-0 z-40"
      style={{ background: 'var(--cc-strip)', borderRight: '1px solid var(--ms-border)' }}
    >
      {/* Logo */}
      <div className="px-4 py-4 flex items-center gap-3" style={{ borderBottom: '1px solid var(--ms-border)' }}>
        <div className="h-8 w-8 rounded-lg bg-violet-600 flex items-center justify-center shrink-0">
          <span className="text-white font-bold text-[10px]">MS</span>
        </div>
        <div className="min-w-0">
          <div className="text-[#f8fafc] font-bold text-sm tracking-tight">MudoSoft</div>
          <div className="text-[9px] text-violet-400 font-medium tracking-[0.12em] uppercase">RMM Platform</div>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 overflow-y-auto py-3 px-2 space-y-4">
        {navGroups.map((group) => {
          const visibleItems = group.items.filter(item => !item.adminOnly || isAdmin);
          if (visibleItems.length === 0) return null;
          return (
            <div key={group.title}>
              <div className="px-3 mb-1 text-[9px] font-semibold uppercase tracking-[0.12em]" style={{ color: '#1e293b' }}>
                {group.title}
              </div>
              <div className="space-y-0.5">
                {visibleItems.map((item) => (
                  <NavLink key={item.to} to={item.to} end={item.exact} className={linkClasses}>
                    {item.icon}
                    <span className="truncate">{item.label}</span>
                    {item.shortcut && (
                      <kbd className="ml-auto text-[8px] font-mono px-1 py-0.5 rounded shrink-0 hidden lg:inline-block"
                        style={{ color: '#334155', background: 'rgba(255,255,255,0.03)' }}>
                        {item.shortcut}
                      </kbd>
                    )}
                  </NavLink>
                ))}
              </div>
            </div>
          );
        })}
      </nav>

      {/* User */}
      <div className="p-3" style={{ borderTop: '1px solid var(--ms-border)' }}>
        <div className="flex items-center gap-2.5 px-2 py-2 rounded-lg" style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid var(--ms-border)' }}>
          <div className="h-6 w-6 rounded-full bg-violet-700/80 flex items-center justify-center text-white font-semibold text-[9px] shrink-0">
            {initials}
          </div>
          <div className="min-w-0 flex-1">
            <div className="text-[11px] font-medium truncate" style={{ color: '#e2e8f0' }}>{fullName || 'Administrator'}</div>
            <div className="text-[9px] truncate" style={{ color: '#475569' }}>{role === 'Admin' ? 'Admin' : 'Teknisyen'}</div>
          </div>
          <button
            onClick={() => {
              ['isAuthenticated', 'token', 'tokenExpiresAt', 'username', 'role', 'fullName']
                .forEach(k => localStorage.removeItem(k));
              window.location.href = '/login';
            }}
            className="p-1 rounded transition-colors shrink-0"
            style={{ color: '#475569' }}
            title="Çıkış Yap"
          >
            <LogOut className="w-3 h-3" />
          </button>
        </div>
      </div>
    </aside>
  );
};

export default Sidebar;
