import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';
import { apiClient } from '../lib/apiClient';

interface MenuVisibilityContextType {
  hiddenMenus: string[];
  loading: boolean;
  setHiddenMenus: (paths: string[]) => Promise<void>;
  refresh: () => Promise<void>;
}

const MenuVisibilityContext = createContext<MenuVisibilityContextType>({
  hiddenMenus: [],
  loading: true,
  setHiddenMenus: async () => {},
  refresh: async () => {},
});

export const useMenuVisibility = () => useContext(MenuVisibilityContext);

export const MenuVisibilityProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [hiddenMenus, setHiddenState] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    const token = localStorage.getItem('token');
    if (!token) {
      setHiddenState([]);
      setLoading(false);
      return;
    }
    try {
      const data = await apiClient.get<string[]>('/api/app-settings/hidden-menus');
      setHiddenState(data);
    } catch {
      setHiddenState([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { refresh(); }, [refresh]);

  const setHiddenMenus = useCallback(async (paths: string[]) => {
    await apiClient.put<string[]>('/api/app-settings/hidden-menus', paths);
    setHiddenState(paths);
  }, []);

  return (
    <MenuVisibilityContext.Provider value={{ hiddenMenus, loading, setHiddenMenus, refresh }}>
      {children}
    </MenuVisibilityContext.Provider>
  );
};
