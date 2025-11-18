import React, { useState } from 'react';
import { apiClient } from '../../lib/apiClient';

interface Props {
  deviceId: string;
}

const FileTransferPanel: React.FC<Props> = ({ deviceId }) => {
  const [file, setFile] = useState<File | null>(null);
  const [remotePath, setRemotePath] = useState('C:\\Temp\\');
  const [busy, setBusy] = useState(false);

  const upload = async () => {
    if (!file) return;
    setBusy(true);
    try {
      await apiClient.uploadFile(deviceId, file, remotePath);
      alert('File uploaded.');
      setFile(null);
    } catch (err) {
      console.error(err);
      alert('Upload failed.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="rounded-2xl border border-ms-border bg-ms-panel p-4">
      <div className="text-sm font-medium mb-3">File Transfer</div>
      <div className="flex flex-col gap-3 text-xs">
        <input
          type="file"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          className="text-[11px]"
        />
        <input
          type="text"
          value={remotePath}
          onChange={(e) => setRemotePath(e.target.value)}
          className="bg-ms-bg-soft border border-ms-border rounded-xl px-2 py-1 outline-none"
          placeholder="Remote path on device"
        />
        <button
          disabled={busy || !file}
          onClick={upload}
          className="self-start px-3 py-1 rounded-xl bg-ms-accent-soft"
        >
          Push File
        </button>
      </div>
    </div>
  );
};

export default FileTransferPanel;
