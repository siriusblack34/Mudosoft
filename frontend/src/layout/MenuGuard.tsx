import React from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useMenuVisibility } from '../contexts/MenuVisibilityContext';

/**
 * Aktif rotanın, kullanıcının etkin menü erişimi içinde olup olmadığını denetler.
 * Menüsü olmayan bir sayfayı URL'yi elle yazarak açmaya çalışan kullanıcı '/'a yönlendirilir.
 * Admin her şeye erişir. Erişim bilgisi henüz yüklenmemişse (loading) engelleme yapılmaz.
 */
const MenuGuard: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { canAccessPath, loading } = useMenuVisibility();
  const location = useLocation();

  if (!loading && !canAccessPath(location.pathname)) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
};

export default MenuGuard;
