import React, { useState, useMemo, useCallback } from 'react';
import {
  Search, Briefcase, MapPin, Calendar, Phone, Building2, Users2,
  ChevronLeft, ChevronRight, LayoutGrid, List, Hash, Filter,
} from 'lucide-react';
import personelData from '../data/personel.json';

interface Personel {
  no: string;
  ad: string;
  soyad: string;
  gorevKod: number;
  gorev: string;
  dogumYeri: string;
  dogumTarih: string;
  girisTarih: string;
  telefon: string;
  birimNo: number;
  birim: string;
  departmanKod: number;
  departman: string;
}

const allPersonel: Personel[] = personelData as Personel[];

const PAGE_SIZE = 48;

function formatName(name: string): string {
  if (!name) return '';
  return name.split(' ').map(w => w.charAt(0).toUpperCase() + w.slice(1).toLowerCase()).join(' ');
}

function getInitials(ad: string, soyad: string): string {
  const a = ad.trim().charAt(0) || '';
  const s = soyad.trim().charAt(0) || '';
  return (a + s).toUpperCase();
}

function parseDate(d: string): Date | null {
  if (!d) return null;
  const [day, month, year] = d.split('/').map(Number);
  return new Date(year, month - 1, day);
}

function calcTenureLabel(girisTarih: string): string {
  const start = parseDate(girisTarih);
  if (!start) return '';
  const now = new Date();
  const diffMs = now.getTime() - start.getTime();
  const totalDays = diffMs / (1000 * 60 * 60 * 24);
  const years = Math.floor(totalDays / 365.25);
  const months = Math.floor((totalDays % 365.25) / 30.44);
  if (years < 1) return months <= 0 ? '< 1 ay' : `${months} ay`;
  if (months === 0) return `${years} yıl`;
  return `${years} yıl ${months} ay`;
}

// Stable color palette based on department
const deptColors = [
  { bg: 'bg-violet-500/15', color: 'text-violet-300', border: 'border-violet-500/30', grad: 'from-violet-600 to-purple-500' },
  { bg: 'bg-rose-500/15', color: 'text-rose-300', border: 'border-rose-500/30', grad: 'from-rose-600 to-pink-500' },
  { bg: 'bg-cyan-500/15', color: 'text-cyan-300', border: 'border-cyan-500/30', grad: 'from-cyan-600 to-teal-500' },
  { bg: 'bg-amber-500/15', color: 'text-amber-300', border: 'border-amber-500/30', grad: 'from-amber-600 to-yellow-500' },
  { bg: 'bg-emerald-500/15', color: 'text-emerald-300', border: 'border-emerald-500/30', grad: 'from-emerald-600 to-green-500' },
  { bg: 'bg-blue-500/15', color: 'text-blue-300', border: 'border-blue-500/30', grad: 'from-blue-600 to-indigo-500' },
  { bg: 'bg-orange-500/15', color: 'text-orange-300', border: 'border-orange-500/30', grad: 'from-orange-600 to-red-500' },
  { bg: 'bg-pink-500/15', color: 'text-pink-300', border: 'border-pink-500/30', grad: 'from-pink-600 to-fuchsia-500' },
  { bg: 'bg-teal-500/15', color: 'text-teal-300', border: 'border-teal-500/30', grad: 'from-teal-600 to-cyan-500' },
  { bg: 'bg-indigo-500/15', color: 'text-indigo-300', border: 'border-indigo-500/30', grad: 'from-indigo-600 to-blue-500' },
  { bg: 'bg-lime-500/15', color: 'text-lime-300', border: 'border-lime-500/30', grad: 'from-lime-600 to-green-500' },
  { bg: 'bg-fuchsia-500/15', color: 'text-fuchsia-300', border: 'border-fuchsia-500/30', grad: 'from-fuchsia-600 to-purple-500' },
  { bg: 'bg-sky-500/15', color: 'text-sky-300', border: 'border-sky-500/30', grad: 'from-sky-600 to-blue-500' },
  { bg: 'bg-red-500/15', color: 'text-red-300', border: 'border-red-500/30', grad: 'from-red-600 to-rose-500' },
  { bg: 'bg-yellow-500/15', color: 'text-yellow-300', border: 'border-yellow-500/30', grad: 'from-yellow-600 to-amber-500' },
  { bg: 'bg-purple-500/15', color: 'text-purple-300', border: 'border-purple-500/30', grad: 'from-purple-600 to-violet-500' },
  { bg: 'bg-slate-500/15', color: 'text-slate-300', border: 'border-slate-500/30', grad: 'from-slate-600 to-gray-500' },
];

