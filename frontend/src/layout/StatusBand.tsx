import React, { useEffect, useState, useCallback, useRef } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { apiClient } from '../lib/apiClient';
import { Device } from '../types';
import { Sun, Moon, RefreshCw } from 'lucide-react';
import { useTheme } from '../contexts/ThemeContext';
import Logo from '../components/Logo';

// ─── System phrase logic ─────────────────────────────────────────────────────

type SystemState = 'nominal' | 'degraded' | 'critical' | 'loading';

function deriveSystemState(devices: Device[]): { state: SystemState; phrase: string } {
  if (devices.length === 0) return { state: 'loading', phrase: 'Taranıyor...' };

  const activeDevices = devices.filter(d => !(d.isTemporarilyClosed || (d.excludeFromOfflineList && !d.online)));
  const total = activeDevices.length;
  if (total === 0) return { state: 'loading', phrase: 'Taranıyor...' };
  const online = activeDevices.filter(d => d.online).length;
  const ratio = online / total;

  if (ratio >= 0.95) return { state: 'nominal', phrase: 'Operational' };
  if (ratio >= 0.80) return { state: 'degraded', phrase: 'Dikkat gerekli' };
  if (ratio >= 0.50) return { state: 'critical', phrase: 'Müdahale gerekli' };
  return { state: 'critical', phrase: 'Kritik durum' };
}

const stateColors: Record<SystemState, string> = {
  nominal:  '#22c55e',
  degraded: '#eab308',
  critical: '#ef4444',
  loading:  '#475569',
};

// ─── Component ───────────────────────────────────────────────────────────────

interface StatusBandProps {
  devices: Device[];
}

interface DashboardTopbarState {
  enabled: boolean;
  message?: string;
  agentTime?: string;
  sqlTime?: string;
  agentState?: 'ok' | 'error' | 'cache' | 'loading';
  sqlState?: 'ok' | 'error' | 'cache' | 'loading';
  isRefreshing?: boolean;
}

