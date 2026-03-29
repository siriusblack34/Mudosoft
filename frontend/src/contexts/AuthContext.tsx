import React, { createContext, useContext, useState, useCallback, useEffect } from 'react';

interface AuthContextType {
  username: string;
  role: string;
  fullName: string;
  isAdmin: boolean;
  setAuth: (data: { username: string; role: string; fullName: string }) => void;
  clearAuth: () => void;
}

const AuthContext = createContext<AuthContextType>({
  username: '', role: '', fullName: '', isAdmin: false,
  setAuth: () => {}, clearAuth: () => {},
});

export const useAuth = () => useContext(AuthContext);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [auth, setAuthState] = useState(() => ({
    username: localStorage.getItem('username') || '',
    role: localStorage.getItem('role') || 'Admin',
    fullName: localStorage.getItem('fullName') || 'Administrator',
  }));

  const setAuth = useCallback((data: { username: string; role: string; fullName: string }) => {
    localStorage.setItem('username', data.username);
    localStorage.setItem('role', data.role);
    localStorage.setItem('fullName', data.fullName);
    setAuthState(data);
  }, []);

  const clearAuth = useCallback(() => {
    localStorage.removeItem('username');
    localStorage.removeItem('role');
    localStorage.removeItem('fullName');
    localStorage.removeItem('token');
    localStorage.removeItem('tokenExpiresAt');
    localStorage.removeItem('isAuthenticated');
    setAuthState({ username: '', role: '', fullName: '' });
  }, []);

  // Proactive token expiry check - every 60 seconds
  useEffect(() => {
    const checkTokenExpiry = () => {
      const expiresAt = localStorage.getItem('tokenExpiresAt');
      if (!expiresAt) return;

      const expiryTime = new Date(expiresAt).getTime();
      const now = Date.now();
      const fiveMinutes = 5 * 60 * 1000;

      // If token expired, redirect to login
      if (now >= expiryTime) {
        clearAuth();
        window.location.href = '/login';
        return;
      }

      // If token expires within 5 minutes, warn in console
      if (expiryTime - now < fiveMinutes) {
        console.warn('Token yakinda sona erecek, oturum yenilenecek.');
      }
    };

    checkTokenExpiry();
    const interval = setInterval(checkTokenExpiry, 60_000);
    return () => clearInterval(interval);
  }, [clearAuth]);

  return (
    <AuthContext.Provider value={{
      ...auth,
      isAdmin: auth.role === 'Admin',
      setAuth,
      clearAuth,
    }}>
      {children}
    </AuthContext.Provider>
  );
};
