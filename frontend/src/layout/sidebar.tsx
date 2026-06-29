import React, { useEffect, useMemo, useRef, useState } from 'react';
import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { useMenuVisibility } from '../contexts/MenuVisibilityContext';
import { useTheme } from '../contexts/ThemeContext';
import Logo from '../components/Logo';
import {
  Activity,
  Archive,
  BarChart3,
  Boxes,
  Building2,
  CalendarDays,
  Cast,
  ClipboardList,
  Contact,
  Database,
  Download,
  FileSearch,
  FileBarChart2,
  FileCode,
  HardDrive,
  History,
  KeyRound,
  LayoutDashboard,
  LogOut,
  Mail,
  Megaphone,
  Menu,
  Monitor,
  Printer,
  Radio,
  RefreshCw,
  Settings,
  Shield,
  ShieldCheck,
  ShoppingCart,
  Sparkles,
  StickyNote,
  Users,
  Users2,
  WifiOff,
  Wrench,
  X,
  Zap,
  CalendarCheck,
  Tag,
} from 'lucide-react';

export interface NavItem {
  to: string;
  label: string;
  icon: React.ReactNode;
  exact?: boolean;
  shortcut?: string;
  /** Extra paths that should mark this item active (e.g. sub-pages accessed via tabs) */
  matchPaths?: string[];
  /** Sadece Admin rolünde görünür */
  requiresAdmin?: boolean;
}

export interface NavGroup {
  title: string;
  shortLabel: string;
  icon: React.ReactNode;
  items: NavItem[];
}

