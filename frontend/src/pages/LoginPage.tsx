import React, { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { User, Lock, ArrowRight, Loader2, AlertCircle } from 'lucide-react';
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
  const [error, setError] = useState('');
  const [shake, setShake] = useState(false);
  const usernameRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);

  // Auto-focus username input on mount
  useEffect(() => {
    usernameRef.current?.focus();
  }, []);

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

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
        }),
      });

      if (!res.ok) {
        const body = await res.json().catch(() => null);
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
      setAuth({ username: data.username, role: data.role || 'Admin', fullName: data.fullName || data.username });
      await refreshMenus();
      navigate('/');
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
      {/* Arka plan dekorasyon */}
      <div className="absolute inset-0 overflow-hidden pointer-events-none">
        <div className="absolute top-1/4 left-1/4 w-96 h-96 bg-violet-600/5 rounded-full blur-3xl" />
        <div className="absolute bottom-1/4 right-1/4 w-96 h-96 bg-violet-900/10 rounded-full blur-3xl" />
      </div>

      {/* Kart */}
      <div className="relative w-full max-w-sm mx-4">
        {/* Logo */}
        <div className="flex flex-col items-center mb-8">
          <Logo size={150} idSuffix="login" className="mb-4" />
          <h1 className="text-xl font-bold text-ms-text tracking-tight">Orchestra</h1>
          <p className="text-ms-text-muted text-sm mt-1">Yönetim Portalına Giriş</p>
        </div>

        {/* Form */}
        <div className={`bg-ms-bg-soft border border-ms-border rounded-2xl p-6 shadow-2xl transition-all ${shake ? 'animate-[shake_0.5s_ease-in-out]' : ''}`} style={shake ? { animation: 'shake 0.5s ease-in-out' } : undefined}>
          <form onSubmit={handleLogin} className="space-y-4">
            {/* Kullanıcı adı */}
            <div>
              <label className="form-label">Kullanıcı Adı</label>
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
              <label className="form-label">Şifre</label>
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Lock className="h-4 w-4 text-zinc-500" />
                </div>
                <input
                  ref={passwordRef}
                  type="password"
                  className="w-full pl-9"
                  placeholder="••••••••"
                  value={formData.password}
                  onChange={(e) => { setFormData({ ...formData, password: e.target.value }); setError(''); }}
                  autoComplete="current-password"
                />
              </div>
            </div>

            {/* Hata mesajı */}
            {error && (
              <div className="flex items-center gap-2 text-red-400 text-xs bg-red-500/10 border border-red-500/20 rounded-lg py-2.5 px-3">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>{error}</span>
              </div>
            )}

            {/* Giriş butonu */}
            <button
              type="submit"
              disabled={isLoading}
              className="btn-primary w-full justify-center py-2.5 mt-2"
            >
              {isLoading ? (
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
