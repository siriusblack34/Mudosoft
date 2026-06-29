import React, { createContext, useContext, useState, useEffect, useCallback, useMemo } from 'react';
import { apiClient } from '../lib/apiClient';
import {
  MyMenusResponse,
  resolveAllowedPaths,
  resolveMenuKey,
  ALWAYS_ALLOWED,
} from '../lib/menuCatalog';

interface MenuVisibilityContextType {
  /** Kullanıcının görebildiği menü `to` anahtarları (sidebar filtresi için). */
  allowedPaths: Set<string>;
  isAdmin: boolean;
  loading: boolean;
  /** Bir router pathname'ine erişim var mı? (route guard için) */
  canAccessPath: (pathname: string) => boolean;
  refresh: () => Promise<void>;
}

const MenuVisibilityContext = createContext<MenuVisibilityContextType>({
  allowedPaths: new Set(ALWAYS_ALLOWED),
  isAdmin: false,
  loading: true,
  canAccessPath: () => true,
  refresh: async () => {},
});

export const useMenuVisibility = () => useContext(MenuVisibilityContext);

export const MenuVisibilityProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [my, setMy] = useState<MyMenusResponse | null>(null);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    const token = localStorage.getItem('token');
    if (!token) {
      setMy(null);
      setLoading(false);
      return;
    }
    try {
      const data = await apiClient.get<MyMenusResponse>('/api/me/menus');
      setMy(data);
    } catch {
      setMy(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { refresh(); }, [refresh]);

  const allowedPaths = useMemo(() => resolveAllowedPaths(my), [my]);
  const isAdmin = my?.isAdmin ?? false;

  const canAccessPath = useCallback((pathname: string) => {
    const key = resolveMenuKey(pathname);
    if (key === null) return true;              // menüye bağlı olmayan rota → engelleme
    if (ALWAYS_ALLOWED.includes(key)) return true;
    return isAdmin || allowedPaths.has(key);
  }, [allowedPaths, isAdmin]);

  return (
    <MenuVisibilityContext.Provider value={{ allowedPaths, isAdmin, loading, canAccessPath, refresh }}>
      {children}
    </MenuVisibilityContext.Provider>
  );
};
