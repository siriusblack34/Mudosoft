import React from 'react';
import { Navigate, Outlet } from 'react-router-dom';

interface Props {
    children?: React.ReactNode;
}

const AuthGuard: React.FC<Props> = ({ children }) => {
    const token = localStorage.getItem('token');
    const expiresAt = localStorage.getItem('tokenExpiresAt');

    // Check token exists and is not expired
    const isAuthenticated = !!token && (!expiresAt || new Date(expiresAt) > new Date());

    if (!isAuthenticated) {
        localStorage.removeItem('token');
        localStorage.removeItem('tokenExpiresAt');
        localStorage.removeItem('isAuthenticated');
        return <Navigate to="/login" replace />;
    }

    return children ? <>{children}</> : <Outlet />;
};

export default AuthGuard;
