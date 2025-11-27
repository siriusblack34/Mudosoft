import React from 'react';
import { RouteObject } from 'react-router-dom';
import DashboardPage from './pages/DashboardPage';
import DevicesPage from './pages/DevicesPage';
import DeviceDetailsPage from './pages/DeviceDetailsPage';



export const routes: RouteObject[] = [
  { path: '/', element: <DashboardPage /> },
  { path: '/devices', element: <DevicesPage /> },
  // 🏆 HATA GİDERME: URL parametresi, bileşen içinde çekilen adla eşleşecek şekilde düzeltildi.
  { path: '/devices/:deviceId', element: <DeviceDetailsPage /> },
];