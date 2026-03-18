import React, { createContext, useContext, useState, useCallback } from 'react';

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
    setAuthState({ username: '', role: '', fullName: '' });
  }, []);

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
