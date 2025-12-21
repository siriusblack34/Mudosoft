import React from 'react';
import { Navigate, Outlet } from 'react-router-dom';

interface Props {
    children?: React.ReactNode;
}

const AuthGuard: React.FC<Props> = ({ children }) => {
    // Basit Auth Kontrolü (Mock)
    const isAuthenticated = localStorage.getItem('isAuthenticated') === 'true';

    if (!isAuthenticated) {
        return <Navigate to="/login" replace />;
    }

    return children ? <>{children}</> : <Outlet />;
};

export default AuthGuard;
