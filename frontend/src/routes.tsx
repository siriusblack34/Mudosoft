import ShellLayout from "./layout/ShellLayout";
import CampaignSyncPage from "./pages/CampaignSyncPage";
import HealthScoreDashboardPage from "./pages/HealthScoreDashboardPage";
import NobetciTakipPage from "./pages/NobetciTakipPage";
import VardiyaRaporPage from "./pages/VardiyaRaporPage";
import PlaybookPage from "./pages/PlaybookPage";
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
import CleanupPage from "./pages/CleanupPage";
import NotesPage from "./pages/NotesPage";

import AuthGuard from "./layout/AuthGuard";
import AdminGuard from "./layout/AdminGuard";
import LoginPage from "./pages/LoginPage";
import StoreManagersPage from "./pages/StoreManagersPage";
import OfflineLogsPage from "./pages/OfflineLogsPage";
import DeviceHealthPage from "./pages/DeviceHealthPage";
import FiscalErrorCodesPage from "./pages/FiscalErrorCodesPage";
import PrinterLicensesPage from "./pages/PrinterLicensesPage";
import WebRdpPage from "./pages/WebRdpPage";
import HolidaysPage from "./pages/HolidaysPage";
import RemoteInstallPage from "./pages/RemoteInstallPage";
import ActiveDirectoryPage from "./pages/ActiveDirectoryPage";
import BatchScriptsPage from "./pages/BatchScriptsPage";
import BilgisayarlarPage from "./pages/BilgisayarlarPage";
import MagazalarPage from "./pages/MagazalarPage";
import RouterPage from "./pages/RouterPage";
import NetworkDiagnosticsPage from "./pages/NetworkDiagnosticsPage";
import PosLogAnalyzerPage from "./pages/PosLogAnalyzerPage";
import EventLogDiagnosticsPage from "./pages/EventLogDiagnosticsPage";
import TeamPage from "./pages/TeamPage";
import PersonelPage from "./pages/PersonelPage";
import StoreOutageReportPage from "./pages/StoreOutageReportPage";
import HardwareInventoryReportPage from "./pages/HardwareInventoryReportPage";
import InventoryPage from "./pages/InventoryPage";
import StoreOpeningsPage from "./pages/StoreOpeningsPage";
import StoreOpeningDetailPage from "./pages/StoreOpeningDetailPage";
import StoreOpeningTemplatesPage from "./pages/StoreOpeningTemplatesPage";
import FaultDensityReportPage from "./pages/FaultDensityReportPage";
import GundemPage from "./pages/GundemPage";
import OutageMailPage from "./pages/OutageMailPage";
import ActiveSessionsPage from "./pages/ActiveSessionsPage";
import SessionHistoryPage from "./pages/SessionHistoryPage";
import ServiceMonitorPage from "./pages/ServiceMonitorPage";
import YazicilarPage from "./pages/YazicilarPage";
import KampanyaKontrolPage from "./pages/KampanyaKontrolPage";
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
                path: "/magazalar",
                element: (
                    <ShellLayout>
                        <MagazalarPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/bilgisayarlar",
                element: (
                    <ShellLayout>
                        <BilgisayarlarPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/routerlar",
                element: (
                    <ShellLayout>
                        <RouterPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/yazicilar",
                element: (
                    <ShellLayout>
                        <YazicilarPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/ag-teshis",
                element: (
                    <ShellLayout>
                        <NetworkDiagnosticsPage />
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
                path: "/cleanup",
                element: (
                    <ShellLayout>
                        <CleanupPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/inbox-cleanup",
                element: <Navigate to="/cleanup?tab=plu-cache" replace />,
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
                path: "/ariza-bildirim",
                element: (
                    <ShellLayout>
                        <OutageMailPage />
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
                element: <Navigate to="/cleanup?tab=plu-sql" replace />,
            },
            {
                path: "/db-log-cleanup",
                element: <Navigate to="/cleanup?tab=db-log" replace />,
            },
            {
                path: "/disk-status",
                element: <Navigate to="/cleanup?tab=disk-status" replace />,
            },
            {
                path: "/offline-logs",
                element: (
                    <ShellLayout>
                        <OfflineLogsPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/devices/:deviceId/health",
                element: (
                    <ShellLayout>
                        <DeviceHealthPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/devices/:deviceId/rdp",
                element: <WebRdpPage />,
            },
            {
                path: "/fiscal-errors",
                element: (
                    <ShellLayout>
                        <FiscalErrorCodesPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/printer-licenses",
                element: (
                    <ShellLayout>
                        <PrinterLicensesPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/holidays",
                element: (
                    <ShellLayout>
                        <HolidaysPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/pos-log-analyzer",
                element: (
                    <ShellLayout>
                        <PosLogAnalyzerPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/event-log-diagnostics",
                element: (
                    <ShellLayout>
                        <EventLogDiagnosticsPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/personel",
                element: (
                    <ShellLayout>
                        <PersonelPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/team",
                element: (
                    <ShellLayout>
                        <TeamPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/gundem",
                element: (
                    <ShellLayout>
                        <GundemPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/remote-install",
                element: (
                    <ShellLayout>
                        <RemoteInstallPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/active-directory",
                element: (
                    <ShellLayout>
                        <AdminGuard>
                            <ActiveDirectoryPage />
                        </AdminGuard>
                    </ShellLayout>
                ),
            },
            {
                path: "/batch-scripts",
                element: (
                    <ShellLayout>
                        <BatchScriptsPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/service-monitor",
                element: (
                    <ShellLayout>
                        <ServiceMonitorPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/remote/sessions",
                element: (
                    <ShellLayout>
                        <ActiveSessionsPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/remote/history",
                element: (
                    <ShellLayout>
                        <SessionHistoryPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/reports/store-outages",
                element: (
                    <ShellLayout>
                        <StoreOutageReportPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/reports/hardware-inventory",
                element: (
                    <ShellLayout>
                        <HardwareInventoryReportPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/reports/fault-density",
                element: (
                    <ShellLayout>
                        <FaultDensityReportPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/inventory",
                element: (
                    <ShellLayout>
                        <InventoryPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/store-openings",
                element: (
                    <ShellLayout>
                        <StoreOpeningsPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/store-openings/templates",
                element: (
                    <ShellLayout>
                        <StoreOpeningTemplatesPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/store-openings/:id",
                element: (
                    <ShellLayout>
                        <StoreOpeningDetailPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/health-score",
                element: (
                    <ShellLayout>
                        <HealthScoreDashboardPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/nobetci-takip",
                element: (
                    <ShellLayout>
                        <NobetciTakipPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/vardiya-raporu",
                element: (
                    <ShellLayout>
                        <VardiyaRaporPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/playbooks",
                element: (
                    <ShellLayout>
                        <PlaybookPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/kampanya-senkron",
                element: (
                    <ShellLayout>
                        <CampaignSyncPage />
                    </ShellLayout>
                ),
            },
            {
                path: "/kampanya-kontrol",
                element: (
                    <ShellLayout>
                        <KampanyaKontrolPage />
                    </ShellLayout>
                ),
            },
        ],
    },
];

export default routes;