function getDeptStyle(departmanKod: number) {
  return deptColors[departmanKod % deptColors.length];
}

function getAvatarGrad(departmanKod: number) {
  return deptColors[departmanKod % deptColors.length].grad;
}

const PersonelPage: React.FC = () => {
  const [search, setSearch] = useState('');
  const [deptFilter, setDeptFilter] = useState<string>('all');
  const [page, setPage] = useState(1);
  const [view, setView] = useState<'grid' | 'table'>('grid');

  // Department list
  const departments = useMemo(() => {
    const map = new Map<string, number>();
    for (const p of allPersonel) {
      const d = p.departman || 'Tanımsız';
      map.set(d, (map.get(d) || 0) + 1);
    }
    return Array.from(map.entries())
      .sort((a, b) => b[1] - a[1])
      .map(([name, count]) => ({ name, count }));
  }, []);

  // Filtered data
  const filtered = useMemo(() => {
    let list = allPersonel;
    if (deptFilter !== 'all') {
      list = list.filter(p => (p.departman || 'Tanımsız') === deptFilter);
    }
    if (search.trim()) {
      const q = search.toLowerCase();
      list = list.filter(p =>
        `${p.no} ${p.ad} ${p.soyad} ${p.gorev} ${p.dogumYeri} ${p.birim} ${p.departman} ${p.telefon}`.toLowerCase().includes(q)
      );
    }
    return list;
  }, [search, deptFilter]);

  // Paginated
  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const paginated = useMemo(
    () => filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE),
    [filtered, currentPage]
  );

  const handlePageChange = useCallback((p: number) => {
    setPage(Math.max(1, Math.min(p, totalPages)));
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }, [totalPages]);

  // Stats
  const stats = useMemo(() => {
    const deptCount = new Set(allPersonel.map(p => p.departman || 'Tanımsız')).size;
    const birimCount = new Set(allPersonel.filter(p => p.birim).map(p => p.birim)).size;
    return { total: allPersonel.length, deptCount, birimCount };
  }, []);

  // Reset page on filter change
  const handleDeptChange = useCallback((val: string) => {
    setDeptFilter(val);
    setPage(1);
  }, []);
  const handleSearchChange = useCallback((val: string) => {
    setSearch(val);
    setPage(1);
  }, []);

  return (
    <div className="min-h-full p-6 space-y-5">
      {/* Header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div className="flex items-center gap-3">
          <div className="h-10 w-10 rounded-xl bg-gradient-to-br from-rose-600 to-orange-500 flex items-center justify-center shadow-lg shadow-rose-600/20">
            <Users2 className="w-5 h-5 text-white" />
          </div>
          <div>
            <h1 className="text-xl font-bold" style={{ color: 'var(--ms-text)' }}>Personel</h1>
            <p className="text-xs font-medium" style={{ color: 'var(--ms-text-muted)' }}>
              Tum departmanlar &middot; {stats.total} kisi
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2 flex-wrap">
          {[
            { label: 'Personel', value: stats.total, icon: <Users2 className="w-3 h-3" /> },
            { label: 'Departman', value: stats.deptCount, icon: <Building2 className="w-3 h-3" /> },
            { label: 'Birim', value: stats.birimCount, icon: <MapPin className="w-3 h-3" /> },
          ].map(s => (
            <div
              key={s.label}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-medium"
              style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text-muted)' }}
            >
              {s.icon}
              <span style={{ color: 'var(--ms-text)' }}>{s.value}</span>
              <span>{s.label}</span>
            </div>
          ))}
        </div>
      </div>

      {/* Filters row */}
      <div className="flex flex-col sm:flex-row gap-3">
        {/* Search */}
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4" style={{ color: 'var(--ms-text-muted)' }} />
          <input
            type="text"
            placeholder="Ad, soyad, gorev, birim, telefon..."
            value={search}
            onChange={e => handleSearchChange(e.target.value)}
            className="w-full pl-9 pr-3 py-2 rounded-lg text-sm outline-none transition-all focus:ring-2 focus:ring-violet-500/30"
            style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text)' }}
          />
        </div>

        {/* Department filter */}
        <div className="relative">
          <Filter className="absolute left-3 top-1/2 -translate-y-1/2 w-3.5 h-3.5" style={{ color: 'var(--ms-text-muted)' }} />
          <select
            value={deptFilter}
            onChange={e => handleDeptChange(e.target.value)}
            className="pl-9 pr-8 py-2 rounded-lg text-sm outline-none appearance-none cursor-pointer focus:ring-2 focus:ring-violet-500/30"
            style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text)' }}
          >
            <option value="all">Tum Departmanlar ({allPersonel.length})</option>
            {departments.map(d => (
              <option key={d.name} value={d.name}>{d.name} ({d.count})</option>
            ))}
          </select>
        </div>

        {/* View toggle */}
        <div className="flex items-center rounded-lg overflow-hidden" style={{ border: '1px solid var(--ms-border)' }}>
          <button
            onClick={() => setView('grid')}
            className={`px-3 py-2 transition-colors ${view === 'grid' ? 'bg-violet-600/20 text-violet-300' : ''}`}
            style={view !== 'grid' ? { color: 'var(--ms-text-muted)', background: 'var(--ms-bg-soft)' } : { background: undefined }}
          >
            <LayoutGrid className="w-4 h-4" />
          </button>
          <button
            onClick={() => setView('table')}
            className={`px-3 py-2 transition-colors ${view === 'table' ? 'bg-violet-600/20 text-violet-300' : ''}`}
            style={view !== 'table' ? { color: 'var(--ms-text-muted)', background: 'var(--ms-bg-soft)' } : { background: undefined }}
          >
            <List className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Result count */}
      <div className="text-[11px] font-medium" style={{ color: 'var(--ms-text-muted)' }}>
        {filtered.length} sonuc &middot; Sayfa {currentPage}/{totalPages}
      </div>

      {/* Grid view */}
      {view === 'grid' && (
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 gap-3">
          {paginated.map((p) => {
            const style = getDeptStyle(p.departmanKod);
            const grad = getAvatarGrad(p.departmanKod);
            return (
              <div
                key={p.no}
                className={`group relative rounded-xl p-4 transition-all duration-200 hover:scale-[1.02] hover:shadow-lg ring-1 ring-white/[0.03]`}
                style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' }}
              >
                <div className={`absolute top-0 left-4 right-4 h-[2px] rounded-b ${style.bg.replace('/15', '/40')}`} />

                <div className="flex items-start gap-3">
                  {/* Avatar */}
                  <div className={`h-10 w-10 rounded-xl bg-gradient-to-br ${grad} flex items-center justify-center shrink-0 shadow-md`}>
                    <span className="text-white font-bold text-xs">{getInitials(p.ad, p.soyad)}</span>
                  </div>

                  <div className="min-w-0 flex-1">
                    <div className="font-semibold text-[13px] leading-tight" style={{ color: 'var(--ms-text)' }}>
                      {formatName(p.ad)} {formatName(p.soyad)}
                    </div>
                    <div className={`inline-flex items-center gap-1 mt-1 px-2 py-0.5 rounded-md text-[10px] font-semibold ${style.bg} ${style.color} ${style.border} border`}>
                      {p.gorev}
                    </div>
                  </div>

                  {/* Personel No */}
                  <span className="text-[9px] font-mono px-1.5 py-0.5 rounded shrink-0" style={{ color: 'var(--ms-text-muted)', background: 'rgba(255,255,255,0.03)' }}>
                    {p.no}
                  </span>
                </div>

                <div className="mt-3 pt-2.5 space-y-1" style={{ borderTop: '1px solid var(--ms-border)' }}>
                  {p.departman && (
                    <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                      <Building2 className="w-3 h-3 shrink-0" />
                      <span className="truncate">{p.departman}</span>
                    </div>
                  )}
                  {p.birim && (
                    <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                      <MapPin className="w-3 h-3 shrink-0" />
                      <span className="truncate">{p.birim}</span>
                    </div>
                  )}
                  {p.dogumYeri && (
                    <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                      <Hash className="w-3 h-3 shrink-0" />
                      <span>{formatName(p.dogumYeri)}</span>
                      {p.dogumTarih && <span className="opacity-50">&middot; {p.dogumTarih}</span>}
                    </div>
                  )}
                  <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                    <Briefcase className="w-3 h-3 shrink-0" />
                    <span>
                      {p.girisTarih}
                      {p.girisTarih && (
                        <span className={`ml-1.5 px-1.5 py-0.5 rounded text-[9px] font-semibold ${style.bg} ${style.color}`}>
                          {calcTenureLabel(p.girisTarih)}
                        </span>
                      )}
                    </span>
                  </div>
                  {p.telefon && (
                    <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                      <Phone className="w-3 h-3 shrink-0" />
                      <span>{p.telefon}</span>
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Table view */}
      {view === 'table' && (
        <div className="rounded-xl overflow-hidden" style={{ border: '1px solid var(--ms-border)' }}>
          <div className="overflow-x-auto">
            <table className="w-full text-[12px]">
              <thead>
                <tr style={{ background: 'var(--ms-bg-soft)', borderBottom: '1px solid var(--ms-border)' }}>
                  {['No', 'Ad Soyad', 'Gorev', 'Departman', 'Birim', 'Dogum Yeri', 'Dogum Tarih', 'Giris Tarih', 'Kidem', 'Telefon'].map(h => (
                    <th key={h} className="px-3 py-2.5 text-left font-semibold uppercase tracking-wider text-[10px]" style={{ color: 'var(--ms-text-muted)' }}>
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {paginated.map((p) => {
                  const style = getDeptStyle(p.departmanKod);
                  return (
                    <tr
                      key={p.no}
                      className="transition-colors hover:bg-white/[0.02]"
                      style={{ borderBottom: '1px solid var(--ms-border)' }}
                    >
                      <td className="px-3 py-2 font-mono text-[10px]" style={{ color: 'var(--ms-text-muted)' }}>{p.no}</td>
                      <td className="px-3 py-2 font-medium whitespace-nowrap" style={{ color: 'var(--ms-text)' }}>
                        {formatName(p.ad)} {formatName(p.soyad)}
                      </td>
                      <td className="px-3 py-2">
                        <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium ${style.bg} ${style.color}`}>
                          {p.gorev}
                        </span>
                      </td>
                      <td className="px-3 py-2 max-w-[160px] truncate" style={{ color: 'var(--ms-text-muted)' }}>{p.departman}</td>
                      <td className="px-3 py-2 max-w-[160px] truncate" style={{ color: 'var(--ms-text-muted)' }}>{p.birim}</td>
                      <td className="px-3 py-2 whitespace-nowrap" style={{ color: 'var(--ms-text-muted)' }}>{formatName(p.dogumYeri)}</td>
                      <td className="px-3 py-2 whitespace-nowrap" style={{ color: 'var(--ms-text-muted)' }}>{p.dogumTarih}</td>
                      <td className="px-3 py-2 whitespace-nowrap" style={{ color: 'var(--ms-text-muted)' }}>{p.girisTarih}</td>
                      <td className="px-3 py-2 whitespace-nowrap">
                        {p.girisTarih && (
                          <span className={`px-1.5 py-0.5 rounded text-[9px] font-semibold ${style.bg} ${style.color}`}>
                            {calcTenureLabel(p.girisTarih)}
                          </span>
                        )}
                      </td>
                      <td className="px-3 py-2 font-mono whitespace-nowrap" style={{ color: 'var(--ms-text-muted)' }}>{p.telefon}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Empty state */}
      {filtered.length === 0 && (
        <div className="text-center py-16">
          <Users2 className="w-12 h-12 mx-auto mb-3" style={{ color: 'var(--ms-text-muted)', opacity: 0.3 }} />
          <p className="text-sm" style={{ color: 'var(--ms-text-muted)' }}>Sonuc bulunamadi</p>
        </div>
      )}

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-2 pt-2">
          <button
            onClick={() => handlePageChange(currentPage - 1)}
            disabled={currentPage <= 1}
            className="p-2 rounded-lg transition-colors disabled:opacity-30"
            style={{ color: 'var(--ms-text-muted)', background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' }}
          >
            <ChevronLeft className="w-4 h-4" />
          </button>

          {Array.from({ length: Math.min(totalPages, 7) }, (_, i) => {
            let pageNum: number;
            if (totalPages <= 7) {
              pageNum = i + 1;
            } else if (currentPage <= 4) {
              pageNum = i + 1;
            } else if (currentPage >= totalPages - 3) {
              pageNum = totalPages - 6 + i;
            } else {
              pageNum = currentPage - 3 + i;
            }
            return (
              <button
                key={pageNum}
                onClick={() => handlePageChange(pageNum)}
                className={`min-w-[32px] h-8 px-2 rounded-lg text-xs font-medium transition-colors ${
                  pageNum === currentPage ? 'bg-violet-600/20 text-violet-300' : ''
                }`}
                style={pageNum !== currentPage ? { color: 'var(--ms-text-muted)', background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' } : { border: '1px solid rgba(139,92,246,0.3)' }}
              >
                {pageNum}
              </button>
            );
          })}

          <button
            onClick={() => handlePageChange(currentPage + 1)}
            disabled={currentPage >= totalPages}
            className="p-2 rounded-lg transition-colors disabled:opacity-30"
            style={{ color: 'var(--ms-text-muted)', background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' }}
          >
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
      )}
    </div>
  );
};

export default PersonelPage;