export const navGroups: NavGroup[] = [
  {
    title: 'Ana Sayfa',
    shortLabel: 'Home',
    icon: <LayoutDashboard className="h-5 w-5" />,
    items: [
      { to: '/', label: 'Kontrol Paneli', icon: <LayoutDashboard className="h-4 w-4" />, exact: true, shortcut: 'Alt+1' },
    ],
  },
  {
    title: 'Mağazalar ve Envanter',
    shortLabel: 'Mağaza',
    icon: <Building2 className="h-5 w-5" />,
    items: [
      { to: '/magazalar', label: 'Mağaza Listesi', icon: <Building2 className="h-4 w-4" />, shortcut: 'Alt+2' },
      { to: '/store-openings', label: 'Mağaza Açılışları', icon: <ClipboardList className="h-4 w-4" />, matchPaths: ['/store-openings'] },
      { to: '/bilgisayarlar', label: 'Bilgisayarlar', icon: <Monitor className="h-4 w-4" /> },
      { to: '/kasa', label: 'Kasalar', icon: <Archive className="h-4 w-4" /> },
      { to: '/yazicilar', label: 'Yazıcılar', icon: <Printer className="h-4 w-4" /> },
      { to: '/routerlar', label: 'Routerlar', icon: <Activity className="h-4 w-4" /> },
      { to: '/store-managers', label: 'Mağaza Müdürleri', icon: <Users className="h-4 w-4" /> },
    ],
  },
  {
    title: 'Uzak',
    shortLabel: 'Uzak',
    icon: <Cast className="h-5 w-5" />,
    items: [
      { to: '/devices', label: 'Agent', icon: <Monitor className="h-4 w-4" />, shortcut: 'Alt+3' },
      { to: '/remote/sessions', label: 'Aktif Oturumlar', icon: <Radio className="h-4 w-4" /> },
      { to: '/remote/history', label: 'Oturum Geçmişi', icon: <History className="h-4 w-4" /> },
      { to: '/cleanup', label: 'Temizlik Merkezi', icon: <Sparkles className="h-4 w-4" />, shortcut: 'Alt+T' },
    ],
  },
  {
    title: 'IT Operasyon',
    shortLabel: 'Operasyon',
    icon: <Wrench className="h-5 w-5" />,
    items: [
      { to: '/service-monitor', label: 'Servis Monitörü', icon: <Shield className="h-4 w-4" /> },
      { to: '/ariza-bildirim', label: 'Arıza Bildirimleri', icon: <Mail className="h-4 w-4" /> },
      { to: '/ag-teshis', label: 'Ağ Teşhis', icon: <Activity className="h-4 w-4" />, shortcut: 'Alt+4' },
      { to: '/actions', label: 'İşlem Geçmişi', icon: <History className="h-4 w-4" /> },
      { to: '/notes', label: 'Notlar', icon: <StickyNote className="h-4 w-4" /> },
      { to: '/kampanya-senkron', label: 'Kampanya Senkron', icon: <ShieldCheck className="h-4 w-4" /> },
      { to: '/kampanya-kontrol', label: 'Kampanya Kontrol', icon: <Tag className="h-4 w-4" /> },
    ],
  },
  {
    title: 'Araçlar',
    shortLabel: 'Araç',
    icon: <Database className="h-5 w-5" />,
    items: [
      { to: '/sql-query', label: 'SQL Sorgu', icon: <Database className="h-4 w-4" />, shortcut: 'Alt+Q' },
      { to: '/event-log-diagnostics', label: 'Event Log Teşhis', icon: <FileSearch className="h-4 w-4" /> },
      { to: '/pos-log-analyzer', label: 'Kasa Log Analizi', icon: <FileBarChart2 className="h-4 w-4" /> },
      { to: '/fiscal-errors', label: 'Mali Hata Kodları', icon: <Printer className="h-4 w-4" /> },
      { to: '/agent-update', label: 'Agent Güncelleme', icon: <RefreshCw className="h-4 w-4" /> },
      { to: '/remote-install', label: 'Uzaktan Kurulum', icon: <Download className="h-4 w-4" /> },
      { to: '/active-directory', label: 'Active Directory', icon: <Building2 className="h-4 w-4" />, requiresAdmin: true },
      { to: '/batch-scripts', label: 'Acil Bat Çalıştır', icon: <FileCode className="h-4 w-4" /> },
      { to: '/genius-pos-sagligi', label: 'Genius POS Sağlığı', icon: <ShoppingCart className="h-4 w-4" /> },
    ],
  },
  {
    title: 'Raporlar',
    shortLabel: 'Rapor',
    icon: <BarChart3 className="h-5 w-5" />,
    items: [
      { to: '/offline-logs', label: 'Offline Geçmişi', icon: <WifiOff className="h-4 w-4" /> },
      { to: '/reports/store-outages', label: 'Mağaza Kesintileri', icon: <WifiOff className="h-4 w-4" /> },
      { to: '/reports/fault-density', label: 'Arıza Yoğunluğu', icon: <Activity className="h-4 w-4" /> },
      { to: '/vardiya-raporu', label: 'Vardiya Devir Raporu', icon: <ClipboardList className="h-4 w-4" /> },
    ],
  },
  {
    title: 'Envanter',
    shortLabel: 'Envanter',
    icon: <Boxes className="h-5 w-5" />,
    items: [
      { to: '/inventory', label: 'Envanter (SDP)', icon: <Boxes className="h-4 w-4" /> },
      { to: '/reports/hardware-inventory', label: 'Donanım Envanteri', icon: <HardDrive className="h-4 w-4" /> },
      { to: '/printer-licenses', label: 'Yazıcı Lisansları', icon: <KeyRound className="h-4 w-4" /> },
    ],
  },
  {
    title: 'Kurumsal',
    shortLabel: 'Kurumsal',
    icon: <Users2 className="h-5 w-5" />,
    items: [
      { to: '/team', label: 'BT Ekibi', icon: <Users2 className="h-4 w-4" /> },
      { to: '/personel', label: 'Personel', icon: <Contact className="h-4 w-4" /> },
      { to: '/gundem', label: 'IT Gündemi', icon: <Megaphone className="h-4 w-4" /> },
      { to: '/holidays', label: 'Resmi Tatiller', icon: <CalendarDays className="h-4 w-4" /> },
      { to: '/nobetci-takip', label: 'Nöbetçi Takip', icon: <CalendarCheck className="h-4 w-4" /> },
    ],
  },
  {
    title: 'Otomasyon',
    shortLabel: 'Otomasyon',
    icon: <Zap className="h-5 w-5" />,
    items: [
      { to: '/playbooks', label: 'Playbook\'lar', icon: <Zap className="h-4 w-4" /> },
    ],
  },
  {
    title: 'Sistem',
    shortLabel: 'Sistem',
    icon: <Settings className="h-5 w-5" />,
    items: [
      { to: '/settings', label: 'Ayarlar', icon: <Settings className="h-4 w-4" />, shortcut: 'Alt+S' },
    ],
  },
];

