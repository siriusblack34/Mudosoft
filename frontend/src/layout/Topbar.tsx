import React, { useEffect, useState, useCallback } from 'react';
import { useLocation } from 'react-router-dom';
import { Bell, Sun, Moon, Wifi, WifiOff } from 'lucide-react';
import { useTheme } from '../contexts/ThemeContext';
import { useAuth } from '../contexts/AuthContext';
import { apiClient } from '../lib/apiClient';

const PAGE_TITLES: Record<string, string> = {
  '/':               'Kontrol Paneli',
  '/devices':        'Cihazlar',
  '/kasa':           'Kasalar',
  '/store-managers': 'Mağaza Müdürleri',
  '/sql-query':      'SQL Sorgu',
  '/actions':        'İşlem Geçmişi',
  '/notes':          'Notlar',
  '/cleanup':        'Temizlik Merkezi',
  '/inbox-cleanup':  'PLU Cache Temizlik',
  '/stock-cleanup':  'PLU SQL Temizlik',
  '/db-log-cleanup': 'Veritabanı Temizliği',
  '/disk-status':    'Disk Durumu',
  '/agent-update':   'Agent Güncelleme',
  '/settings':       'Ayarlar',
  '/printer-licenses':'Yazıcı Lisansları',
  '/offline-history':'Offline Geçmişi',
};

const Topbar: React.FC = () => {
  const location = useLocation();
  const { isDark, toggleTheme } = useTheme();
  const { fullName, role } = useAuth();
  const [backendStatus, setBackendStatus] = useState<'connected' | 'disconnected' | 'checking'>('checking');

  const checkBackend = useCallback(async () => {
    const ok = await apiClient.checkBackendHealth();
    setBackendStatus(ok ? 'connected' : 'disconnected');
  }, []);

  useEffect(() => {
    checkBackend();
    const interval = setInterval(checkBackend, 30000);
    return () => clearInterval(interval);
  }, [checkBackend]);

  const title =
    PAGE_TITLES[location.pathname] ||
    (location.pathname.startsWith('/devices/') ? 'Cihaz Detayı' : 'MudoSoft RMM');

  const initials = (fullName || 'AD').split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2);
  const roleName = role === 'Admin' ? 'Admin' : role === 'Teknisyen' ? 'Teknisyen' : 'Mudo IT';

  return (
    <header className="h-[52px] border-b border-ms-border bg-ms-bg-soft flex items-center justify-between px-6 sticky top-0 z-50 shrink-0">
      <h1 className="text-sm font-semibold text-ms-text">{title}</h1>

      <div className="flex items-center gap-2">
        {/* Backend Status */}
        <button
          onClick={checkBackend}
          className={`h-8 flex items-center gap-1.5 px-2 rounded-lg text-xs font-medium transition-colors ${
            backendStatus === 'connected'
              ? 'text-emerald-400 hover:bg-emerald-500/10'
              : backendStatus === 'disconnected'
              ? 'text-red-400 hover:bg-red-500/10 animate-pulse'
              : 'text-ms-text-muted'
          }`}
          title={backendStatus === 'connected' ? 'Backend bağlantısı aktif' : backendStatus === 'disconnected' ? 'Backend bağlantısı kesildi! Tıklayarak tekrar deneyin.' : 'Bağlantı kontrol ediliyor...'}
        >
          {backendStatus === 'connected' ? (
            <Wifi className="w-3.5 h-3.5" />
          ) : backendStatus === 'disconnected' ? (
            <WifiOff className="w-3.5 h-3.5" />
          ) : (
            <Wifi className="w-3.5 h-3.5 opacity-50" />
          )}
          <span className="hidden sm:inline">
            {backendStatus === 'connected' ? 'Bağlı' : backendStatus === 'disconnected' ? 'Bağlantı Yok' : '...'}
          </span>
        </button>

        <div className="h-5 w-px bg-ms-border" />

        {/* Theme Toggle */}
        <button
          onClick={toggleTheme}
          className="h-8 w-8 flex items-center justify-center rounded-lg text-ms-text-muted hover:text-ms-text hover:bg-ms-border transition-colors"
          title={isDark ? 'Açık tema' : 'Koyu tema'}
        >
          {isDark ? <Sun className="w-4 h-4" /> : <Moon className="w-4 h-4" />}
        </button>

        <button className="relative h-8 w-8 flex items-center justify-center rounded-lg text-ms-text-muted hover:text-ms-text hover:bg-ms-border transition-colors">
          <Bell className="w-4 h-4" />
          <span className="absolute top-1.5 right-1.5 w-1.5 h-1.5 bg-violet-500 rounded-full" />
        </button>

        <div className="h-5 w-px bg-ms-border mx-1" />

        <div className="flex items-center gap-2 px-2 py-1 rounded-lg hover:bg-ms-border transition-colors cursor-default">
          <div className="h-7 w-7 rounded-full bg-violet-700 flex items-center justify-center text-white font-semibold text-xs shrink-0">
            {initials}
          </div>
          <div className="hidden sm:block">
            <div className="text-ms-text text-xs font-medium leading-tight">{fullName || 'Administrator'}</div>
            <div className="text-[10px] text-violet-400 font-medium">{roleName}</div>
          </div>
        </div>
      </div>
    </header>
  );
};

export default Topbar;
