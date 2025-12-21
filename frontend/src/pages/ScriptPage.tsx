import React from "react";
import { useParams, useNavigate } from "react-router-dom";
import RunScriptPanel from "../components/devices/RunScriptPanel";
import { ArrowLeft, Terminal } from "lucide-react";

const ScriptPage: React.FC = () => {
    const { deviceId } = useParams<{ deviceId: string }>();
    const navigate = useNavigate();

    if (!deviceId) {
        return <div className="p-4 text-red-500">Device ID not found</div>;
    }

    return (
        <div className="space-y-6 p-4">
            {/* Header */}
            <div className="flex items-center gap-4">
                <button
                    onClick={() => navigate(`/devices/${deviceId}`)}
                    className="p-2 hover:bg-slate-700 rounded-lg transition-colors"
                >
                    <ArrowLeft className="w-5 h-5" />
                </button>
                <div className="flex items-center gap-2">
                    <Terminal className="w-6 h-6 text-amber-400" />
                    <h1 className="text-2xl font-semibold">Remote Script</h1>
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
                <span className="text-white">Script</span>
            </div>

            {/* Script Panel */}
            <RunScriptPanel deviceId={deviceId} />
        </div>
    );
};

export default ScriptPage;