const AVATAR_STORAGE_KEY = 'msSelectedAvatar';

const avatarOptions = [
  { id: 'series-lead', label: 'Series Lead', tone: 'Karakter', url: 'https://api.dicebear.com/9.x/lorelei/svg?seed=MudoSeriesLead&backgroundColor=0f172a,7f1d1d&radius=50' },
  { id: 'ops-captain', label: 'Ops Captain', tone: 'Takım kaptanı', url: 'https://api.dicebear.com/9.x/notionists/svg?seed=MudoOpsCaptain&backgroundColor=111827,581c87&radius=50' },
  { id: 'quest-runner', label: 'Quest Runner', tone: 'Macera', url: 'https://api.dicebear.com/9.x/adventurer/svg?seed=MudoQuestRunner&backgroundColor=164e63,312e81&radius=50' },
  { id: 'street-agent', label: 'Street Agent', tone: 'Animasyon', url: 'https://api.dicebear.com/9.x/avataaars/svg?seed=MudoStreetAgent&backgroundColor=0f172a,14532d&radius=50' },
  { id: 'mecha-pilot', label: 'Mecha Pilot', tone: 'Robot', url: 'https://api.dicebear.com/9.x/bottts-neutral/svg?seed=MudoMechaPilot&backgroundColor=1e293b,0f766e&radius=50' },
  { id: 'field-chief', label: 'Field Chief', tone: 'Operasyon', url: 'https://api.dicebear.com/9.x/personas/svg?seed=MudoFieldChief&backgroundColor=0f172a,155e75&radius=50' },
  { id: 'night-admin', label: 'Night Admin', tone: 'Admin', url: 'https://api.dicebear.com/9.x/lorelei/svg?seed=MudoNightAdmin&backgroundColor=111827,1e3a8a&radius=50' },
  { id: 'support-pro', label: 'Support Pro', tone: 'Destek', url: 'https://api.dicebear.com/9.x/adventurer/svg?seed=MudoSupportPro&backgroundColor=0f172a,166534&radius=50' },
  { id: 'system-scout', label: 'System Scout', tone: 'İzleme', url: 'https://api.dicebear.com/9.x/notionists/svg?seed=MudoSystemScout&backgroundColor=0f172a,0f766e&radius=50' },
  { id: 'tech-lead', label: 'Tech Lead', tone: 'Teknik', url: 'https://api.dicebear.com/9.x/avataaars/svg?seed=MudoTechLead&backgroundColor=111827,0369a1&radius=50' },
  { id: 'route-master', label: 'Route Master', tone: 'Saha', url: 'https://api.dicebear.com/9.x/personas/svg?seed=MudoRouteMaster&backgroundColor=0f172a,365314&radius=50' },
  { id: 'signal-guard', label: 'Signal Guard', tone: 'Güvenlik', url: 'https://api.dicebear.com/9.x/bottts-neutral/svg?seed=MudoSignalGuard&backgroundColor=111827,7f1d1d&radius=50' },
  { id: 'calm-operator', label: 'Calm Operator', tone: 'Operatör', url: 'https://api.dicebear.com/9.x/lorelei/svg?seed=MudoCalmOperator&backgroundColor=0f172a,334155&radius=50' },
];

function sidebarPalette(isDark: boolean) {
  if (isDark) {
    return {
      page: '#060d16',
      card: '#0c1525',
      cardSoft: '#111d2b',
      border: 'rgba(148,163,184,0.10)',
      text: '#f1f5f9',
      muted: '#94a3b8',
      subtle: '#64748b',
      sky: '#38bdf8',
      skyStrong: '#0ea5e9',
      hover: 'rgba(56,189,248,0.06)',
      shadow: 'rgba(0,0,0,0.40)',
    };
  }

  return {
    page: '#f0f4f8',
    card: '#ffffff',
    cardSoft: '#f1f5f9',
    border: 'rgba(15,23,42,0.07)',
    text: '#0f172a',
    muted: '#475569',
    subtle: '#94a3b8',
    sky: '#0284c7',
    skyStrong: '#0284c7',
    hover: 'rgba(2,132,199,0.04)',
    shadow: 'rgba(148,163,184,0.16)',
  };
}

