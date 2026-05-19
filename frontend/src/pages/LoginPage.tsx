import React, { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { User, Lock, ArrowRight, Loader2, AlertCircle, Building2, KeyRound, X, Eye, EyeOff, ShieldAlert, CheckCircle2 } from 'lucide-react';
import { API_BASE_URL } from '../lib/apiClient';
import { useAuth } from '../contexts/AuthContext';
import { useMenuVisibility } from '../contexts/MenuVisibilityContext';
import Logo from '../components/Logo';

const LoginPage: React.FC = () => {
  const navigate = useNavigate();
  const { setAuth } = useAuth();
  const { refresh: refreshMenus } = useMenuVisibility();
  const [isLoading, setIsLoading] = useState(false);
  const [formData, setFormData] = useState({ username: '', password: '' });
  const [useLdap, setUseLdap] = useState<boolean>(() => localStorage.getItem('loginUseLdap') === '1');
  const [error, setError] = useState('');
  const [shake, setShake] = useState(false);
  const [capsLockOn, setCapsLockOn] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [success, setSuccess] = useState(false);
  const [failedAttempts, setFailedAttempts] = useState<number>(() => Number(localStorage.getItem('loginFailedAttempts') || '0'));
  const [lockedUntil, setLockedUntil] = useState<number>(() => Number(localStorage.getItem('loginLockedUntil') || '0'));
  const [now, setNow] = useState<number>(() => Date.now());
  const [lastUser, setLastUser] = useState<{ username: string; fullName: string } | null>(() => {
    try {
      const raw = localStorage.getItem('lastLoginUser');
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  });
  const usernameRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);

  // Auto-focus: son kullanıcı varsa direkt şifreye, yoksa kullanıcı adına
  useEffect(() => {
    if (lastUser) {
      setFormData((f) => ({ ...f, username: lastUser.username }));
      passwordRef.current?.focus();
    } else {
      usernameRef.current?.focus();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Caps Lock global dinleyici (klavye olayı her yerden gelebilir)
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (typeof e.getModifierState === 'function') {
        setCapsLockOn(e.getModifierState('CapsLock'));
      }
    };
    window.addEventListener('keydown', handler);
    window.addEventListener('keyup', handler);
    return () => {
      window.removeEventListener('keydown', handler);
      window.removeEventListener('keyup', handler);
    };
  }, []);

  const forgetLastUser = () => {
    localStorage.removeItem('lastLoginUser');
    setLastUser(null);
    setFormData({ username: '', password: '' });
    setTimeout(() => usernameRef.current?.focus(), 0);
  };

  // Son login tercihini hatırla
  useEffect(() => {
    localStorage.setItem('loginUseLdap', useLdap ? '1' : '0');
  }, [useLdap]);

  // Kilitliyken her saniye sayaç güncelle
  useEffect(() => {
    if (lockedUntil <= Date.now()) return;
    const id = setInterval(() => setNow(Date.now()), 500);
    return () => clearInterval(id);
  }, [lockedUntil]);

  const isLocked = lockedUntil > now;
  const lockSecondsLeft = isLocked ? Math.ceil((lockedUntil - now) / 1000) : 0;
  const lockTotalSeconds = failedAttempts >= 7 ? 120 : failedAttempts >= 5 ? 60 : 30;
  const lockProgressPct = isLocked ? Math.max(0, Math.min(100, (lockSecondsLeft / lockTotalSeconds) * 100)) : 0;

  const registerFailure = () => {
    const newCount = failedAttempts + 1;
    setFailedAttempts(newCount);
    localStorage.setItem('loginFailedAttempts', String(newCount));
    if (newCount >= 3) {
      const lockMs = newCount >= 7 ? 120_000 : newCount >= 5 ? 60_000 : 30_000;
      const until = Date.now() + lockMs;
      setLockedUntil(until);
      setNow(Date.now());
      localStorage.setItem('loginLockedUntil', String(until));
    }
  };

  const clearFailures = () => {
    setFailedAttempts(0);
    setLockedUntil(0);
    localStorage.removeItem('loginFailedAttempts');
    localStorage.removeItem('loginLockedUntil');
  };

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    if (isLocked) return;

    if (!formData.username || !formData.password) {
      setError('Kullanıcı adı ve şifre giriniz.');
      setShake(true);
      setTimeout(() => setShake(false), 600);
      if (!formData.username) usernameRef.current?.focus();
      else if (!formData.password) passwordRef.current?.focus();
      return;
    }

    setIsLoading(true);

    try {
      const res = await fetch(`${API_BASE_URL}/api/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          username: formData.username,
          password: formData.password,
          useLdap,
        }),
      });

      if (!res.ok) {
        const body = await res.json().catch(() => null);
        registerFailure();
        const msg = body?.error || (res.status === 401
          ? 'Kullanıcı adı veya şifre hatalı.'
          : res.status >= 500
          ? 'Sunucu hatası oluştu. Lütfen daha sonra tekrar deneyin.'
          : 'Giriş başarısız. Bilgilerinizi kontrol edin.');
        setError(msg);
        setShake(true);
        setTimeout(() => setShake(false), 600);
        passwordRef.current?.focus();
        passwordRef.current?.select();
        return;
      }

      const data = await res.json();
      localStorage.setItem('token', data.token);
      localStorage.setItem('tokenExpiresAt', data.expiresAt);
      localStorage.setItem('isAuthenticated', 'true');
      localStorage.setItem('username', data.username);
      localStorage.setItem('role', data.role || 'Admin');
      localStorage.setItem('fullName', data.fullName || data.username);
      localStorage.setItem('lastLoginUser', JSON.stringify({
        username: data.username,
        fullName: data.fullName || data.username,
      }));
      clearFailures();
      setAuth({ username: data.username, role: data.role || 'Admin', fullName: data.fullName || data.username });
      await refreshMenus();
      setSuccess(true);
      setTimeout(() => navigate('/'), 750);
    } catch {
      setError('Sunucuya bağlanılamadı. Ağ bağlantınızı kontrol edip tekrar deneyin.');
      setShake(true);
      setTimeout(() => setShake(false), 600);
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-ms-bg flex items-center justify-center relative overflow-hidden">
      {/* Arka plan: aurora + sürüklenen orblar + ızgara + yıldızlar + partiküller */}
      {/* Başarı geçiş efekti */}
      {success && <div className="login-success-flash" />}

      <div className="login-bg">
        <div className={`login-bg-aurora ${success ? 'boost' : ''}`} />
        <div className="login-bg-grid" />
        <div className="login-bg-orb login-bg-orb-1" />
        <div className="login-bg-orb login-bg-orb-2" />
        <div className="login-bg-orb login-bg-orb-3" />
        <div className="login-bg-orb login-bg-orb-4" />
        {Array.from({ length: 35 }).map((_, i) => {
          const top = (i * 37) % 100;
          const left = (i * 71) % 100;
          const delay = (i * 0.3) % 5;
          return (
            <span
              key={`star-${i}`}
              className="login-bg-star"
              style={{ top: `${top}%`, left: `${left}%`, animationDelay: `${delay}s` }}
            />
          );
        })}
        {Array.from({ length: 22 }).map((_, i) => {
          const left = (i * 53) % 100;
          const duration = 12 + (i % 7) * 2;
          const delay = (i * 0.9) % 14;
          const size = 2 + (i % 3);
          const alt = i % 3 === 0;
          return (
            <span
              key={`p-${i}`}
              className={`login-bg-particle ${alt ? 'alt' : ''}`}
              style={{
                left: `${left}%`,
                width: size,
                height: size,
                animationDuration: `${duration}s`,
                animationDelay: `${delay}s`,
              }}
            />
          );
        })}
      </div>

      {/* Kart */}
      <div className={`relative w-full max-w-sm mx-4 ${success ? 'login-card-exit' : ''}`}>
        {/* Logo */}
        <div className="flex flex-col items-center mb-8">
          <Logo size={150} idSuffix="login" className="mb-4" />
          <h1 className="text-xl font-bold text-ms-text tracking-tight">Orchestra</h1>
          <p className="text-ms-text-muted text-sm mt-1">
            {lastUser ? (
              <>Tekrar hoş geldin, <span className="text-violet-300 font-medium">{lastUser.fullName.split(' ')[0]}</span></>
            ) : (
              'Yönetim Portalına Giriş'
            )}
          </p>
        </div>

        {/* Form */}
        <div className={`bg-ms-bg-soft border border-ms-border rounded-2xl p-6 shadow-2xl transition-all ${shake ? 'animate-[shake_0.5s_ease-in-out]' : ''}`} style={shake ? { animation: 'shake 0.5s ease-in-out' } : undefined}>
          <form onSubmit={handleLogin} className="space-y-4">
            {/* Kullanıcı adı */}
            <div>
              <label className="form-label flex items-center justify-between">
                <span>Kullanıcı Adı</span>
                {lastUser && (
                  <button
                    type="button"
                    onClick={forgetLastUser}
                    className="text-[10px] text-zinc-500 hover:text-violet-300 transition-colors flex items-center gap-1"
                    title="Bu hesabı unut"
                  >
                    <X className="w-3 h-3" />
                    Ben değilim
                  </button>
                )}
              </label>
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <User className="h-4 w-4 text-zinc-500" />
                </div>
                <input
                  ref={usernameRef}
                  type="text"
                  className="w-full pl-9"
                  placeholder="admin"
                  value={formData.username}
                  onChange={(e) => { setFormData({ ...formData, username: e.target.value }); setError(''); }}
                  autoComplete="username"
                />
              </div>
            </div>

            {/* Şifre */}
            <div>
              <label className="form-label flex items-center justify-between">
                <span>Şifre</span>
                {capsLockOn && (
                  <span className="text-[10px] text-amber-400 flex items-center gap-1 font-medium animate-[fade-in_0.2s_ease-out]">
                    <KeyRound className="w-3 h-3" />
                    Caps Lock açık
                  </span>
                )}
              </label>
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Lock className="h-4 w-4 text-zinc-500" />
                </div>
                <input
                  ref={passwordRef}
                  type={showPassword ? 'text' : 'password'}
                  className={`w-full pl-9 pr-10 ${capsLockOn ? 'ring-1 ring-amber-400/40' : ''}`}
                  placeholder="••••••••"
                  value={formData.password}
                  onChange={(e) => { setFormData({ ...formData, password: e.target.value }); setError(''); }}
                  autoComplete="current-password"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword((v) => !v)}
                  className="absolute inset-y-0 right-0 pr-3 flex items-center text-zinc-500 hover:text-violet-300 transition-colors"
                  tabIndex={-1}
                  title={showPassword ? 'Şifreyi gizle' : 'Şifreyi göster'}
                  aria-label={showPassword ? 'Şifreyi gizle' : 'Şifreyi göster'}
                >
                  {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              </div>
            </div>

            {/* Domain hesabıyla giriş */}
            <label className="flex items-center gap-2 text-xs text-ms-text-muted cursor-pointer select-none">
              <input
                type="checkbox"
                checked={useLdap}
                onChange={(e) => setUseLdap(e.target.checked)}
                className="rounded border-ms-border bg-ms-bg accent-violet-500"
              />
              <Building2 className="w-3.5 h-3.5" />
              <span>Domain hesabıyla giriş</span>
            </label>

            {/* Hata mesajı + başarısız deneme sayacı */}
            {error && !isLocked && (
              <div className="flex items-start gap-2 text-red-400 text-xs bg-red-500/10 border border-red-500/20 rounded-lg py-2.5 px-3">
                <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
                <div className="flex-1">
                  <div>{error}</div>
                  {failedAttempts > 0 && failedAttempts < 3 && (
                    <div className="text-[10px] text-red-300/80 mt-1">
                      {failedAttempts}/3 başarısız deneme — {3 - failedAttempts} hak kaldı
                    </div>
                  )}
                </div>
              </div>
            )}

            {/* Rate-limit / kilit uyarısı */}
            {isLocked && (
              <div className="flex items-start gap-2 text-amber-300 text-xs bg-amber-500/10 border border-amber-500/30 rounded-lg py-2.5 px-3">
                <ShieldAlert className="w-4 h-4 shrink-0 mt-0.5" />
                <div className="flex-1">
                  <div className="font-medium">Çok fazla başarısız deneme</div>
                  <div className="text-[10px] text-amber-300/80 mt-1">
                    Güvenlik için giriş <span className="font-mono font-bold">{lockSecondsLeft}sn</span> süreyle kilitlendi.
                  </div>
                  <div className="w-full h-1 bg-amber-500/15 rounded-full mt-2 overflow-hidden">
                    <div
                      className="h-full bg-amber-400/70 transition-[width] duration-500 ease-linear"
                      style={{ width: `${lockProgressPct}%` }}
                    />
                  </div>
                </div>
              </div>
            )}

            {/* Giriş butonu */}
            <button
              type="submit"
              disabled={isLoading || isLocked || success}
              className="btn-primary w-full justify-center py-2.5 mt-2"
            >
              {success ? (
                <>
                  <CheckCircle2 className="w-4 h-4" />
                  Giriş başarılı
                </>
              ) : isLocked ? (
                <>
                  <ShieldAlert className="w-4 h-4" />
                  {lockSecondsLeft}sn bekleyin
                </>
              ) : isLoading ? (
                <Loader2 className="w-4 h-4 animate-spin" />
              ) : (
                <>
                  Giriş Yap
                  <ArrowRight className="w-4 h-4" />
                </>
              )}
            </button>
          </form>
        </div>

        <p className="text-center text-zinc-600 text-xs mt-6">
          &copy; {new Date().getFullYear()} Orchestra. Tüm hakları saklıdır.
        </p>
      </div>
    </div>
  );
};

export default LoginPage;
