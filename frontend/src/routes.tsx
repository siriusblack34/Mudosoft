// routes.tsx (DÜZELTİLMİŞ)

import ShellLayout from "./layout/ShellLayout";
import DashboardPage from "./pages/DashboardPage";
import DevicesPage from "./pages/DevicesPage";
import DeviceDetailsPage from "./pages/DeviceDetailsPage";
import ActionsHistoryPage from "./pages/ActionsHistoryPage";
import SettingsPage from "./pages/SettingsPage";
// FIX: SQLQueryPage artık varsayılan (default) export olarak içe aktarılıyor.
import SQLQueryPage from "./pages/SQLQueryPage"; 

const routes = [
  {
    path: "/",
    element: (
      <ShellLayout>
        <DashboardPage />
      </ShellLayout>
    ),
  },
  {
    path: "/devices",
    element: (
      <ShellLayout>
        <DevicesPage />
      </ShellLayout>
    ),
  },
  {
    path: "/devices/:deviceId",
    element: (
      <ShellLayout>
        <DeviceDetailsPage />
      </ShellLayout>
    ),
  },
  {
    path: "/actions",
    element: (
      <ShellLayout>
        <ActionsHistoryPage />
      </ShellLayout>
    ),
  },
  {
    path: "/settings",
    element: (
      <ShellLayout>
        <SettingsPage />
      </ShellLayout>
    ),
  },
  {
    path: "/sql-query",
    element: (
      <ShellLayout>
        <SQLQueryPage />
      </ShellLayout>
    ),
  },
];

export default routes;