const Sidebar: React.FC = () => {
  const { isAdmin, fullName, role, username } = useAuth();
  const { allowedPaths } = useMenuVisibility();
  const { isDark } = useTheme();
  const navigate = useNavigate();
  const location = useLocation();
  const sidebarRef = useRef<HTMLElement | null>(null);
  const [openGroupTitle, setOpenGroupTitle] = useState<string | null>(null);
  const [avatarOpen, setAvatarOpen] = useState(false);
  const [selectedAvatarId, setSelectedAvatarId] = useState<string>(() => {
    try {
      return localStorage.getItem(AVATAR_STORAGE_KEY) || avatarOptions[0].id;
    } catch {
      return avatarOptions[0].id;
    }
  });

  useEffect(() => {
    try { localStorage.setItem(AVATAR_STORAGE_KEY, selectedAvatarId); } catch { /* ignore */ }
  }, [selectedAvatarId]);

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
      if (target) {
        e.preventDefault();
        navigate(target);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [navigate]);

  useEffect(() => {
    setOpenGroupTitle(null);
    setAvatarOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    const hasOpenSurface = Boolean(openGroupTitle || avatarOpen);
    if (!hasOpenSurface) return;

    const closeFloatingSurfaces = () => {
      setOpenGroupTitle(null);
      setAvatarOpen(false);
    };

    const handlePointerDown = (event: PointerEvent) => {
      const target = event.target;
      if (!(target instanceof Node)) return;
      if (sidebarRef.current?.contains(target)) return;
      closeFloatingSurfaces();
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') closeFloatingSurfaces();
    };

    document.addEventListener('pointerdown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [avatarOpen, openGroupTitle]);

  const visibleGroups = useMemo(() => {
    return navGroups
      .map((group) => {
        const items = isAdmin
          ? group.items
          : group.items.filter((item) => !item.requiresAdmin && allowedPaths.has(item.to));
        return { ...group, items };
      })
      .filter((group) => group.items.length > 0);
  }, [allowedPaths, isAdmin]);

  const closeSession = () => {
    ['isAuthenticated', 'token', 'tokenExpiresAt', 'username', 'role', 'fullName']
      .forEach(k => localStorage.removeItem(k));
    window.location.href = '/login';
  };

  const isItemActive = (item: NavItem): boolean => {
    if (item.exact) return location.pathname === item.to;
    if (location.pathname === item.to || location.pathname.startsWith(item.to + '/')) return true;
    if (item.matchPaths?.some(p => location.pathname === p || location.pathname.startsWith(p + '/'))) return true;
    return false;
  };

  const activeGroupTitle = visibleGroups.find(group => group.items.some(isItemActive))?.title;
  const openGroup = visibleGroups.find(group => group.title === openGroupTitle);
  const selectedAvatar = avatarOptions.find(a => a.id === selectedAvatarId) ?? avatarOptions[0];
  const c = sidebarPalette(isDark);
  const sidebarStyle = {
    '--sidebar-page': c.page,
    '--sidebar-card': c.card,
    '--sidebar-card-soft': c.cardSoft,
    '--sidebar-border': c.border,
    '--sidebar-text': c.text,
    '--sidebar-muted': c.muted,
    '--sidebar-subtle': c.subtle,
    '--sidebar-sky': c.sky,
    '--sidebar-sky-strong': c.skyStrong,
    '--sidebar-hover': c.hover,
    '--sidebar-shadow': c.shadow,
    background: c.page,
    borderColor: c.border,
  } as React.CSSProperties;

  const handleGroupClick = (group: NavGroup) => {
    setAvatarOpen(false);
    if (group.items.length === 1) {
      navigate(group.items[0].to);
      return;
    }
    setOpenGroupTitle(current => current === group.title ? null : group.title);
  };

  const menuButtonClass = (active: boolean) => [
    'group relative flex h-[62px] w-full flex-col items-center justify-center gap-1 overflow-hidden px-1 text-center transition-all duration-150',
    active
      ? 'bg-[var(--sidebar-sky-strong)] text-white shadow-[inset_3px_0_0_rgba(241,245,249,0.78)]'
      : 'text-[var(--sidebar-muted)] hover:bg-[var(--sidebar-hover)] hover:text-[var(--sidebar-text)]',
  ].join(' ');

  return (
    <aside
      ref={sidebarRef}
      className="relative z-40 flex h-full w-[72px] shrink-0 flex-col border-r border-[var(--sidebar-border)] text-[var(--sidebar-text)] shadow-[8px_0_24px_var(--sidebar-shadow)]"
      style={sidebarStyle}
    >
      <div className="flex h-12 shrink-0 items-center justify-center border-b border-[var(--sidebar-border)]">
        <button
          type="button"
          onClick={() => {
            setOpenGroupTitle(null);
            setAvatarOpen(false);
          }}
          className="flex h-9 w-9 items-center justify-center rounded-lg text-[var(--sidebar-muted)] transition-colors hover:bg-[var(--sidebar-hover)] hover:text-[var(--sidebar-text)]"
          title="Menüyü kapat"
          aria-label="Menüyü kapat"
        >
          <Menu className="h-5 w-5" />
        </button>
      </div>

      <nav className="min-h-0 flex-1 overflow-y-auto overflow-x-hidden py-1">
        {visibleGroups.map((group) => {
          const active = activeGroupTitle === group.title;
          const expanded = openGroupTitle === group.title;
          return (
            <button
              key={group.title}
              type="button"
              onClick={() => handleGroupClick(group)}
              className={menuButtonClass(active)}
              title={group.title}
              aria-expanded={group.items.length > 1 ? expanded : undefined}
            >
              <span className={active ? 'text-white' : 'text-[var(--sidebar-muted)] transition-colors group-hover:text-[var(--sidebar-text)]'}>
                {group.icon}
              </span>
              <span className="w-full truncate text-[10px] font-bold leading-tight tracking-[-0.03em]">
                {group.shortLabel}
              </span>
              {expanded && group.items.length > 1 && (
                <span className="absolute right-0 top-1/2 h-8 w-[3px] -translate-y-1/2 rounded-l bg-[var(--sidebar-text)]" />
              )}
            </button>
          );
        })}
      </nav>

      <div className="relative shrink-0 border-t border-[var(--sidebar-border)] px-2 py-2">
        {avatarOpen && (
          <div className="absolute bottom-3 left-[78px] w-[244px] rounded-2xl border border-[var(--sidebar-border)] bg-[var(--sidebar-card)] p-3 shadow-2xl">
            <div className="mb-2 flex items-center justify-between gap-2">
              <div>
                <div className="text-[12px] font-semibold text-[var(--sidebar-text)]">Avatar seç</div>
                <div className="text-[10px] text-[var(--sidebar-muted)]">{fullName || username || 'Kullanıcı'}</div>
              </div>
              <button
                type="button"
                onClick={() => setAvatarOpen(false)}
                className="flex h-7 w-7 items-center justify-center rounded-lg text-[var(--sidebar-muted)] transition-colors hover:bg-[var(--sidebar-hover)] hover:text-[var(--sidebar-text)]"
                title="Kapat"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            </div>
            <div className="grid grid-cols-3 gap-2">
              {avatarOptions.map((avatar) => {
                const selected = avatar.id === selectedAvatar.id;
                return (
                  <button
                    key={avatar.id}
                    type="button"
                    onClick={() => {
                      setSelectedAvatarId(avatar.id);
                      setAvatarOpen(false);
                    }}
                    className={`rounded-xl border p-1.5 transition-colors ${
                      selected
                        ? 'border-[var(--sidebar-sky)] bg-[color:var(--sidebar-hover)]'
                        : 'border-[var(--sidebar-border)] bg-[var(--sidebar-card-soft)] hover:border-[var(--sidebar-sky)]'
                    }`}
                    title={`${avatar.label} - ${avatar.tone}`}
                  >
                    <img src={avatar.url} alt={avatar.label} className="mx-auto h-10 w-10 rounded-full object-cover" />
                    <span className="mt-1 block truncate text-center text-[9px] font-medium text-[var(--sidebar-text)]">
                      {avatar.label}
                    </span>
                  </button>
                );
              })}
            </div>
          </div>
        )}

        <button
          type="button"
          onClick={() => {
            setOpenGroupTitle(null);
            setAvatarOpen(open => !open);
          }}
          className="mx-auto block h-10 w-10 overflow-hidden rounded-full border border-[var(--sidebar-border)] bg-[var(--sidebar-card-soft)] transition-transform hover:scale-105"
          title={`${fullName || username || 'Kullanıcı'} - ${role === 'Admin' ? 'Admin' : 'Teknisyen'}`}
        >
          <img src={selectedAvatar.url} alt={selectedAvatar.label} className="h-full w-full object-cover" />
        </button>

        <button
          type="button"
          onClick={closeSession}
          className="mt-2 flex h-9 w-full items-center justify-center rounded-xl text-[var(--sidebar-muted)] transition-colors hover:bg-red-500/15 hover:text-red-300"
          title="Çıkış Yap"
          aria-label="Çıkış Yap"
        >
          <LogOut className="h-4 w-4" />
        </button>
      </div>

      {openGroup && openGroup.items.length > 1 && (
        <div className="absolute bottom-0 left-full top-0 z-50 w-64 border-r border-[var(--sidebar-border)] bg-[var(--sidebar-card)] shadow-[18px_0_34px_var(--sidebar-shadow)] animate-slide-in">
          <div className="flex h-14 items-center justify-between border-b border-[var(--sidebar-border)] px-4">
            <div className="flex min-w-0 items-center gap-3">
              <Logo size={42} idSuffix={`drawer-${openGroup.title}`} />
              <div className="min-w-0">
                <div className="text-[10px] font-bold uppercase tracking-[0.22em] text-[var(--sidebar-sky)]">Orchestra</div>
                <div className="truncate text-sm font-semibold text-[var(--sidebar-text)]">{openGroup.title}</div>
              </div>
            </div>
            <button
              type="button"
              onClick={() => setOpenGroupTitle(null)}
              className="flex h-8 w-8 items-center justify-center rounded-lg text-[var(--sidebar-muted)] transition-colors hover:bg-[var(--sidebar-hover)] hover:text-[var(--sidebar-text)]"
              title="Alt menüyü kapat"
            >
              <X className="h-4 w-4" />
            </button>
          </div>

          <div className="space-y-1 p-3">
            {openGroup.items.map((item) => {
              const active = isItemActive(item);
              return (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.exact}
                  className={[
                    'group flex h-11 items-center gap-3 rounded-xl px-3 text-[13px] font-semibold transition-colors',
                    active
                      ? 'bg-[var(--sidebar-sky-strong)] text-white shadow-[0_10px_22px_rgba(14,165,233,0.2)]'
                      : 'text-[var(--sidebar-text)] hover:bg-[var(--sidebar-hover)]',
                  ].join(' ')}
                >
                  <span className={active ? 'text-white' : 'text-[var(--sidebar-muted)] transition-colors group-hover:text-[var(--sidebar-text)]'}>
                    {item.icon}
                  </span>
                  <span className="min-w-0 flex-1 truncate">{item.label}</span>
                  {item.shortcut && (
                    <kbd className={`rounded border px-1.5 py-0.5 font-mono text-[9px] ${
                      active
                        ? 'border-white/30 bg-white/15 text-white/85'
                        : 'border-[var(--sidebar-border)] bg-[var(--sidebar-card-soft)] text-[var(--sidebar-muted)]'
                    }`}>
                      {item.shortcut}
                    </kbd>
                  )}
                </NavLink>
              );
            })}
          </div>
        </div>
      )}
    </aside>
  );
};

export default Sidebar;
