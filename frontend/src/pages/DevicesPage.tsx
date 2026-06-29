import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { useNavigate } from 'react-router-dom';
import { formatDistanceToNow } from 'date-fns';
import { tr } from 'date-fns/locale';
import {
  AlertCircle,
  Bell,
  BellOff,
  CheckCircle,
  ChevronDown,
  ChevronUp,
  Download,
  LayoutGrid,
  List,
  Monitor,
  PauseCircle,
  PlayCircle,
  Power,
  RefreshCw,
  Search,
  ShoppingCart,
  Trash2,
  Wifi,
  WifiOff,
  Eye,
  EyeOff,
} from 'lucide-react';
import { apiClient, API_BASE_URL } from '../lib/apiClient';
import type {
  DeviceOfflineExclusionResponse,
  DeviceTemporaryCloseResponse,
  StartOfflineServiceResult,
  StartOfflineServicesResponse,
} from '../lib/apiClient';
import type { Device } from '../types';
import DeviceTabs from '../components/DeviceTabs';

type SortKey = 'hostname' | 'storeCode' | 'cpuUsage' | 'ramUsage' | 'diskUsage' | 'online' | 'lastSeen';
type SortDir = 'asc' | 'desc';
type FilterKey = 'all' | 'online' | 'offline' | 'closed';
type TypeFilterKey = 'all' | 'pc' | 'kasa' | 'merkez';
type ContextMenuState = { device: Device; x: number; y: number } | null;