const StatusBand: React.FC<StatusBandProps> = ({ devices }) => {
  const navigate = useNavigate();
  const location = useLocation();
  // auth artık sidebar'da
  const { isDark, toggleTheme } = useTheme();
  const [backendOk, setBackendOk] = useState(true);
  const [time, setTime] = useState(() => new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }));
  const [dashboardTopbar, setDashboardTopbar] = useState<DashboardTopbarState>({ enabled: false });
  const [refreshAnim, setRefreshAnim] = useState(false);

  // Clock
  useEffect(() => {
    const t = setInterval(() => {
      setTime(new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }));
    }, 30_000);
    return () => clearInterval(t);
  }, []);

  // Backend health check — tek bir aşımı ciddiye almıyoruz; en az 2 ardışık fail sonrası kırmızı
  const failureCountRef = useRef(0);
  const checkBackend = useCallback(async () => {
    const ok = await apiClient.checkBackendHealth();
    if (ok) {
      failureCountRef.current = 0;
      setBackendOk(true);
    } else {
      failureCountRef.current += 1;
      if (failureCountRef.current >= 2) setBackendOk(false);
    }
  }, []);

  useEffect(() => {
    checkBackend();
    const i = setInterval(checkBackend, 15_000);
    return () => clearInterval(i);
  }, [checkBackend]);

  useEffect(() => {
    const handleDashboardStatus = (event: Event) => {
      const customEvent = event as CustomEvent<DashboardTopbarState>;
      setDashboardTopbar(customEvent.detail || { enabled: false });
    };
    window.addEventListener('ms-dashboard-status', handleDashboardStatus as EventListener);
    return () => window.removeEventListener('ms-dashboard-status', handleDashboardStatus as EventListener);
  }, []);

  const isKasa = (device: Device) => device.hostname?.startsWith('KSTR');
  const pcs = devices.filter(d => !isKasa(d));
  const kasas = devices.filter(d => isKasa(d));
  const pcOnline = pcs.filter(d => d.online && !d.isTemporarilyClosed).length;
  const kasaOnline = kasas.filter(d => d.online && !d.isTemporarilyClosed).length;
  const offline = devices.filter(d => !d.online && !d.excludeFromOfflineList && !d.isTemporarilyClosed).length;

  const { state, phrase } = deriveSystemState(devices);
  const dotColor = stateColors[state];


  // Counter chip
  const Chip: React.FC<{ label: string; count: number; onClick?: () => void; muted?: boolean }> = ({ label, count, onClick, muted }) => (
    <button
      onClick={onClick}
      className="flex items-center gap-1.5 px-2 py-0.5 rounded hover:bg-white/[0.04] transition-colors text-[12px]"
    >
      <span className="font-mono font-semibold" style={{ color: muted ? '#64748b' : '#e2e8f0' }}>{count}</span>
      <span style={{ color: '#64748b' }}>{label}</span>
    </button>
  );

  const TopbarPill: React.FC<{ label: string; value: string; status?: DashboardTopbarState['agentState'] }> = ({ label, value, status }) => {
    const dotColor = status === 'ok' ? '#34d399' : status === 'error' ? '#fb7185' : status === 'cache' ? '#fbbf24' : '#38bdf8';
    return (
      <div className="flex items-center gap-2 rounded-xl border px-3 py-1.5" style={{ background: 'rgba(15,23,42,0.45)', borderColor: 'var(--ms-border)' }}>
        {status && <span className="h-1.5 w-1.5 rounded-full" style={{ background: dotColor }} />}
        <div>
          <div className="text-[9px] font-bold uppercase tracking-[0.18em]" style={{ color: '#64748b' }}>{label}</div>
          <div className="text-xs font-semibold text-slate-200">{value}</div>
        </div>
      </div>
    );
  };

  return (
    <header
      className="h-12 flex items-center gap-4 px-4 shrink-0 z-50 border-b transition-colors duration-500"
      style={{
        background: state === 'critical' ? 'rgba(239,68,68,0.03)' : 'var(--cc-band)',
        borderColor: 'var(--ms-border)',
      }}
    >
      {/* Left: brand + system phrase */}
      <div className="flex items-center gap-4 shrink-0">
        {/* Brand */}
        <button
          onClick={() => navigate('/')}
          className="flex items-center gap-2 hover:opacity-80 transition-opacity"
        >
          <Logo size={40} idSuffix="statusband" />
        </button>

        {/* System phrase */}
        <div className="flex items-center gap-2">
          <div
            className="w-[6px] h-[6px] rounded-full transition-colors duration-500"
            style={{
              background: dotColor,
              boxShadow: state === 'critical' ? `0 0 6px ${dotColor}` : 'none',
              animation: state === 'critical' ? 'dot-pulse 2s ease-in-out infinite' : 'none',
            }}
          />
          <span
            className="text-[13px] font-medium transition-colors duration-500"
            style={{ color: state === 'nominal' ? '#94a3b8' : dotColor }}
          >
            {phrase}
          </span>
        </div>

        {!backendOk && (
          <>
            <div className="w-px h-4" style={{ background: 'var(--ms-border)' }} />
            <span className="text-[11px] text-red-400 font-medium animate-pulse">Bağlantı kesildi</span>
          </>
        )}
      </div>

      {/* Center: spacer or dashboard live status */}
      <div className="min-w-0 flex-1" />

      {/* Right: clock + theme toggle */}
      <div className="flex items-center gap-3 shrink-0">
        <span className="font-mono text-[13px] tracking-wide" style={{ color: '#94a3b8' }}>{time}</span>

        <button
          onClick={toggleTheme}
          className="h-7 w-7 flex items-center justify-center rounded text-slate-500 hover:text-slate-300 hover:bg-white/[0.04] transition-colors"
        >
          {isDark ? <Sun className="w-3.5 h-3.5" /> : <Moon className="w-3.5 h-3.5" />}
        </button>
      </div>
    </header>
  );
};

export default StatusBand;
