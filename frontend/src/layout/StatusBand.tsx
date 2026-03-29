import React, { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { apiClient, API_BASE_URL } from '../lib/apiClient';
import { Device } from '../types';
import { LogOut, Sun, Moon } from 'lucide-react';
import { useTheme } from '../contexts/ThemeContext';

// ─── System phrase logic ─────────────────────────────────────────────────────

type SystemState = 'nominal' | 'degraded' | 'critical' | 'loading';

function deriveSystemState(devices: Device[]): { state: SystemState; phrase: string } {
  if (devices.length === 0) return { state: 'loading', phrase: 'Taraniyor...' };

  const activeDevices = devices.filter(d => !(d.isTemporarilyClosed || (d.excludeFromOfflineList && !d.online)));
  const total = activeDevices.length;
  if (total === 0) return { state: 'loading', phrase: 'TaranÄ±yor...' };
  const online = activeDevices.filter(d => d.online).length;
  const ratio = online / total;

  if (ratio >= 0.95) return { state: 'nominal', phrase: 'Sistem sakin' };
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

const StatusBand: React.FC<StatusBandProps> = ({ devices }) => {
  const navigate = useNavigate();
  const { fullName, role } = useAuth();
  const { isDark, toggleTheme } = useTheme();
  const [backendOk, setBackendOk] = useState(true);
  const [time, setTime] = useState(() => new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }));

  // Clock
  useEffect(() => {
    const t = setInterval(() => {
      setTime(new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }));
    }, 30_000);
    return () => clearInterval(t);
  }, []);

  // Backend health check
  const checkBackend = useCallback(async () => {
    try {
      await fetch(`${API_BASE_URL}/api/dashboard/summary`, {
        headers: { Authorization: `Bearer ${localStorage.getItem('token') || ''}` },
        signal: AbortSignal.timeout(5000),
      });
      // Herhangi bir HTTP yanıtı (200, 401, 500 vs.) = sunucu ayakta
      setBackendOk(true);
    } catch {
      // Network hatası veya timeout = gerçek bağlantı kopması
      setBackendOk(false);
    }
  }, []);

  useEffect(() => {
    checkBackend();
    const i = setInterval(checkBackend, 30_000);
    return () => clearInterval(i);
  }, [checkBackend]);

  const pcs = devices.filter(d => d.hostname?.startsWith('WSTR') || d.hostname?.startsWith('TEST'));
  const kasas = devices.filter(d => d.hostname?.startsWith('KSTR'));
  const pcOnline = pcs.filter(d => d.online).length;
  const kasaOnline = kasas.filter(d => d.online).length;
  const offline = devices.filter(d => !d.online && !d.excludeFromOfflineList && !d.isTemporarilyClosed).length;

  const { state, phrase } = deriveSystemState(devices);
  const dotColor = stateColors[state];

  const initials = (fullName || 'AD').split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2);

  const logout = () => {
    ['isAuthenticated', 'token', 'tokenExpiresAt', 'username', 'role', 'fullName']
      .forEach(k => localStorage.removeItem(k));
    window.location.href = '/login';
  };

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

  return (
    <header
      className="h-12 flex items-center justify-between px-4 shrink-0 z-50 border-b transition-colors duration-500"
      style={{
        background: state === 'critical' ? 'rgba(239,68,68,0.03)' : 'var(--cc-band)',
        borderColor: 'var(--ms-border)',
      }}
    >
      {/* Left: brand + system phrase */}
      <div className="flex items-center gap-4">
        {/* Brand */}
        <button
          onClick={() => navigate('/')}
          className="flex items-center gap-2 hover:opacity-80 transition-opacity"
        >
          <div className="h-6 w-6 rounded-md bg-violet-600 flex items-center justify-center">
            <span className="text-white font-bold text-[9px]">MS</span>
          </div>
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

        <div className="w-px h-4" style={{ background: 'var(--ms-border)' }} />

        {/* Entity counters */}
        <div className="flex items-center gap-1">
          <Chip label="PC" count={pcOnline} onClick={() => navigate('/devices')} />
          <Chip label="Kasa" count={kasaOnline} onClick={() => navigate('/kasa')} />
          {offline > 0 && (
            <Chip label="sessiz" count={offline} onClick={() => navigate('/offline-logs')} muted />
          )}
        </div>

        {!backendOk && (
          <>
            <div className="w-px h-4" style={{ background: 'var(--ms-border)' }} />
            <span className="text-[11px] text-red-400 font-medium animate-pulse">Bağlantı kesildi</span>
          </>
        )}
      </div>

      {/* Right: theme + user + clock */}
      <div className="flex items-center gap-2">
        <button
          onClick={toggleTheme}
          className="h-7 w-7 flex items-center justify-center rounded text-slate-500 hover:text-slate-300 hover:bg-white/[0.04] transition-colors"
        >
          {isDark ? <Sun className="w-3.5 h-3.5" /> : <Moon className="w-3.5 h-3.5" />}
        </button>

        <div className="w-px h-4" style={{ background: 'var(--ms-border)' }} />

        <div className="flex items-center gap-2 px-1.5 py-1 rounded hover:bg-white/[0.04] transition-colors">
          <div className="h-5 w-5 rounded-full bg-violet-700/80 flex items-center justify-center text-white font-semibold text-[8px] shrink-0">
            {initials}
          </div>
          <span className="text-[11px] text-slate-400 font-medium hidden sm:inline">{fullName || 'Admin'}</span>
          <button
            onClick={logout}
            className="p-0.5 text-slate-600 hover:text-red-400 transition-colors"
            title="Çıkış"
          >
            <LogOut className="w-3 h-3" />
          </button>
        </div>

        <div className="w-px h-4" style={{ background: 'var(--ms-border)' }} />

        <span className="text-[11px] font-mono text-slate-600 w-10 text-right">{time}</span>
      </div>
    </header>
  );
};

export default StatusBand;