const DevicesPage: React.FC = () => {
  const navigate = useNavigate();
  const { isAdmin } = useAuth();
  const [devices, setDevices] = useState<Device[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<FilterKey>('all');
  const [typeFilter, setTypeFilter] = useState<TypeFilterKey>('all');
  const [searchTerm, setSearchTerm] = useState('');
  const [view, setView] = useState<'list' | 'grid'>(() =>
    (localStorage.getItem('devicesView') as 'list' | 'grid') || 'list'
  );
  const [sortKey, setSortKey] = useState<SortKey>('storeCode');
  const [sortDir, setSortDir] = useState<SortDir>('asc');
  const [startingOfflineServices, setStartingOfflineServices] = useState(false);
  const [busyDeviceId, setBusyDeviceId] = useState<string | null>(null);
  const [contextMenu, setContextMenu] = useState<ContextMenuState>(null);
  const [actionMessage, setActionMessage] = useState<{ text: string; type: 'success' | 'error' } | null>(null);
  const [offlineJob, setOfflineJob] = useState<StartOfflineServicesResponse | null>(null);
  const activeJobIdRef = useRef<string | null>(null);

  const loadDevices = async (silent = false) => {
    try {
      if (!silent) {
        setLoading(true);
      }

      setDevices(await apiClient.getDevices());
    } catch (err) {
      console.error(err);
      setActionMessage({
        text: err instanceof Error ? err.message : 'Cihazlar yuklenemedi.',
        type: 'error',
      });
    } finally {
      if (!silent) {
        setLoading(false);
      }
    }
  };

  useEffect(() => {
    void loadDevices();
    const interval = window.setInterval(() => {
      void loadDevices(true);
    }, 10000);

    return () => window.clearInterval(interval);
  }, []);

  useEffect(() => {
    if (!contextMenu) {
      return;
    }

    const closeMenu = () => setContextMenu(null);
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setContextMenu(null);
      }
    };

    window.addEventListener('click', closeMenu);
    window.addEventListener('scroll', closeMenu, true);
    window.addEventListener('keydown', onKeyDown);

    return () => {
      window.removeEventListener('click', closeMenu);
      window.removeEventListener('scroll', closeMenu, true);
      window.removeEventListener('keydown', onKeyDown);
    };
  }, [contextMenu]);

  const toggleView = (nextView: 'list' | 'grid') => {
    setView(nextView);
    localStorage.setItem('devicesView', nextView);
  };

  const toggleSort = (key: SortKey) => {
    if (sortKey === key) {
      setSortDir((current) => (current === 'asc' ? 'desc' : 'asc'));
      return;
    }

    setSortKey(key);
    setSortDir('asc');
  };

  const isKasa    = (device: Device) => device.hostname?.startsWith('KSTR');
  const isMerkez  = (device: Device) => device.type === 'CentralOffice';
  const isTemporarilyClosed = (device: Device) => Boolean(device.isTemporarilyClosed);
  const isOfflineExcluded = (device: Device) => !device.online && Boolean(device.excludeFromOfflineList);
  const isCountedOffline = (device: Device) => !device.online && !device.excludeFromOfflineList && !device.isTemporarilyClosed;

  const onlineCount = devices.filter((device) => device.online && !device.isTemporarilyClosed).length;
  const offlineCount = devices.filter(isCountedOffline).length;
  const excludedOfflineCount = devices.filter(isOfflineExcluded).length;
  const temporaryClosedCount = devices.filter(isTemporarilyClosed).length;
  const pcCount     = devices.filter((device) => !isKasa(device) && !isMerkez(device)).length;
  const kasaCount   = devices.filter((device) => isKasa(device)).length;
  const merkezCount = devices.filter((device) => isMerkez(device)).length;

  const filtered = useMemo(() => {
    return [...devices]
      .filter((device) => {
        if (filter === 'online' && (!device.online || device.isTemporarilyClosed)) {
          return false;
        }

        if (filter === 'offline' && !isCountedOffline(device)) {
          return false;
        }

        if (filter === 'closed' && !device.isTemporarilyClosed) {
          return false;
        }

        if (typeFilter === 'pc' && (isKasa(device) || isMerkez(device))) {
          return false;
        }

        if (typeFilter === 'kasa' && !isKasa(device)) {
          return false;
        }

        if (typeFilter === 'merkez' && !isMerkez(device)) {
          return false;
        }

        if (!searchTerm) {
          return true;
        }

        const query = searchTerm.toLowerCase();
        return (
          device.hostname?.toLowerCase().includes(query) ||
          device.ipAddress?.toLowerCase().includes(query) ||
          String(device.storeCode || '').includes(query) ||
          (device.storeName || '').toLowerCase().includes(query) ||
          (device.temporaryCloseReason || '').toLowerCase().includes(query)
        );
      })
      .sort((a, b) => {
        const dir = sortDir === 'asc' ? 1 : -1;

        switch (sortKey) {
          case 'hostname':
            return dir * (a.hostname || '').localeCompare(b.hostname || '');
          case 'storeCode':
            return dir * ((a.storeCode || 0) - (b.storeCode || 0));
          case 'cpuUsage':
            return dir * ((a.cpuUsage ?? 0) - (b.cpuUsage ?? 0));
          case 'ramUsage':
            return dir * ((a.ramUsage ?? 0) - (b.ramUsage ?? 0));
          case 'diskUsage':
            return dir * ((a.diskUsage ?? 0) - (b.diskUsage ?? 0));
          case 'online':
            return dir * (Number(a.online) - Number(b.online));
          case 'lastSeen':
            return dir * ((a.lastSeen ? new Date(a.lastSeen).getTime() : 0) - (b.lastSeen ? new Date(b.lastSeen).getTime() : 0));
          default:
            return 0;
        }
      });
  }, [devices, filter, searchTerm, sortDir, sortKey, typeFilter]);

  const buildStartServiceMessage = (result: StartOfflineServicesResponse) => {
    if (result.totalOffline === 0) {
      return { text: 'Offline agent bulunmadi.', type: 'success' as const };
    }

    const targets = result.results
      .slice(0, 4)
      .map((item) => `${item.hostname} (${item.ipAddress})`)
      .join(', ');
    const more = result.results.length > 4 ? ` ve ${result.results.length - 4} cihaz daha` : '';
    const prefix = result.jobId ? `Kuyruk olustu (${result.jobId})` : 'Kuyruk olustu';
    const summary = result.completedAtUtc
      ? `${result.totalOffline} offline cihaz islendi, ${result.pingReachable} ping verdi, ${result.runningConfirmed} servis dogrulandi.`
      : `${result.totalOffline} offline cihaz hedeflendi${targets ? `: ${targets}${more}` : '.'}`;

    return { text: `${prefix}. ${summary}`, type: 'success' as const };
  };

  const handleStartOfflineServices = async () => {
    setStartingOfflineServices(true);
    setActionMessage(null);
    setOfflineJob(null);
    activeJobIdRef.current = null;

    try {
      const result = await apiClient.startOfflineServices();
      setActionMessage(buildStartServiceMessage(result));
      setOfflineJob(result);
      await loadDevices(true);

      if (!result.jobId) {
        return;
      }

      activeJobIdRef.current = result.jobId;

      const poll = async () => {
        // Yeni bir job baslatildiysa eski poll'u dusur
        if (activeJobIdRef.current !== result.jobId) {
          return;
        }

        try {
          const latest = await apiClient.getOfflineServiceStartStatus(result.jobId!);

          if (activeJobIdRef.current !== result.jobId) {
            return;
          }

          setOfflineJob(latest);
          setActionMessage(buildStartServiceMessage(latest));
          await loadDevices(true);

          if (!latest.completedAtUtc) {
            window.setTimeout(() => {
              void poll();
            }, 4000);
          }
        } catch (pollErr) {
          console.error(pollErr);
          // Tek bir hatada vazgecme — birkac saniye sonra tekrar dene
          if (activeJobIdRef.current === result.jobId) {
            window.setTimeout(() => {
              void poll();
            }, 6000);
          }
        }
      };

      window.setTimeout(() => {
        void poll();
      }, 4000);
    } catch (err) {
      console.error(err);
      setActionMessage({
        text: err instanceof Error ? err.message : 'Servis baslatma komutu gonderilemedi.',
        type: 'error',
      });
    } finally {
      setStartingOfflineServices(false);
    }
  };

  const patchDevice = (deviceId: string, mapper: (device: Device) => Device) => {
    setDevices((current) => current.map((device) => (
      device.id === deviceId ? mapper(device) : device
    )));
  };

  const applyOfflineExclusionResult = (device: Device, result: DeviceOfflineExclusionResponse) => {
    patchDevice(device.id, (current) => ({
      ...current,
      excludeFromOfflineList: result.excludeFromOfflineList,
    }));

    setActionMessage({
      text: result.excludeFromOfflineList
        ? `${device.storeName || device.hostname} offline listesinden haric tutuldu.`
        : `${device.storeName || device.hostname} tekrar offline listesine dahil edildi.`,
      type: 'success',
    });
  };

  const handleToggleOfflineExclusion = async (device: Device) => {
    setBusyDeviceId(device.id);
    setActionMessage(null);

    try {
      const result = await apiClient.setDeviceOfflineExclusion(device.id, !device.excludeFromOfflineList);
      applyOfflineExclusionResult(device, result);
    } catch (err) {
      console.error(err);
      setActionMessage({
        text: err instanceof Error ? err.message : 'Cihaz durumu guncellenemedi.',
        type: 'error',
      });
    } finally {
      setBusyDeviceId(null);
      setContextMenu(null);
    }
  };

  const applyTemporaryCloseResult = (device: Device, result: DeviceTemporaryCloseResponse) => {
    patchDevice(device.id, (current) => ({
      ...current,
      isTemporarilyClosed: result.isTemporarilyClosed,
      temporaryCloseReason: result.temporaryCloseReason ?? null,
    }));

    setActionMessage({
      text: result.isTemporarilyClosed
        ? `${device.storeName || device.hostname} gecici kapali olarak isaretlendi.`
        : `${device.storeName || device.hostname} tekrar aktif izlemeye alindi.`,
      type: 'success',
    });
  };

  const handleToggleTemporaryClose = async (device: Device) => {
    const isClosing = !device.isTemporarilyClosed;
    let reason: string | undefined;

    if (isClosing) {
      const response = window.prompt('Gecici kapali sebebi (opsiyonel):', device.temporaryCloseReason || '');
      if (response === null) {
        setContextMenu(null);
        return;
      }

      reason = response.trim() || undefined;
    }

    setBusyDeviceId(device.id);
    setActionMessage(null);

    try {
      const result = await apiClient.setDeviceTemporaryClose(device.id, isClosing, reason);
      applyTemporaryCloseResult(device, result);
    } catch (err) {
      console.error(err);
      setActionMessage({
        text: err instanceof Error ? err.message : 'Gecici kapalilik durumu guncellenemedi.',
        type: 'error',
      });
    } finally {
      setBusyDeviceId(null);
      setContextMenu(null);
    }
  };

  const handleToggleVisibility = async (device: Device) => {
    setBusyDeviceId(device.id);
    setActionMessage(null);
    try {
      const next = !device.hiddenForNonAdmins;
      const result = await apiClient.setDeviceVisibility(device.id, next);
      patchDevice(device.id, (current) => ({ ...current, hiddenForNonAdmins: result.hiddenForNonAdmins }));
      setActionMessage({
        text: result.hiddenForNonAdmins
          ? `${device.storeName || device.hostname} sadece adminlere görünecek.`
          : `${device.storeName || device.hostname} tüm kullanıcılara görünür.`,
        type: 'success',
      });
    } catch (err) {
      setActionMessage({
        text: err instanceof Error ? err.message : 'Görünürlük güncellenemedi.',
        type: 'error',
      });
    } finally {
      setBusyDeviceId(null);
      setContextMenu(null);
    }
  };

  const handleDeleteDevice = async (device: Device) => {
    if (device.online) {
      setActionMessage({
        text: 'Online cihaz silinemez. Once cihazi kapatin veya agenti kaldirin.',
        type: 'error',
      });
      setContextMenu(null);
      return;
    }

    const confirmed = window.confirm(`"${device.storeName || device.hostname}" cihazini envanterden silmek istediginize emin misiniz?`);
    if (!confirmed) {
      setContextMenu(null);
      return;
    }

    setBusyDeviceId(device.id);
    setActionMessage(null);

    try {
      await apiClient.deleteDevice(device.id);
      setDevices((current) => current.filter((item) => item.id !== device.id));
      setActionMessage({
        text: `${device.storeName || device.hostname} envanterden silindi.`,
        type: 'success',
      });
    } catch (err) {
      console.error(err);
      setActionMessage({
        text: err instanceof Error ? err.message : 'Cihaz silinemedi.',
        type: 'error',
      });
    } finally {
      setBusyDeviceId(null);
      setContextMenu(null);
    }
  };

  const openContextMenu = (event: React.MouseEvent, device: Device) => {
    event.preventDefault();
    event.stopPropagation();
    setContextMenu({ device, x: event.clientX, y: event.clientY });
  };

  const getStatusMeta = (device: Device) => {
    if (device.isTemporarilyClosed) {
      return {
        border: 'border-l-amber-500/60',
        dot: 'bg-amber-400 shadow-[0_0_10px_rgba(251,191,36,0.45)]',
        text: 'text-amber-300/90',
        label: 'Kapali',
      };
    }

    if (device.online) {
      return {
        border: 'border-l-emerald-500/60',
        dot: 'bg-emerald-400 shadow-[0_0_8px_rgba(52,211,153,0.6)]',
        text: 'text-slate-500',
        label: 'Online',
      };
    }

    if (device.excludeFromOfflineList) {
      return {
        border: 'border-l-sky-500/30',
        dot: 'bg-slate-500',
        text: 'text-sky-300/80',
        label: 'Haric',
      };
    }

    return {
      border: 'border-l-rose-500/40',
      dot: 'bg-rose-500',
      text: 'text-rose-400/75',
      label: 'Offline',
    };
  };

  const SortIcon = ({ col }: { col: SortKey }) => {
    if (sortKey !== col) return null;
    return sortDir === 'asc' ? <ChevronUp className="w-3.5 h-3.5 inline" /> : <ChevronDown className="w-3.5 h-3.5 inline" />;
  };

  const osBadge = (os?: string) => {
    if (!os) return null;
    if (os.includes('11')) return <span className="px-2 py-0.5 rounded-md text-[11px] font-medium bg-blue-500/10 text-blue-400 border border-blue-500/20">Win 11</span>;
    if (os.includes('10')) return <span className="px-2 py-0.5 rounded-md text-[11px] font-medium bg-cyan-500/10 text-cyan-400 border border-cyan-500/20">Win 10</span>;
    if (os.includes('7')) return <span className="px-2 py-0.5 rounded-md text-[11px] font-medium bg-amber-500/10 text-amber-400 border border-amber-500/20">Win 7</span>;
    return <span className="px-2 py-0.5 rounded-md text-[11px] font-medium bg-slate-500/10 text-slate-400 border border-slate-500/20">Windows</span>;
  };

  const uptimeText = (device: Device) => {
    if (!device.lastSeen) return '-';
    if (device.isTemporarilyClosed && device.temporaryCloseReason) return device.temporaryCloseReason;
    return formatDistanceToNow(new Date(device.lastSeen), { addSuffix: true, locale: tr });
  };

  const contextDevice = contextMenu?.device;
  const menuLeft = contextMenu ? Math.min(contextMenu.x, window.innerWidth - 220) : 0;
  const menuTop = contextMenu ? Math.min(contextMenu.y, window.innerHeight - 220) : 0;

  return (
    <div className="space-y-6 p-1">
      <DeviceTabs />
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-5">
          <h1 className="text-2xl font-bold text-white tracking-tight">Cihazlar</h1>
          <div className="flex items-center gap-2 text-sm">
            <div className="flex items-center gap-1.5 text-emerald-400">
              <Wifi className="w-4 h-4" />
              <span className="font-semibold">{onlineCount}</span>
            </div>
            <span className="text-slate-600">/</span>
            <div className="flex items-center gap-1.5 text-rose-400">
              <WifiOff className="w-4 h-4" />
              <span className="font-semibold">{offlineCount}</span>
            </div>
            {temporaryClosedCount > 0 && (
              <>
                <span className="text-slate-600">/</span>
                <div className="flex items-center gap-1.5 text-amber-400">
                  <PauseCircle className="w-4 h-4" />
                  <span className="font-semibold">{temporaryClosedCount}</span>
                </div>
              </>
            )}
            {excludedOfflineCount > 0 && (
              <>
                <span className="text-slate-600">/</span>
                <div className="flex items-center gap-1.5 text-sky-300">
                  <BellOff className="w-4 h-4" />
                  <span className="font-semibold">{excludedOfflineCount}</span>
                </div>
              </>
            )}
          </div>
        </div>

        <div className="flex items-center gap-2">
          <button
            onClick={handleStartOfflineServices}
            disabled={startingOfflineServices || offlineCount === 0}
            className="inline-flex items-center gap-2 rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-2 text-sm font-medium text-amber-300 transition-colors hover:bg-amber-500/20 disabled:cursor-not-allowed disabled:opacity-50"
            title="Ping alan offline cihazlarda agent servisini baslatmayi dener"
          >
            {startingOfflineServices ? <RefreshCw className="w-4 h-4 animate-spin" /> : <Power className="w-4 h-4" />}
            <span>{startingOfflineServices ? 'Gonderiliyor...' : 'Offline Servisleri Baslat'}</span>
          </button>

          <div className="relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-500" />
            <input
              type="text"
              placeholder="Ara..."
              value={searchTerm}
              onChange={(event) => setSearchTerm(event.target.value)}
              className="w-56 pl-10 pr-4 py-2 bg-slate-800/50 border border-slate-700/50 rounded-xl text-sm text-white placeholder-slate-500 focus:outline-none focus:border-sky-500/40"
            />
          </div>

          <div className="flex rounded-xl overflow-hidden border border-slate-700/50">
            {(['all', 'online', 'offline', 'closed'] as const).map((value) => (
              <button
                key={value}
                onClick={() => setFilter(value)}
                className={`px-3 py-2 text-xs font-medium transition-colors ${filter === value ? 'bg-slate-700/50 text-white' : 'text-slate-500 hover:text-slate-300'}`}
              >
                {value === 'all' ? 'Hepsi' : value === 'online' ? 'Aktif' : value === 'offline' ? 'Offline' : 'Kapali'}
              </button>
            ))}
          </div>

          <div className="flex rounded-xl overflow-hidden border border-slate-700/50">
            {([
              ['all',    `Tümü (${devices.length})`],
              ['pc',     `PC (${pcCount})`],
              ['kasa',   `Kasa (${kasaCount})`],
              ['merkez', `Merkez (${merkezCount})`],
            ] as [TypeFilterKey, string][]).map(([value, label]) => (
              <button
                key={value}
                onClick={() => setTypeFilter(value)}
                className={`px-3 py-2 text-xs font-medium transition-colors ${
                  typeFilter === value
                    ? value === 'merkez'
                      ? 'bg-violet-700/50 text-violet-200'
                      : 'bg-slate-700/50 text-white'
                    : 'text-slate-500 hover:text-slate-300'
                }`}
              >
                {label}
              </button>
            ))}
          </div>

          {typeFilter === 'merkez' && (
            <a
              href={`${API_BASE_URL}/api/agent/central/download`}
              download="OrchestraCentralAgent.exe"
              className="inline-flex items-center gap-2 rounded-xl border border-violet-500/40 bg-violet-500/10 px-3 py-2 text-xs font-medium text-violet-300 transition-colors hover:bg-violet-500/20"
            >
              <Download className="w-3.5 h-3.5" />
              Merkez Agent İndir
            </a>
          )}

          <div className="flex rounded-xl overflow-hidden border border-slate-700/50">
            <button
              onClick={() => toggleView('list')}
              className={`p-2 transition-colors ${view === 'list' ? 'bg-slate-700/50 text-white' : 'text-slate-500 hover:text-slate-300'}`}
            >
              <List className="w-4 h-4" />
            </button>
            <button
              onClick={() => toggleView('grid')}
              className={`p-2 transition-colors ${view === 'grid' ? 'bg-slate-700/50 text-white' : 'text-slate-500 hover:text-slate-300'}`}
            >
              <LayoutGrid className="w-4 h-4" />
            </button>
          </div>
        </div>
      </div>

      {actionMessage && (
        <div className={`rounded-2xl border px-4 py-3 text-sm flex items-center gap-3 ${
          actionMessage.type === 'success'
            ? 'border-emerald-700/60 bg-emerald-900/20 text-emerald-200'
            : 'border-rose-700/60 bg-rose-900/20 text-rose-200'
        }`}>
          {actionMessage.type === 'success' ? <CheckCircle className="w-4 h-4 shrink-0" /> : <AlertCircle className="w-4 h-4 shrink-0" />}
          <span>{actionMessage.text}</span>
        </div>
      )}

      {offlineJob && (
        <OfflineJobPanel job={offlineJob} onClose={() => setOfflineJob(null)} />
      )}

      {loading ? (
        <div className="flex items-center justify-center py-32">
          <div className="w-8 h-8 border-2 border-sky-500 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : filtered.length === 0 ? (
        <div className="text-center py-32 text-slate-500">
          <Monitor className="w-12 h-12 mx-auto mb-3 opacity-30" />
          <p>Cihaz bulunamadi</p>
        </div>
      ) : view === 'list' ? (
        <div className="rounded-2xl overflow-hidden border border-slate-700/40">
          <table className="w-full table-fixed">
            <colgroup>
              <col className="w-10" />
              <col style={{ width: '26%' }} />
              <col style={{ width: '11%' }} />
              <col style={{ width: '13%' }} />
              <col style={{ width: '13%' }} />
              <col style={{ width: '13%' }} />
              <col style={{ width: '8%' }} />
              <col style={{ width: '13%' }} />
            </colgroup>
            <thead>
              <tr className="bg-slate-800/30 text-xs text-slate-400 uppercase tracking-wider border-b border-slate-700/40">
                <th className="px-3 py-4"></th>
                <th className="text-left px-3 py-4 font-medium cursor-pointer hover:text-white select-none" onClick={() => toggleSort('storeCode')}>
                  Magaza <SortIcon col="storeCode" />
                </th>
                <th className="text-left px-3 py-4 font-medium">IP Adresi</th>
                <th className="text-left px-3 py-4 font-medium cursor-pointer hover:text-white select-none" onClick={() => toggleSort('cpuUsage')}>
                  Islemci <SortIcon col="cpuUsage" />
                </th>
                <th className="text-left px-3 py-4 font-medium cursor-pointer hover:text-white select-none" onClick={() => toggleSort('ramUsage')}>
                  Bellek <SortIcon col="ramUsage" />
                </th>
                <th className="text-left px-3 py-4 font-medium cursor-pointer hover:text-white select-none" onClick={() => toggleSort('diskUsage')}>
                  Disk <SortIcon col="diskUsage" />
                </th>
                <th className="text-left px-3 py-4 font-medium">OS</th>
                <th className="text-right px-3 py-4 font-medium cursor-pointer hover:text-white select-none" onClick={() => toggleSort('lastSeen')}>
                  Son Aktivite <SortIcon col="lastSeen" />
                </th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((device) => {
                const deviceIsKasa = isKasa(device);
                const status = getStatusMeta(device);

                return (
                  <tr
                    key={device.id}
                    onClick={() => navigate(`/devices/${device.id}`)}
                    onContextMenu={(event) => openContextMenu(event, device)}
                    className={`border-l-[3px] border-b border-slate-700/20 hover:bg-slate-800/30 cursor-pointer transition-colors group ${status.border}`}
                  >
                    <td className="px-3 py-3">
                      <div className={`w-2.5 h-2.5 rounded-full mx-auto ${status.dot}`} />
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-3">
                        <div className={`p-2 rounded-lg ${deviceIsKasa ? 'bg-amber-500/10' : 'bg-sky-500/10'}`}>
                          {deviceIsKasa
                            ? <ShoppingCart className="w-4 h-4 text-amber-400" />
                            : <Monitor className="w-4 h-4 text-sky-400" />}
                        </div>
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="text-sm font-semibold text-white group-hover:text-sky-400 transition-colors">
                              {device.storeName || device.hostname}
                            </span>
                            <span className={`text-[10px] px-1.5 py-0.5 rounded font-medium ${deviceIsKasa ? 'bg-amber-500/10 text-amber-400' : 'bg-sky-500/10 text-sky-400'}`}>
                              {deviceIsKasa ? 'Kasa' : 'PC'}
                            </span>
                            {device.hiddenForNonAdmins && isAdmin && (
                              <span className="inline-flex items-center gap-1 text-[10px] px-1.5 py-0.5 rounded font-medium bg-purple-500/10 text-purple-300 border border-purple-500/20" title="Sadece admin görür">
                                <EyeOff className="w-3 h-3" /> Admin
                              </span>
                            )}
                            {device.isTemporarilyClosed && (
                              <span className="text-[10px] px-1.5 py-0.5 rounded font-medium bg-amber-500/10 text-amber-300 border border-amber-500/20">
                                Kapali
                              </span>
                            )}
                            {!device.isTemporarilyClosed && device.excludeFromOfflineList && (
                              <span className="text-[10px] px-1.5 py-0.5 rounded font-medium bg-sky-500/10 text-sky-300 border border-sky-500/20">
                                Haric
                              </span>
                            )}
                          </div>
                          <div className="mt-0.5 flex items-center gap-2">
                            <span className="text-xs text-slate-500">M:{device.storeCode}</span>
                            <span className={`text-xs ${status.text}`}>{status.label}</span>
                            {busyDeviceId === device.id && (
                              <span className="text-xs text-amber-300">Isleniyor...</span>
                            )}
                          </div>
                        </div>
                      </div>
                    </td>
                    <td className="px-3 py-3 text-sm text-slate-400 font-mono">{device.ipAddress}</td>
                    <td className="px-3 py-3">
                      {device.online && !device.isTemporarilyClosed ? <MetricBar value={device.cpuUsage ?? 0} /> : <span className="text-slate-700 text-sm">-</span>}
                    </td>
                    <td className="px-3 py-3">
                      {device.online && !device.isTemporarilyClosed ? <MetricBar value={device.ramUsage ?? 0} /> : <span className="text-slate-700 text-sm">-</span>}
                    </td>
                    <td className="px-3 py-3">
                      {device.online && !device.isTemporarilyClosed ? <MetricBar value={device.diskUsage ?? 0} /> : <span className="text-slate-700 text-sm">-</span>}
                    </td>
                    <td className="px-3 py-3">{osBadge(device.os?.name)}</td>
                    <td className="px-3 py-3 text-right">
                      <span className={`text-xs ${status.text}`}>{uptimeText(device)}</span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
          {filtered.map((device) => {
            const deviceIsKasa = isKasa(device);
            const status = getStatusMeta(device);

            return (
              <div
                key={device.id}
                onClick={() => navigate(`/devices/${device.id}`)}
                onContextMenu={(event) => openContextMenu(event, device)}
                className={`group p-5 rounded-2xl border-l-[3px] border cursor-pointer transition-all duration-200 hover:scale-[1.02] hover:shadow-lg hover:shadow-sky-500/5 ${status.border} ${
                  device.online && !device.isTemporarilyClosed
                    ? 'bg-slate-800/40 border-slate-700/40 hover:border-sky-500/30'
                    : device.isTemporarilyClosed
                      ? 'bg-slate-800/25 border-amber-500/20'
                      : device.excludeFromOfflineList
                        ? 'bg-slate-800/20 border-sky-500/15'
                        : 'bg-slate-800/20 border-slate-700/20'
                }`}
              >
                <div className="flex items-start justify-between mb-4">
                  <div className={`p-2.5 rounded-xl ${deviceIsKasa ? 'bg-amber-500/10' : 'bg-sky-500/10'}`}>
                    {deviceIsKasa
                      ? <ShoppingCart className="w-5 h-5 text-amber-400" />
                      : <Monitor className="w-5 h-5 text-sky-400" />}
                  </div>
                  <div className="flex items-center gap-2">
                    {osBadge(device.os?.name)}
                    <div className={`w-2.5 h-2.5 rounded-full ${status.dot}`} />
                  </div>
                </div>

                <h3 className="text-sm font-bold text-white group-hover:text-sky-400 transition-colors truncate">
                  {device.storeName || device.hostname}
                </h3>
                <p className="text-xs text-slate-500 font-mono mt-1">{device.ipAddress}</p>

                <div className="mt-3 px-3 py-2 rounded-lg bg-slate-700/20 border border-slate-700/30">
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-[11px] text-slate-500">Magaza {device.storeCode}</span>
                    <div className="flex items-center gap-2">
                      <span className={`text-[10px] px-1.5 py-0.5 rounded font-medium ${deviceIsKasa ? 'bg-amber-500/10 text-amber-400' : 'bg-sky-500/10 text-sky-400'}`}>
                        {deviceIsKasa ? 'Kasa' : 'PC'}
                      </span>
                      {device.isTemporarilyClosed && (
                        <span className="text-[10px] px-1.5 py-0.5 rounded font-medium bg-amber-500/10 text-amber-300 border border-amber-500/20">
                          Kapali
                        </span>
                      )}
                      {!device.isTemporarilyClosed && device.excludeFromOfflineList && (
                        <span className="text-[10px] px-1.5 py-0.5 rounded font-medium bg-sky-500/10 text-sky-300 border border-sky-500/20">
                          Haric
                        </span>
                      )}
                    </div>
                  </div>
                  <div className={`mt-2 text-[11px] ${status.text}`}>
                    {busyDeviceId === device.id ? 'Isleniyor...' : status.label}
                  </div>
                </div>

                {device.online && !device.isTemporarilyClosed && (
                  <div className="mt-4 space-y-2">
                    <MetricRow label="Islemci" value={device.cpuUsage ?? 0} />
                    <MetricRow label="Bellek" value={device.ramUsage ?? 0} />
                    <MetricRow label="Disk" value={device.diskUsage ?? 0} />
                  </div>
                )}

                <p className={`text-[11px] mt-3 ${status.text}`}>{uptimeText(device)}</p>
              </div>
            );
          })}
        </div>
      )}

      {contextDevice && (
        <div
          className="fixed z-50 min-w-[210px] rounded-2xl border border-slate-700/70 bg-slate-950/95 p-2 shadow-2xl shadow-black/40 backdrop-blur"
          style={{ left: menuLeft, top: menuTop }}
          onClick={(event) => event.stopPropagation()}
        >
          <div className="border-b border-slate-800 px-3 py-2">
            <div className="truncate text-sm font-semibold text-white">{contextDevice.storeName || contextDevice.hostname}</div>
            <div className="mt-1 truncate text-xs text-slate-500">{contextDevice.hostname} - {contextDevice.ipAddress}</div>
          </div>

          <div className="mt-2 space-y-1">
            <ContextMenuButton
              icon={contextDevice.isTemporarilyClosed ? <PlayCircle className="h-4 w-4" /> : <PauseCircle className="h-4 w-4" />}
              label={contextDevice.isTemporarilyClosed ? 'Aktif Et' : 'Gecici Kapali Yap'}
              busy={busyDeviceId === contextDevice.id}
              onClick={() => { void handleToggleTemporaryClose(contextDevice); }}
            />
            <ContextMenuButton
              icon={contextDevice.excludeFromOfflineList ? <Bell className="h-4 w-4" /> : <BellOff className="h-4 w-4" />}
              label={contextDevice.excludeFromOfflineList ? 'Listeye Al' : 'Offline Listeden Haric Tut'}
              busy={busyDeviceId === contextDevice.id}
              onClick={() => { void handleToggleOfflineExclusion(contextDevice); }}
            />
            {isAdmin && (
              <ContextMenuButton
                icon={contextDevice.hiddenForNonAdmins ? <Eye className="h-4 w-4" /> : <EyeOff className="h-4 w-4" />}
                label={contextDevice.hiddenForNonAdmins ? 'Herkese Göster' : 'Sadece Admin Görsün'}
                busy={busyDeviceId === contextDevice.id}
                onClick={() => { void handleToggleVisibility(contextDevice); }}
              />
            )}
            {isAdmin && !contextDevice.online && (
              <ContextMenuButton
                icon={<Trash2 className="h-4 w-4" />}
                label="Sil"
                busy={busyDeviceId === contextDevice.id}
                danger
                onClick={() => { void handleDeleteDevice(contextDevice); }}
              />
            )}
          </div>
        </div>
      )}
    </div>
  );
};

type StatusTone = 'ok' | 'warn' | 'err' | 'mute';

const STATUS_META: Record<string, { label: string; tone: StatusTone }> = {
  queued: { label: 'Sirada', tone: 'mute' },
  pinging: { label: 'Pingleniyor', tone: 'mute' },
  starting: { label: 'Baslatiliyor', tone: 'mute' },
  running: { label: 'Baslatildi', tone: 'ok' },
  'already-running': { label: 'Zaten calisiyor', tone: 'ok' },
  unreachable: { label: 'Ping yok', tone: 'err' },
  'access-denied': { label: 'Yetki yok', tone: 'warn' },
  'rpc-unavailable': { label: 'RPC kapali', tone: 'warn' },
  'host-unreachable': { label: 'Ag yolu yok', tone: 'warn' },
  'service-missing': { label: 'Agent yok', tone: 'warn' },
  'auth-failed': { label: 'Kimlik hatasi', tone: 'warn' },
  'rpc-busy': { label: 'RPC mesgul', tone: 'warn' },
  'start-failed': { label: 'Baslatma basarisiz', tone: 'err' },
  failed: { label: 'Hata', tone: 'err' },
  error: { label: 'Istisna', tone: 'err' },
  unknown: { label: 'Bilinmiyor', tone: 'mute' },
};

const TONE_CLASSES: Record<StatusTone, string> = {
  ok: 'bg-emerald-500/10 text-emerald-300 border-emerald-500/30',
  warn: 'bg-amber-500/10 text-amber-300 border-amber-500/30',
  err: 'bg-rose-500/10 text-rose-300 border-rose-500/30',
  mute: 'bg-slate-700/30 text-slate-300 border-slate-700/40',
};

const statusMeta = (status: string) =>
  STATUS_META[status] ?? { label: status || '-', tone: 'mute' as StatusTone };

const StatusBadge: React.FC<{ status: string }> = ({ status }) => {
  const meta = statusMeta(status);
  return (
    <span className={`inline-block whitespace-nowrap rounded-md border px-2 py-0.5 text-[11px] font-medium ${TONE_CLASSES[meta.tone]}`}>
      {meta.label}
    </span>
  );
};

const OfflineJobPanel: React.FC<{
  job: StartOfflineServicesResponse;
  onClose: () => void;
}> = ({ job, onClose }) => {
  const completed = !!job.completedAtUtc;
  const groups = job.results.reduce<Record<string, number>>((acc, r) => {
    acc[r.status] = (acc[r.status] ?? 0) + 1;
    return acc;
  }, {});
  const groupEntries = Object.entries(groups).sort(([, a], [, b]) => b - a);

  return (
    <div className="rounded-2xl border border-slate-700/60 bg-slate-900/40 px-4 py-3 text-sm">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          {!completed && <RefreshCw className="h-4 w-4 animate-spin text-amber-300" />}
          <span className="font-medium text-slate-200">
            Offline servis baslatma {completed ? 'tamamlandi' : 'devam ediyor'}
          </span>
          {job.jobId && <span className="text-xs text-slate-500">#{job.jobId}</span>}
        </div>
        <button
          type="button"
          onClick={onClose}
          className="text-xs text-slate-400 hover:text-slate-200"
        >
          Kapat
        </button>
      </div>

      <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-5">
        <Stat label="Hedef" value={job.totalOffline} />
        <Stat label="Islenen" value={job.attempted} total={job.totalOffline} />
        <Stat label="Pinglendi" value={job.pingReachable} />
        <Stat label="Komut gonderildi" value={job.startIssued} />
        <Stat label="Calistigi dogrulandi" value={job.runningConfirmed} tone="ok" />
      </div>

      {groupEntries.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-1.5">
          {groupEntries.map(([status, count]) => (
            <span
              key={status}
              className={`inline-flex items-center gap-1 rounded-md border px-2 py-0.5 text-[11px] ${TONE_CLASSES[statusMeta(status).tone]}`}
            >
              <span>{statusMeta(status).label}</span>
              <span className="font-semibold">{count}</span>
            </span>
          ))}
        </div>
      )}

      {job.results.length > 0 && (
        <details className="mt-3" open={completed}>
          <summary className="cursor-pointer select-none text-xs text-slate-300 hover:text-white">
            Cihaz detaylari ({job.results.length})
          </summary>
          <div className="mt-2 max-h-80 overflow-y-auto rounded-lg border border-slate-800">
            <table className="w-full text-xs">
              <thead className="bg-slate-800/80 text-slate-300 sticky top-0">
                <tr>
                  <th className="px-2 py-1.5 text-left font-medium">Magaza</th>
                  <th className="px-2 py-1.5 text-left font-medium">Hostname</th>
                  <th className="px-2 py-1.5 text-left font-medium">IP</th>
                  <th className="px-2 py-1.5 text-left font-medium">Durum</th>
                  <th className="px-2 py-1.5 text-left font-medium">Aciklama</th>
                </tr>
              </thead>
              <tbody>
                {job.results.map((r: StartOfflineServiceResult) => (
                  <tr key={r.deviceId} className="border-t border-slate-800/60">
                    <td className="px-2 py-1.5 text-slate-400">{r.storeCode || '-'}</td>
                    <td className="px-2 py-1.5 text-slate-200">{r.hostname}</td>
                    <td className="px-2 py-1.5 font-mono text-slate-400">{r.ipAddress}</td>
                    <td className="px-2 py-1.5"><StatusBadge status={r.status} /></td>
                    <td className="px-2 py-1.5 text-slate-400" title={r.message}>
                      {r.message}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      )}
    </div>
  );
};

const Stat: React.FC<{ label: string; value: number; total?: number; tone?: StatusTone }> = ({
  label,
  value,
  total,
  tone = 'mute',
}) => (
  <div className={`rounded-lg border px-2 py-1.5 ${TONE_CLASSES[tone]}`}>
    <div className="text-[10px] uppercase tracking-wide opacity-70">{label}</div>
    <div className="text-base font-semibold">
      {value}
      {total !== undefined && <span className="ml-1 text-xs opacity-60">/ {total}</span>}
    </div>
  </div>
);

const ContextMenuButton = ({
  icon,
  label,
  onClick,
  busy,
  danger = false,
}: {
  icon: React.ReactNode;
  label: string;
  onClick: () => void;
  busy?: boolean;
  danger?: boolean;
}) => (
  <button
    type="button"
    onClick={onClick}
    disabled={busy}
    className={`flex w-full items-center gap-3 rounded-xl px-3 py-2 text-left text-sm transition-colors disabled:cursor-not-allowed disabled:opacity-60 ${
      danger ? 'text-rose-300 hover:bg-rose-500/10' : 'text-slate-200 hover:bg-slate-800'
    }`}
  >
    <span className={danger ? 'text-rose-400' : 'text-slate-400'}>{icon}</span>
    <span>{busy ? 'Isleniyor...' : label}</span>
  </button>
);

const MetricBar: React.FC<{ value: number }> = ({ value }) => {
  const color = value > 90 ? 'bg-rose-500' : value > 70 ? 'bg-amber-500' : 'bg-sky-500';
  return (
    <div className="flex items-center gap-2">
      <div className="flex-1 h-2 bg-slate-700/40 rounded-full overflow-hidden">
        <div className={`h-full rounded-full transition-all ${color}`} style={{ width: `${Math.min(value, 100)}%` }} />
      </div>
      <span className={`text-xs font-mono w-8 text-right ${value > 90 ? 'text-rose-400' : value > 70 ? 'text-amber-400' : 'text-slate-400'}`}>
        {value}%
      </span>
    </div>
  );
};

const MetricRow: React.FC<{ label: string; value: number }> = ({ label, value }) => (
  <div>
    <div className="flex justify-between text-[11px] mb-1">
      <span className="text-slate-500">{label}</span>
      <span className={value > 80 ? 'text-rose-400' : 'text-slate-400'}>{value}%</span>
    </div>
    <MetricBar value={value} />
  </div>
);

export default DevicesPage;
