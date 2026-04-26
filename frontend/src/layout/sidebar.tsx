import React, { useEffect, useMemo, useState } from 'react';
import { NavLink, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { useMenuVisibility } from '../contexts/MenuVisibilityContext';
import {
  LayoutDashboard, Monitor, Users, Database, FileBarChart2, HardDrive, StickyNote,
  RefreshCw, Settings, History, LogOut, WifiOff, Printer, KeyRound, CalendarDays, Download, Users2, Contact, Activity, Megaphone,
  Archive, ShieldCheck, Sparkles, BarChart3, Building2, Server, Mail, Search, X, Wrench,
} from 'lucide-react';

export interface NavItem {
  to: string;
  label: string;
  icon: React.ReactNode;
  exact?: boolean;
  shortcut?: string;
  /** Extra paths that should mark this item active (e.g. sub-pages accessed via tabs) */
  matchPaths?: string[];
}

export interface NavGroup {
  title: string;
  icon: React.ReactNode;
  items: NavItem[];
}

export const navGroups: NavGroup[] = [
  {
    title: 'İzleme',
    icon: <Activity className="w-4 h-4" />,
    items: [
      { to: '/',          label: 'Kontrol Paneli', icon: <LayoutDashboard className="w-4 h-4" />, exact: true, shortcut: 'Alt+1' },
      { to: '/magazalar', label: 'Mağazalar',      icon: <Building2 className="w-4 h-4" />, shortcut: 'Alt+2' },
      { to: '/devices',   label: 'Cihazlar',       icon: <Monitor className="w-4 h-4" />, shortcut: 'Alt+3',
        matchPaths: ['/bilgisayarlar', '/kasa', '/routerlar'] },
      { to: '/ag-teshis', label: 'Ağ Teşhis',      icon: <Activity className="w-4 h-4" />, shortcut: 'Alt+4' },
    ],
  },
  {
    title: 'Araçlar',
    icon: <Wrench className="w-4 h-4" />,
    items: [
      { to: '/sql-query',      label: 'SQL Sorgu',       icon: <Database className="w-4 h-4" />, shortcut: 'Alt+Q' },
      { to: '/actions',        label: 'İşlem Geçmişi',    icon: <History className="w-4 h-4" /> },
      { to: '/notes',          label: 'Notlar',           icon: <StickyNote className="w-4 h-4" /> },
      { to: '/ariza-bildirim', label: 'Arıza Bildirimi',  icon: <Mail className="w-4 h-4" /> },
      { to: '/store-managers', label: 'Mağaza Müdürleri', icon: <Users className="w-4 h-4" /> },
    ],
  },
  {
    title: 'Geçmiş & Raporlar',
    icon: <BarChart3 className="w-4 h-4" />,
    items: [
      { to: '/offline-logs',               label: 'Offline Geçmişi',    icon: <WifiOff className="w-4 h-4" /> },
      { to: '/reports/store-outages',      label: 'Mağaza Kesintisi',   icon: <WifiOff className="w-4 h-4" /> },
      { to: '/reports/fault-density',      label: 'Arıza Yoğunluğu',    icon: <Activity className="w-4 h-4" /> },
      { to: '/reports/hardware-inventory', label: 'Donanım Envanteri',  icon: <HardDrive className="w-4 h-4" /> },
      { to: '/fiscal-errors',              label: 'Mali Hata Kodları',  icon: <Printer className="w-4 h-4" /> },
      { to: '/printer-licenses',           label: 'Yazıcı Lisansları',  icon: <KeyRound className="w-4 h-4" /> },
      { to: '/pos-log-analyzer',           label: 'Kasa Log Analizi',   icon: <FileBarChart2 className="w-4 h-4" /> },
      { to: '/holidays',                   label: 'Resmi Tatiller',     icon: <CalendarDays className="w-4 h-4" /> },
    ],
  },
  {
    title: 'Ekip',
    icon: <Users2 className="w-4 h-4" />,
    items: [
      { to: '/personel', label: 'Personel',   icon: <Contact className="w-4 h-4" /> },
      { to: '/gundem',   label: 'IT Gündemi', icon: <Megaphone className="w-4 h-4" /> },
      { to: '/team',     label: 'BT Ekibi',   icon: <Users2 className="w-4 h-4" /> },
    ],
  },
  {
    title: 'Sistem',
    icon: <Server className="w-4 h-4" />,
    items: [
      { to: '/cleanup',        label: 'Temizlik Merkezi',  icon: <Sparkles className="w-4 h-4" />, shortcut: 'Alt+T' },
      { to: '/agent-update',   label: 'Agent Güncelleme',  icon: <RefreshCw className="w-4 h-4" /> },
      { to: '/remote-install', label: 'Uzaktan Kurulum',   icon: <Download className="w-4 h-4" /> },
      { to: '/settings',       label: 'Ayarlar',           icon: <Settings className="w-4 h-4" />, shortcut: 'Alt+S' },
    ],
  },
];

const AVATAR_STORAGE_KEY = 'msSelectedAvatar';

const avatarOptions = [
  { id: 'series-lead',   label: 'Series Lead',   tone: 'Karakter',      url: 'https://api.dicebear.com/9.x/lorelei/svg?seed=MudoSeriesLead&backgroundColor=0f172a,7f1d1d&radius=50' },
  { id: 'ops-captain',   label: 'Ops Captain',   tone: 'Takım kaptanı', url: 'https://api.dicebear.com/9.x/notionists/svg?seed=MudoOpsCaptain&backgroundColor=111827,581c87&radius=50' },
  { id: 'quest-runner',  label: 'Quest Runner',  tone: 'Macera',        url: 'https://api.dicebear.com/9.x/adventurer/svg?seed=MudoQuestRunner&backgroundColor=164e63,312e81&radius=50' },
  { id: 'street-agent',  label: 'Street Agent',  tone: 'Animasyon',     url: 'https://api.dicebear.com/9.x/avataaars/svg?seed=MudoStreetAgent&backgroundColor=0f172a,14532d&radius=50' },
  { id: 'mecha-pilot',   label: 'Mecha Pilot',   tone: 'Robot',         url: 'https://api.dicebear.com/9.x/bottts-neutral/svg?seed=MudoMechaPilot&backgroundColor=1e293b,0f766e&radius=50' },
  { id: 'field-chief',   label: 'Field Chief',   tone: 'Operasyon',     url: 'https://api.dicebear.com/9.x/personas/svg?seed=MudoFieldChief&backgroundColor=0f172a,155e75&radius=50' },
  { id: 'night-admin',   label: 'Night Admin',   tone: 'Admin',         url: 'https://api.dicebear.com/9.x/lorelei/svg?seed=MudoNightAdmin&backgroundColor=111827,1e3a8a&radius=50' },
  { id: 'support-pro',   label: 'Support Pro',   tone: 'Destek',        url: 'https://api.dicebear.com/9.x/adventurer/svg?seed=MudoSupportPro&backgroundColor=0f172a,166534&radius=50' },
  { id: 'system-scout',  label: 'System Scout',  tone: 'İzleme',        url: 'https://api.dicebear.com/9.x/notionists/svg?seed=MudoSystemScout&backgroundColor=0f172a,0f766e&radius=50' },
  { id: 'tech-lead',     label: 'Tech Lead',     tone: 'Teknik',        url: 'https://api.dicebear.com/9.x/avataaars/svg?seed=MudoTechLead&backgroundColor=111827,0369a1&radius=50' },
  { id: 'route-master',  label: 'Route Master',  tone: 'Saha',          url: 'https://api.dicebear.com/9.x/personas/svg?seed=MudoRouteMaster&backgroundColor=0f172a,365314&radius=50' },
  { id: 'signal-guard',  label: 'Signal Guard',  tone: 'Güvenlik',      url: 'https://api.dicebear.com/9.x/bottts-neutral/svg?seed=MudoSignalGuard&backgroundColor=111827,7f1d1d&radius=50' },
  { id: 'calm-operator', label: 'Calm Operator', tone: 'Operatör',      url: 'https://api.dicebear.com/9.x/lorelei/svg?seed=MudoCalmOperator&backgroundColor=0f172a,334155&radius=50' },
];

const Sidebar: React.FC = () => {
  const { isAdmin, fullName, role, username } = useAuth();
  const { hiddenMenus } = useMenuVisibility();
  const navigate = useNavigate();
  const location = useLocation();
  const [query, setQuery] = useState('');
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

  const selectedAvatar = avatarOptions.find(a => a.id === selectedAvatarId) ?? avatarOptions[0];
  const normalizedQuery = query.trim().toLocaleLowerCase('tr-TR');

  const visibleGroups = useMemo(() => {
    return navGroups
      .map((group) => {
        const roleItems = isAdmin ? group.items : group.items.filter((item) => !hiddenMenus.includes(item.to));
        const items = normalizedQuery
          ? roleItems.filter((item) => {
              const haystack = `${item.label} ${group.title} ${item.shortcut ?? ''}`.toLocaleLowerCase('tr-TR');
              return haystack.includes(normalizedQuery);
            })
          : roleItems;
        return { ...group, items };
      })
      .filter((group) => group.items.length > 0);
  }, [hiddenMenus, isAdmin, normalizedQuery]);

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

  return (
    <aside
      className="flex h-full w-64 shrink-0 flex-col z-40
                 bg-gradient-to-b from-white to-slate-50
                 dark:from-[#081324] dark:to-[#07101f]
                 border-r border-ms-border"
    >
      <div className="px-4 pb-3 pt-4 border-b border-ms-border">
        <button type="button" onClick={() => navigate('/')} className="block text-left">
          <div className="text-[11px] font-bold uppercase tracking-[0.26em] text-sky-600 dark:text-sky-400">Mudosoft</div>
          <div className="mt-1 flex items-center gap-2 text-[12px] font-medium text-ms-text-muted">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-500 shadow-[0_0_10px_rgba(52,211,153,0.7)]" />
            <span>Operations Console</span>
          </div>
        </button>

        <div className="relative mt-4">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-ms-text-muted" />
          <input
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Ara: cihaz, mağaza, işlem..."
            className="h-9 w-full rounded-lg border border-ms-border bg-ms-bg-soft py-0 pl-9 pr-8 text-[12px] text-ms-text placeholder:text-ms-text-muted focus:ring-1 focus:ring-sky-500/40 focus:outline-none"
          />
          {query && (
            <button
              type="button"
              onClick={() => setQuery('')}
              className="absolute right-2 top-1/2 flex h-5 w-5 -translate-y-1/2 items-center justify-center rounded text-ms-text-muted hover:bg-black/5 hover:text-ms-text dark:hover:bg-white/[0.06]"
              title="Aramayı temizle"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>
      </div>

      <nav className="flex-1 overflow-y-auto px-3 py-3">
        {visibleGroups.length === 0 ? (
          <div className="rounded-lg border border-dashed border-ms-border px-3 py-6 text-center text-[12px] text-ms-text-muted">
            Sonuç bulunamadı.
          </div>
        ) : (
          <div className="space-y-4">
            {visibleGroups.map((group) => (
              <section key={group.title}>
                <div className="mb-1.5 flex items-center gap-2 px-2 text-[10px] font-bold uppercase tracking-[0.18em] text-ms-text-muted">
                  <span className="opacity-70">{group.icon}</span>
                  <span>{group.title}</span>
                </div>
                <div className="space-y-0.5">
                  {group.items.map((item) => {
                    const active = isItemActive(item);
                    const baseCls = 'group relative flex h-9 items-center gap-2 rounded-lg px-3 text-[13px] font-medium transition-colors';
                    const stateCls = active
                      ? 'bg-sky-500/[0.14] text-sky-700 dark:text-white'
                      : 'text-ms-text hover:bg-black/[0.04] dark:hover:bg-white/[0.05]';
                    return (
                      <NavLink key={item.to} to={item.to} end={item.exact} className={`${baseCls} ${stateCls}`}>
                        <>
                          {active && <span className="absolute left-0 top-1.5 bottom-1.5 w-[3px] rounded-r bg-sky-500 dark:bg-sky-400" />}
                          <span className={active ? 'text-sky-600 dark:text-sky-300' : 'text-ms-text-muted group-hover:text-ms-text'}>
                            {item.icon}
                          </span>
                          <span className="min-w-0 flex-1 truncate">{item.label}</span>
                          {item.shortcut && (
                            <kbd className="rounded border border-ms-border bg-ms-bg px-1.5 py-0.5 font-mono text-[9px] text-ms-text-muted opacity-0 transition-opacity group-hover:opacity-100">
                              {item.shortcut}
                            </kbd>
                          )}
                        </>
                      </NavLink>
                    );
                  })}
                </div>
              </section>
            ))}
          </div>
        )}
      </nav>

      <div className="relative p-3 border-t border-ms-border">
        {avatarOpen && (
          <div className="absolute left-3 right-3 bottom-[66px] rounded-lg border border-ms-border bg-ms-bg-soft p-3 shadow-2xl">
            <div className="mb-2 flex items-center justify-between gap-2">
              <div>
                <div className="text-[11px] font-semibold text-ms-text">Avatar seç</div>
                <div className="text-[10px] text-ms-text-muted">Tek avatar, net kimlik.</div>
              </div>
              <button
                type="button"
                onClick={() => setAvatarOpen(false)}
                className="rounded px-1.5 py-0.5 text-[11px] text-ms-text-muted hover:bg-black/5 hover:text-ms-text dark:hover:bg-white/[0.06]"
              >
                Kapat
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
                    className={`rounded-lg border p-1.5 transition-colors ${
                      selected
                        ? 'border-sky-400 bg-sky-500/10'
                        : 'border-ms-border bg-ms-bg hover:border-sky-400/50'
                    }`}
                    title={`${avatar.label} - ${avatar.tone}`}
                  >
                    <img src={avatar.url} alt={avatar.label} className="mx-auto h-11 w-11 rounded-full object-cover" />
                    <span className="mt-1 block truncate text-center text-[9px] font-medium text-ms-text">
                      {avatar.label}
                    </span>
                  </button>
                );
              })}
            </div>
          </div>
        )}

        <div className="flex items-center gap-2 rounded-lg border border-ms-border bg-ms-bg-soft p-2">
          <button
            type="button"
            onClick={() => setAvatarOpen((open) => !open)}
            className="h-10 w-10 shrink-0 overflow-hidden rounded-full border border-ms-border bg-ms-bg transition-transform hover:scale-105"
            title="Avatar değiştir"
          >
            <img src={selectedAvatar.url} alt={selectedAvatar.label} className="h-full w-full object-cover" />
          </button>
          <div className="min-w-0 flex-1">
            <div className="truncate text-[12px] font-semibold text-ms-text">
              {fullName || username || 'Kullanıcı'}
            </div>
            <div className="truncate text-[10px] font-medium text-sky-600 dark:text-sky-400/80">
              {role === 'Admin' ? 'Admin' : 'Teknisyen'}
            </div>
          </div>
          <button
            type="button"
            onClick={closeSession}
            className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-ms-text-muted transition-colors hover:bg-red-500/[0.12] hover:text-red-600 dark:hover:text-red-300"
            title="Çıkış Yap"
          >
            <LogOut className="h-4 w-4" />
          </button>
        </div>
      </div>
    </aside>
  );
};

export default Sidebar;
