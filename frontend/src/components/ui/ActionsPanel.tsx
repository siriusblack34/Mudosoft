
import React, { useState } from 'react';
import { apiService } from '../../services/apiService';
import { ActionType } from '../../types';
import { RebootIcon, TerminalIcon, FileIcon, SqlIcon } from '../icons/Icons';
import Modal from './Modal';
import Spinner from './Spinner';

interface ActionsPanelProps {
    deviceId: string;
    onActionFeedback: (type: 'success' | 'error', message: string) => void;
}

const ActionButton: React.FC<{
    icon: React.ReactNode;
    label: string;
    onClick: () => void;
    isDangerous?: boolean;
    disabled?: boolean;
}> = ({ icon, label, onClick, isDangerous = false, disabled = false }) => (
    <button
        onClick={onClick}
        disabled={disabled}
        className={`w-full flex items-center space-x-3 px-4 py-3 rounded-lg transition-all duration-200 text-left
        ${isDangerous ? 'hover:bg-red-500/20 hover:text-red-400' : 'hover:bg-cyan-500/20 hover:text-cyan-400'}
        ${disabled ? 'opacity-50 cursor-not-allowed' : 'text-gray-300'}`}
    >
        {icon}
        <span>{label}</span>
    </button>
);


const ActionsPanel: React.FC<ActionsPanelProps> = ({ deviceId, onActionFeedback }) => {
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [modalContent, setModalContent] = useState<{ title: string; type: 'ps' | 'sql' | 'file' } | null>(null);
    const [script, setScript] = useState('');
    const [isExecuting, setIsExecuting] = useState(false);

    const handleAction = async (action: ActionType, params?: any) => {
        setIsExecuting(true);
        try {
            const result = await apiService.executeAction(deviceId, action, params);
            onActionFeedback(result.success ? 'success' : 'error', result.message);
        } catch (error) {
            onActionFeedback('error', `An unexpected error occurred while performing ${action}.`);
        }
        setIsExecuting(false);
        setIsModalOpen(false);
        setScript('');
    };
    
    const openModal = (title: string, type: 'ps' | 'sql' | 'file') => {
        setModalContent({ title, type });
        setIsModalOpen(true);
    };

    return (
        <>
            <div className="bg-gray-800/70 p-4 rounded-xl shadow-lg border border-gray-700 h-full">
                <h2 className="text-xl font-semibold text-white mb-4">Remote Actions</h2>
                <div className="space-y-2">
                    <ActionButton icon={<TerminalIcon className="h-5 w-5"/>} label="Run PowerShell" onClick={() => openModal('Run PowerShell Script', 'ps')} disabled={isExecuting} />
                    <ActionButton icon={<SqlIcon className="h-5 w-5"/>} label="Execute SQL Query" onClick={() => openModal('Execute SQL Query', 'sql')} disabled={isExecuting} />
                    <ActionButton icon={<FileIcon className="h-5 w-5"/>} label="Push File" onClick={() => openModal('Push File', 'file')} disabled={isExecuting} />
                    <ActionButton icon={<RebootIcon className="h-5 w-5"/>} label="Reboot Device" isDangerous onClick={() => handleAction(ActionType.Reboot)} disabled={isExecuting} />
                </div>
            </div>

            {modalContent && (
                 <Modal isOpen={isModalOpen} onClose={() => setIsModalOpen(false)} title={modalContent.title}>
                    <div className="space-y-4">
                        { (modalContent.type === 'ps' || modalContent.type === 'sql') && (
                            <textarea
                                value={script}
                                onChange={(e) => setScript(e.target.value)}
                                placeholder={modalContent.type === 'ps' ? "Enter PowerShell script..." : "Enter SQL query..."}
                                className="w-full h-48 bg-gray-900 border border-gray-600 rounded-md p-2 text-white font-mono text-sm focus:outline-none focus:ring-2 focus:ring-cyan-500"
                            />
                        )}
                         { modalContent.type === 'file' && (
                             <div>
                                 <label className="block text-sm font-medium text-gray-300 mb-2">Select file to upload:</label>
                                 <input type="file" className="block w-full text-sm text-gray-400 file:mr-4 file:py-2 file:px-4 file:rounded-full file:border-0 file:text-sm file:font-semibold file:bg-cyan-500/10 file:text-cyan-400 hover:file:bg-cyan-500/20"/>
                             </div>
                         )}

                        <button
                            onClick={() => handleAction(modalContent.type === 'ps' ? ActionType.RunPS : ActionType.RunSQL, { script })}
                            disabled={isExecuting || (modalContent.type !== 'file' && !script)}
                            className="w-full bg-cyan-600 hover:bg-cyan-700 disabled:bg-gray-500 text-white font-bold py-2 px-4 rounded-md transition-colors flex justify-center items-center"
                        >
                            {isExecuting ? <Spinner size="sm" /> : 'Execute'}
                        </button>
                    </div>
                 </Modal>
            )}
        </>
    );
};

export default ActionsPanel;
