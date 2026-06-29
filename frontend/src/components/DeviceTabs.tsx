import React from "react";
import { NavLink } from "react-router-dom";
import { LayoutGrid, Monitor, Printer, ShoppingCart, Wifi } from "lucide-react";

const tabs = [
    { to: "/devices",       label: "Tümü",    icon: <LayoutGrid className="h-3.5 w-3.5" /> },
    { to: "/bilgisayarlar", label: "PC",      icon: <Monitor className="h-3.5 w-3.5" /> },
    { to: "/kasa",          label: "Kasa",    icon: <ShoppingCart className="h-3.5 w-3.5" /> },
    { to: "/routerlar",     label: "Router",  icon: <Wifi className="h-3.5 w-3.5" /> },
    { to: "/yazicilar",     label: "Yazıcı",  icon: <Printer className="h-3.5 w-3.5" /> },
];

const DeviceTabs: React.FC = () => (
    <div className="flex items-center gap-1 rounded-lg border border-ms-border bg-ms-bg-soft p-1 w-fit">
        {tabs.map(t => (
            <NavLink
                key={t.to}
                to={t.to}
                end
                className={({ isActive }) =>
                    `inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-[12px] font-medium transition-colors ${
                        isActive
                            ? "bg-sky-500/15 text-sky-700 dark:text-sky-300"
                            : "text-ms-text-muted hover:bg-black/[0.04] hover:text-ms-text dark:hover:bg-white/[0.04]"
                    }`
                }
            >
                {t.icon}
                {t.label}
            </NavLink>
        ))}
    </div>
);

export default DeviceTabs;
