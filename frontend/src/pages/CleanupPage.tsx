import React from "react";
import { useSearchParams } from "react-router-dom";
import {
    Database,
    FileBarChart2,
    FolderOpen,
    HardDrive,
    Monitor,
    Server,
    Sparkles,
    Trash2,
} from "lucide-react";
import InboxCleanupPage from "./InboxCleanupPage";
import StockCleanupPage from "./StockCleanupPage";
import DbLogCleanupPage from "./DbLogCleanupPage";
import DiskStatusPage from "./DiskStatusPage";

type CleanupTab = "plu-cache" | "plu-sql" | "db-log" | "disk-status";

const tabs: Array<{
    id: CleanupTab;
    label: string;
    scope: "PC" | "DB" | "Sistem";
    description: string;
    icon: React.ElementType;
    accent: string;
}> = [
    {
        id: "plu-cache",
        label: "PLU Cache",
        scope: "PC",
        description: "Inbox, Kasa, Ready, Processed ve Seq dosyaları",
        icon: FolderOpen,
        accent: "text-orange-400 border-orange-500/30 bg-orange-500/10",
    },
    {
        id: "plu-sql",
        label: "PLU SQL",
        scope: "DB",
        description: "POS_STOCK_TRANSFER kayıtları",
        icon: Database,
        accent: "text-cyan-400 border-cyan-500/30 bg-cyan-500/10",
    },
    {
        id: "db-log",
        label: "DB Log",
        scope: "DB",
        description: "EXPORT_LOG ve EXPORT_ERR_LOG tabloları",
        icon: FileBarChart2,
        accent: "text-indigo-400 border-indigo-500/30 bg-indigo-500/10",
    },
    {
        id: "disk-status",
        label: "Disk Durumu",
        scope: "Sistem",
        description: "Mağaza PC ve kasa disk kullanımı",
        icon: HardDrive,
        accent: "text-violet-400 border-violet-500/30 bg-violet-500/10",
    },
];

const CleanupPage: React.FC = () => {
    const [searchParams, setSearchParams] = useSearchParams();
    const requestedTab = searchParams.get("tab") as CleanupTab | null;
    const activeTab = tabs.some(tab => tab.id === requestedTab) ? requestedTab! : "plu-cache";
    const activeConfig = tabs.find(tab => tab.id === activeTab) ?? tabs[0];
    const ActiveIcon = activeConfig.icon;

    const setActiveTab = (tab: CleanupTab) => {
        setSearchParams({ tab });
    };

    const renderActivePage = () => {
        switch (activeTab) {
            case "plu-sql":
                return <StockCleanupPage embedded />;
            case "db-log":
                return <DbLogCleanupPage embedded />;
            case "disk-status":
                return <DiskStatusPage embedded />;
            case "plu-cache":
            default:
                return <InboxCleanupPage embedded />;
        }
    };

    return (
        <div className="min-h-screen p-6 text-slate-200">
            <div className="mb-5 glass-panel rounded-2xl border-white/5 p-5 shadow-lg">
                <div className="flex flex-col gap-5 xl:flex-row xl:items-start xl:justify-between">
                    <div className="min-w-0">
                        <div className="flex items-center gap-3">
                            <div className="rounded-xl border border-emerald-500/25 bg-emerald-500/10 p-2">
                                <Sparkles className="h-6 w-6 text-emerald-400" />
                            </div>
                            <div>
                                <h1 className="text-2xl font-bold tracking-tight text-white">Temizlik Merkezi</h1>
                                <p className="mt-1 text-sm text-slate-400">
                                    PC klasör temizliği, DB tablo temizliği ve disk kontrolü tek ekranda.
                                </p>
                            </div>
                        </div>
                    </div>

                    <div className="grid gap-3 sm:grid-cols-3 xl:min-w-[520px]">
                        <div className="rounded-xl border border-white/10 bg-white/[0.03] p-3">
                            <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-orange-300">
                                <Monitor className="h-4 w-4" />
                                PC
                            </div>
                            <div className="mt-1 text-sm text-slate-300">PLU cache ve disk kontrolleri</div>
                        </div>
                        <div className="rounded-xl border border-white/10 bg-white/[0.03] p-3">
                            <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-cyan-300">
                                <Server className="h-4 w-4" />
                                DB
                            </div>
                            <div className="mt-1 text-sm text-slate-300">PLU SQL ve log tabloları</div>
                        </div>
                        <div className="rounded-xl border border-white/10 bg-white/[0.03] p-3">
                            <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-rose-300">
                                <Trash2 className="h-4 w-4" />
                                İşlem
                            </div>
                            <div className="mt-1 text-sm text-slate-300">Kontrol et, filtrele, temizle</div>
                        </div>
                    </div>
                </div>

                <div className="mt-5 grid gap-3 lg:grid-cols-4">
                    {tabs.map((tab) => {
                        const Icon = tab.icon;
                        const selected = tab.id === activeTab;
                        return (
                            <button
                                key={tab.id}
                                type="button"
                                onClick={() => setActiveTab(tab.id)}
                                className={`rounded-xl border p-4 text-left transition-all ${
                                    selected
                                        ? `${tab.accent} shadow-lg`
                                        : "border-white/10 bg-white/[0.03] text-slate-400 hover:border-white/20 hover:bg-white/[0.06] hover:text-slate-200"
                                }`}
                            >
                                <div className="flex items-start justify-between gap-3">
                                    <div className="flex min-w-0 items-center gap-3">
                                        <Icon className={`h-5 w-5 shrink-0 ${selected ? "" : "text-slate-500"}`} />
                                        <div className="min-w-0">
                                            <div className="flex items-center gap-2">
                                                <span className="truncate text-sm font-semibold text-white">{tab.label}</span>
                                                <span className="rounded border border-white/10 bg-black/20 px-1.5 py-0.5 text-[10px] font-bold uppercase text-slate-400">
                                                    {tab.scope}
                                                </span>
                                            </div>
                                            <p className="mt-1 line-clamp-2 text-xs text-slate-400">{tab.description}</p>
                                        </div>
                                    </div>
                                </div>
                            </button>
                        );
                    })}
                </div>
            </div>

            <div className="mb-3 flex items-center gap-2 text-xs font-semibold uppercase tracking-widest text-slate-500">
                <ActiveIcon className="h-4 w-4" />
                <span>{activeConfig.scope}</span>
                <span className="text-slate-700">/</span>
                <span className="text-slate-300">{activeConfig.label}</span>
            </div>

            {renderActivePage()}
        </div>
    );
};

export default CleanupPage;
