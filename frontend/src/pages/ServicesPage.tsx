import React from "react";
import { useParams, useNavigate } from "react-router-dom";
import ServicesPanel from "../components/devices/ServicesPanel";
import { ArrowLeft, Settings } from "lucide-react";

const ServicesPage: React.FC = () => {
    const { deviceId } = useParams<{ deviceId: string }>();
    const navigate = useNavigate();

    if (!deviceId) {
        return <div className="p-4 text-red-500">Device ID not found</div>;
    }

    return (
        <div className="space-y-6">
            {/* Header */}
            <div className="flex items-center gap-4">
                <button
                    onClick={() => navigate(`/devices/${deviceId}`)}
                    className="p-2 glass-button rounded-xl transition-colors hover-lift"
                >
                    <ArrowLeft className="w-5 h-5" />
                </button>
                <div className="flex items-center gap-3">
                    <div className="p-2 rounded-xl bg-indigo-500/20 border border-indigo-500/30 shadow-inner">
                        <Settings className="w-5 h-5 text-indigo-400" />
                    </div>
                    <h1 className="text-2xl font-bold text-white tracking-tight">Windows Services</h1>
                </div>
            </div>

            {/* Breadcrumb */}
            <div className="text-sm text-slate-400">
                <span
                    className="hover:text-white cursor-pointer"
                    onClick={() => navigate('/devices')}
                >
                    Devices
                </span>
                <span className="mx-2">›</span>
                <span
                    className="hover:text-white cursor-pointer"
                    onClick={() => navigate(`/devices/${deviceId}`)}
                >
                    Device Details
                </span>
                <span className="mx-2">›</span>
                <span className="text-white">Services</span>
            </div>

            {/* Services Panel - Full Width */}
            <ServicesPanel deviceId={deviceId} />
        </div>
    );
};

export default ServicesPage;
