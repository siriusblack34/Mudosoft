import React, { useState } from 'react';
import { apiClient } from '../../lib/apiClient';
import type { SqlResult } from '../../types';

interface Props {
  deviceId: string;
}

const SqlQueryPanel: React.FC<Props> = ({ deviceId }) => {
  const [query, setQuery] = useState('SELECT TOP 10 * FROM sys.databases');
  const [result, setResult] = useState<SqlResult | null>(null);
  const [busy, setBusy] = useState(false);

  const run = async () => {
    setBusy(true);
    try {
      const res = await apiClient.runSql(deviceId, query);
      setResult(res);
    } catch (err) {
      console.error(err);
      alert('SQL execution failed.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="rounded-2xl border border-ms-border bg-ms-panel p-4">
      <div className="flex items-center justify-between mb-3">
        <div className="text-sm font-medium">SQL Query</div>
        <button
          disabled={busy}
          onClick={run}
          className="px-3 py-1 rounded-xl bg-ms-accent-soft text-xs"
        >
          Run on device
        </button>
      </div>
      <textarea
        className="w-full h-24 text-xs bg-ms-bg-soft border border-ms-border rounded-xl p-2 outline-none"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
      />
      {result && (
        <div className="mt-3 border border-ms-border rounded-xl overflow-x-auto text-xs">
          <table className="w-full">
            <thead className="bg-ms-bg-soft">
              <tr>
                {result.columns.map((c) => (
                  <th key={c} className="px-2 py-1 text-left">
                    {c}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {result.rows.map((row, i) => (
                <tr key={i} className="border-t border-ms-border/60">
                  {row.map((cell, j) => (
                    <td key={j} className="px-2 py-1">
                      {cell as string}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};

export default SqlQueryPanel;
