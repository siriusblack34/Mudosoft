import ShellLayout from "./layout/ShellLayout";
import DashboardPage from "./pages/DashboardPage";
import DevicesPage from "./pages/DevicesPage";
import DeviceDetailsPage from "./pages/DeviceDetailsPage";
import ActionsHistoryPage from "./pages/ActionsHistoryPage";
import SettingsPage from "./pages/SettingsPage";
import SQLQueryPage from "./pages/SQLQueryPage";
import RemoteDesktopPage from "./pages/RemoteDesktopPage";
import ServicesPage from "./pages/ServicesPage";
import FileManagerPage from "./pages/FileManagerPage";
import SoftwareDeploymentPage from "./pages/SoftwareDeploymentPage";
import ScriptPage from "./pages/ScriptPage";
import AgentUpdatePage from "./pages/AgentUpdatePage";

import AuthGuard from "./layout/AuthGuard";
import LoginPage from "./pages/LoginPage";
import { Navigate } from "react-router-dom";

const routes = [
    {
        path: "/login",
        element: <LoginPage />,
    },
    {
        path: '/remote/:deviceId',
        element: (
            <AuthGuard>
                <RemoteDesktopPage />
            </AuthGuard>
        )
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
                path: "/agent-update",
                element: (
                    <ShellLayout>
                        <AgentUpdatePage />
                    </ShellLayout>
                ),
            },
        ],
    },
];

export default routes;