import ShellLayout from "./layout/ShellLayout";
import DashboardPage from "./pages/DashboardPage";
import DevicesPage from "./pages/DevicesPage";
import DeviceDetailsPage from "./pages/DeviceDetailsPage";
import ActionsHistoryPage from "./pages/ActionsHistoryPage";
import SettingsPage from "./pages/SettingsPage";
import SQLQueryPage from "./pages/SQLQueryPage";
import KasaPage from "./pages/KasaPage";
import ServicesPage from "./pages/ServicesPage";
import FileManagerPage from "./pages/FileManagerPage";
import SoftwareDeploymentPage from "./pages/SoftwareDeploymentPage";
import ScriptPage from "./pages/ScriptPage";
import AgentUpdatePage from "./pages/AgentUpdatePage";
import InboxCleanupPage from "./pages/InboxCleanupPage";
import NotesPage from "./pages/NotesPage";
import StockCleanupPage from "./pages/StockCleanupPage";
import DbLogCleanupPage from "./pages/DbLogCleanupPage";
import DiskStatusPage from "./pages/DiskStatusPage";

import AuthGuard from "./layout/AuthGuard";
import LoginPage from "./pages/LoginPage";
import StoreManagersPage from "./pages/StoreManagersPage";
import { Navigate } from "react-router-dom";

const routes = [
    {
        path: '/login',
        element: <LoginPage />,
    },
    {
        path: '*',
        element: <Navigate to="/" replace />
    },
    {
        element: <AuthGuard />,
        children: [
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
                path: "/devices/:deviceId/services",
                element: (
                    <ShellLayout>
                        <ServicesPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/devices/:deviceId/files",
                element: (
                    <ShellLayout>
                        <FileManagerPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/devices/:deviceId/software",
                element: (
                    <ShellLayout>
                        <SoftwareDeploymentPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/devices/:deviceId/script",
                element: (
                    <ShellLayout>
                        <ScriptPage />
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
            {
                path: "/kasa",
                element: (
                    <ShellLayout>
                        <KasaPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/agent-update",
                element: (
                    <ShellLayout>
                        <AgentUpdatePage />
                    </ShellLayout>
                ),
            },
            {
                path: "/inbox-cleanup",
                element: (
                    <ShellLayout>
                        <InboxCleanupPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/store-managers",
                element: (
                    <ShellLayout>
                        <StoreManagersPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/notes",
                element: (
                    <ShellLayout>
                        <NotesPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/stock-cleanup",
                element: (
                    <ShellLayout>
                        <StockCleanupPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/db-log-cleanup",
                element: (
                    <ShellLayout>
                        <DbLogCleanupPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/disk-status",
                element: (
                    <ShellLayout>
                        <DiskStatusPage />
                    </ShellLayout>
                ),
            },
        ],
    },
];

export default routes;