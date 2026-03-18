import React, { useState, useMemo, useCallback } from 'react';
import { Search } from 'lucide-react';
import licensesData from '../data/printerLicenses.json';

interface License {
  serialNo: string;
  epayKeyInfo?: string;
  epayActivationKey?: string;
  geniusKeyInfo?: string;
  geniusActivationKey?: string;
}

const licenses = licensesData as License[];

const copy = (text: string) => navigator.clipboard.writeText(text);

const Cell: React.FC<{ val?: string }> = ({ val }) => (
  <td className="px-3 py-2 font-mono text-xs text-ms-text cursor-pointer hover:text-violet-400 transition-colors"
    onClick={() => val && copy(val)} title={val ? 'Tıkla → kopyala' : ''}>
    {val || '-'}
  </td>
);

const PrinterLicensesPage: React.FC = () => {
  const [search, setSearch] = useState('');

  const results = useMemo(() => {
    const q = search.trim().toUpperCase();
    if (q.length < 3) return [];
    return licenses.filter(l => l.serialNo.toUpperCase().includes(q));
  }, [search]);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <div className="relative flex-1 max-w-sm">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-ms-text-muted" />
          <input
            placeholder="Seri no ara (min 3 karakter)..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="w-full pl-9 text-sm"
            autoFocus
          />
        </div>
        <span className="text-xs text-ms-text-muted">{licenses.length} kayıt</span>
      </div>

      {search.trim().length >= 3 && results.length === 0 && (
        <p className="text-sm text-ms-text-muted py-6 text-center">Sonuç bulunamadı</p>
      )}

      {results.length > 0 && (
        <div className="bg-ms-bg-soft border border-ms-border rounded-xl overflow-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-ms-border text-ms-text-muted text-xs">
                <th className="text-left px-3 py-2.5">Seri No</th>
                <th className="text-left px-3 py-2.5">EPAY Anahtar</th>
                <th className="text-left px-3 py-2.5">EPAY Aktivasyon</th>
                <th className="text-left px-3 py-2.5">Genius Anahtar</th>
                <th className="text-left px-3 py-2.5">Genius Aktivasyon</th>
              </tr>
            </thead>
            <tbody>
              {results.map(l => (
                <tr key={l.serialNo} className="border-b border-ms-border/40 hover:bg-ms-border/20">
                  <td className="px-3 py-2 font-mono text-xs font-bold text-violet-400">{l.serialNo}</td>
                  <Cell val={l.epayKeyInfo} />
                  <Cell val={l.epayActivationKey} />
                  <Cell val={l.geniusKeyInfo} />
                  <Cell val={l.geniusActivationKey} />
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {search.trim().length < 3 && (
        <p className="text-sm text-ms-text-muted py-6 text-center">Yazıcı seri numarasını yazarak arayın</p>
      )}
    </div>
  );
};

export default PrinterLicensesPage;
