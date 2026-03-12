import React from 'react';
import { useLocation } from 'react-router-dom';
import { Bell } from 'lucide-react';

const PAGE_TITLES: Record<string, string> = {
  '/':               'Kontrol Paneli',
  '/devices':        'Cihazlar',
  '/kasa':           'Kasalar',
  '/store-managers': 'Mağaza Müdürleri',
  '/sql-query':      'SQL Sorgu',
  '/actions':        'İşlem Geçmişi',
  '/notes':          'Notlar',
  '/inbox-cleanup':  'PLU Cache Temizlik',
  '/stock-cleanup':  'PLU SQL Temizlik',
  '/db-log-cleanup': 'Veritabanı Temizliği',
  '/disk-status':    'Disk Durumu',
  '/agent-update':   'Agent Güncelleme',
  '/settings':       'Ayarlar',
};

const Topbar: React.FC = () => {
  const location = useLocation();

  const title =
    PAGE_TITLES[location.pathname] ||
    (location.pathname.startsWith('/devices/') ? 'Cihaz Detayı' : 'MudoSoft RMM');

  return (
    <header className="h-[52px] border-b border-ms-border bg-ms-bg-soft flex items-center justify-between px-6 sticky top-0 z-50 shrink-0">
      <h1 className="text-sm font-semibold text-ms-text">{title}</h1>

      <div className="flex items-center gap-2">
        <button className="relative h-8 w-8 flex items-center justify-center rounded-lg text-zinc-400 hover:text-ms-text hover:bg-zinc-800 transition-colors">
          <Bell className="w-4 h-4" />
          <span className="absolute top-1.5 right-1.5 w-1.5 h-1.5 bg-violet-500 rounded-full" />
        </button>

        <div className="h-5 w-px bg-ms-border mx-1" />

        <div className="flex items-center gap-2 px-2 py-1 rounded-lg hover:bg-zinc-800 transition-colors cursor-default">
          <div className="h-7 w-7 rounded-full bg-violet-700 flex items-center justify-center text-white font-semibold text-xs shrink-0">
            AD
          </div>
          <div className="hidden sm:block">
            <div className="text-ms-text text-xs font-medium leading-tight">Administrator</div>
            <div className="text-[10px] text-violet-400 font-medium">Mudo IT</div>
          </div>
        </div>
      </div>
    </header>
  );
};

export default Topbar;
