import { useEffect, useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { apiClient } from '../lib/apiClient';
import {
  Calendar,
  ChevronLeft,
  ChevronRight,
  Plus,
  Trash2,
  Users,
  Star,
} from 'lucide-react';

interface OnCallWorkday {
  id: number;
  userId: number;
  username: string;
  fullName: string;
  workDate: string;
  dayType: 'ResmiTatil' | 'HaftaSonu' | 'Mesai';
  notes?: string;
  createdAt: string;
}

interface UserSummary {
  userId: number;
  username: string;
  fullName: string;
  totalDays: number;
  resmiTatilCount: number;
  haftaSonuCount: number;
  mesaiCount: number;
  workDates: string[];
}

const DAY_TYPE_LABELS: Record<string, string> = {
  ResmiTatil: 'Resmi Tatil',
  HaftaSonu: 'Hafta Sonu',
  Mesai: 'Mesai',
};

const DAY_TYPE_COLORS: Record<string, string> = {
  ResmiTatil: 'bg-red-500',
  HaftaSonu: 'bg-amber-500',
  Mesai: 'bg-blue-500',
};

const DAY_TYPE_LIGHT: Record<string, string> = {
  ResmiTatil: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300',
  HaftaSonu: 'bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300',
  Mesai: 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300',
};

const MONTH_NAMES = [
  'Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran',
  'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık',
];
const DAY_NAMES = ['Pzt', 'Sal', 'Çar', 'Per', 'Cum', 'Cmt', 'Paz'];

export default function NobetciTakipPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const today = new Date();
  const [year, setYear] = useState(today.getFullYear());
  const [month, setMonth] = useState(today.getMonth() + 1);
  const [view, setView] = useState<'mine' | 'team'>('mine');

  const [workdays, setWorkdays] = useState<OnCallWorkday[]>([]);
  const [summaries, setSummaries] = useState<UserSummary[]>([]);
  const [loading, setLoading] = useState(false);

  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  const [addModal, setAddModal] = useState(false);
  const [addForm, setAddForm] = useState({ dayType: 'ResmiTatil', notes: '', username: '' });
  const [addLoading, setAddLoading] = useState(false);
  const [error, setError] = useState('');

  const [users, setUsers] = useState<{ id: number; username: string; fullName: string }[]>([]);

  useEffect(() => {
    loadData();
    if (isAdmin) loadUsers();
  }, [year, month, view]);

  async function loadData() {
    setLoading(true);
    try {
      const [workdayData, summaryData] = await Promise.all([
        apiClient.get<OnCallWorkday[]>(`/api/oncall-workdays?year=${year}&month=${month}`),
        apiClient.get<UserSummary[]>(`/api/oncall-workdays/summary?year=${year}&month=${month}`),
      ]);
      setWorkdays(workdayData ?? []);
      setSummaries(summaryData ?? []);
    } catch {
      setError('Veriler yüklenemedi.');
    } finally {
      setLoading(false);
    }
  }

  async function loadUsers() {
    try {
      const data = await apiClient.get<{ id: number; username: string; fullName: string }[]>('/api/users');
      setUsers(data ?? []);
    } catch { /* ignore */ }
  }

  function prevMonth() {
    if (month === 1) { setYear(y => y - 1); setMonth(12); }
    else setMonth(m => m - 1);
  }
  function nextMonth() {
    if (month === 12) { setYear(y => y + 1); setMonth(1); }
    else setMonth(m => m + 1);
  }

  // Calendar grid
  const firstDay = new Date(year, month - 1, 1);
  const daysInMonth = new Date(year, month, 0).getDate();
  // Haftanın başı Pazartesi (0=Pzt, 6=Paz)
  let startDow = firstDay.getDay() - 1;
  if (startDow < 0) startDow = 6;

  // Tarih → işaretlenmiş günler map'i
  const workdayMap = new Map<string, OnCallWorkday>();
  workdays.forEach(w => {
    const d = w.workDate.substring(0, 10);
    workdayMap.set(d, w);
  });

  // Türkiye resmi tatilleri (yıla göre - statik temel liste)
  const holidays2026 = ['2026-01-01', '2026-04-23', '2026-05-01', '2026-05-19', '2026-07-15', '2026-08-30', '2026-10-29'];
  const publicHolidays = new Set(holidays2026);

  function isWeekend(day: number): boolean {
    const d = new Date(year, month - 1, day);
    return d.getDay() === 0 || d.getDay() === 6;
  }

  function formatDateKey(day: number): string {
    return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
  }

  function openAddModal(day: number) {
    const key = formatDateKey(day);
    if (workdayMap.has(key) && !isAdmin) return; // Kendi günü zaten işaretliyse açma

    const isHol = publicHolidays.has(key);
    const isWknd = isWeekend(day);
    const defaultType = isHol ? 'ResmiTatil' : isWknd ? 'HaftaSonu' : 'Mesai';

    setSelectedDate(key);
    setAddForm({ dayType: defaultType, notes: '', username: '' });
    setAddModal(true);
    setError('');
  }

  async function submitAdd() {
    if (!selectedDate) return;
    setAddLoading(true);
    setError('');
    try {
      await apiClient.post<unknown>('/api/oncall-workdays', {
        workDate: selectedDate,
        dayType: addForm.dayType,
        notes: addForm.notes || undefined,
        username: isAdmin && addForm.username ? addForm.username : undefined,
      });
      setAddModal(false);
      await loadData();
    } catch (e: any) {
      setError(e?.message || 'Kayıt eklenemedi.');
    } finally {
      setAddLoading(false);
    }
  }

  async function deleteWorkday(id: number) {
    if (!confirm('Bu kayıt silinsin mi?')) return;
    try {
      await apiClient.delete(`/api/oncall-workdays/${id}`);
      await loadData();
    } catch {
      alert('Silinemedi.');
    }
  }

  const myWorkdays = workdays.filter(w => w.username === user?.username);
  const myEntry = selectedDate ? workdayMap.get(selectedDate) : undefined;

  return (
    <div className="p-4 md:p-6 max-w-5xl mx-auto">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 mb-6">
        <div>
          <h1 className="text-2xl font-bold text-slate-900 dark:text-white flex items-center gap-2">
            <Calendar className="h-6 w-6 text-sky-500" />
            Nöbetçi Takip
          </h1>
          <p className="text-sm text-slate-500 dark:text-slate-400 mt-0.5">
            Tatil ve mesai günlerinizi işaretleyin
          </p>
        </div>

        {isAdmin && (
          <div className="flex gap-2">
            <button
              onClick={() => setView('mine')}
              className={`px-3 py-1.5 rounded text-sm font-medium transition-colors ${
                view === 'mine'
                  ? 'bg-sky-600 text-white'
                  : 'bg-slate-100 text-slate-700 dark:bg-slate-700 dark:text-slate-200 hover:bg-slate-200 dark:hover:bg-slate-600'
              }`}
            >
              Benim
            </button>
            <button
              onClick={() => setView('team')}
              className={`px-3 py-1.5 rounded text-sm font-medium transition-colors flex items-center gap-1 ${
                view === 'team'
                  ? 'bg-sky-600 text-white'
                  : 'bg-slate-100 text-slate-700 dark:bg-slate-700 dark:text-slate-200 hover:bg-slate-200 dark:hover:bg-slate-600'
              }`}
            >
              <Users className="h-3.5 w-3.5" />
              Ekip Özeti
            </button>
          </div>
        )}
      </div>

      {/* Month Navigator */}
      <div className="flex items-center justify-between mb-4 bg-white dark:bg-slate-800 rounded-xl border border-slate-200 dark:border-slate-700 px-4 py-3">
        <button onClick={prevMonth} className="p-1.5 rounded hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors">
          <ChevronLeft className="h-5 w-5 text-slate-600 dark:text-slate-300" />
        </button>
        <span className="text-lg font-semibold text-slate-900 dark:text-white">
          {MONTH_NAMES[month - 1]} {year}
        </span>
        <button onClick={nextMonth} className="p-1.5 rounded hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors">
          <ChevronRight className="h-5 w-5 text-slate-600 dark:text-slate-300" />
        </button>
      </div>

      {view === 'mine' || !isAdmin ? (
        <>
          {/* Calendar */}
          <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-200 dark:border-slate-700 overflow-hidden mb-6">
            {/* Day headers */}
            <div className="grid grid-cols-7 border-b border-slate-200 dark:border-slate-700">
              {DAY_NAMES.map(d => (
                <div key={d} className="py-2 text-center text-xs font-semibold text-slate-500 dark:text-slate-400">
                  {d}
                </div>
              ))}
            </div>

            {/* Day cells */}
            <div className="grid grid-cols-7">
              {/* Empty cells before first day */}
              {Array.from({ length: startDow }).map((_, i) => (
                <div key={`empty-${i}`} className="h-16 border-b border-r border-slate-100 dark:border-slate-700/50" />
              ))}

              {Array.from({ length: daysInMonth }).map((_, i) => {
                const day = i + 1;
                const key = formatDateKey(day);
                const entry = workdayMap.get(key);
                const isMyEntry = entry?.username === user?.username;
                const isHol = publicHolidays.has(key);
                const isWknd = isWeekend(day);
                const isToday = key === today.toISOString().substring(0, 10);

                return (
                  <div
                    key={day}
                    onClick={() => openAddModal(day)}
                    className={`h-16 border-b border-r border-slate-100 dark:border-slate-700/50 p-1.5 cursor-pointer group relative transition-colors ${
                      isToday ? 'bg-sky-50 dark:bg-sky-900/20' : ''
                    } ${
                      !entry ? 'hover:bg-slate-50 dark:hover:bg-slate-700/40' : ''
                    } ${
                      isWknd && !entry ? 'bg-slate-50/60 dark:bg-slate-800/60' : ''
                    }`}
                  >
                    <div className={`flex items-center justify-between ${isToday ? 'font-bold text-sky-600 dark:text-sky-400' : 'text-slate-700 dark:text-slate-200'} text-sm`}>
                      <span>{day}</span>
                      {isHol && !entry && (
                        <span className="text-xs text-red-400 dark:text-red-400 leading-none">T</span>
                      )}
                    </div>

                    {entry && isMyEntry && (
                      <div className={`mt-0.5 rounded px-1 py-0.5 text-xs font-medium leading-tight ${DAY_TYPE_LIGHT[entry.dayType]}`}>
                        {DAY_TYPE_LABELS[entry.dayType]}
                      </div>
                    )}

                    {/* Other people's entries (admin only) - just a dot */}
                    {entry && !isMyEntry && isAdmin && (
                      <div className={`mt-1 w-2 h-2 rounded-full ${DAY_TYPE_COLORS[entry.dayType]}`} />
                    )}

                    {!entry && (
                      <div className="absolute inset-0 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity">
                        <Plus className="h-4 w-4 text-slate-400 dark:text-slate-500" />
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </div>

          {/* Legend */}
          <div className="flex flex-wrap gap-3 mb-6">
            {Object.entries(DAY_TYPE_LABELS).map(([k, v]) => (
              <div key={k} className="flex items-center gap-1.5 text-sm text-slate-600 dark:text-slate-300">
                <div className={`w-3 h-3 rounded-full ${DAY_TYPE_COLORS[k]}`} />
                {v}
              </div>
            ))}
            <div className="flex items-center gap-1.5 text-sm text-slate-600 dark:text-slate-300">
              <span className="text-xs font-semibold text-red-400">T</span>
              Resmi Tatil Günü
            </div>
          </div>

          {/* My records list */}
          <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-200 dark:border-slate-700">
            <div className="px-4 py-3 border-b border-slate-200 dark:border-slate-700">
              <h3 className="font-semibold text-slate-900 dark:text-white">
                {MONTH_NAMES[month - 1]} Çalışma Kayıtlarım
                <span className="ml-2 text-sm font-normal text-slate-500">({myWorkdays.length} gün)</span>
              </h3>
            </div>
            {myWorkdays.length === 0 ? (
              <div className="p-6 text-center text-slate-500 dark:text-slate-400 text-sm">
                Bu ay için kayıt yok. Takvimden gün seçin.
              </div>
            ) : (
              <ul className="divide-y divide-slate-100 dark:divide-slate-700">
                {myWorkdays.map(w => (
                  <li key={w.id} className="flex items-center justify-between px-4 py-3">
                    <div className="flex items-center gap-3">
                      <div className={`w-2.5 h-2.5 rounded-full ${DAY_TYPE_COLORS[w.dayType]}`} />
                      <div>
                        <div className="text-sm font-medium text-slate-900 dark:text-white">
                          {new Date(w.workDate).toLocaleDateString('tr-TR', { weekday: 'long', day: 'numeric', month: 'long' })}
                        </div>
                        {w.notes && <div className="text-xs text-slate-500 dark:text-slate-400">{w.notes}</div>}
                      </div>
                    </div>
                    <div className="flex items-center gap-2">
                      <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${DAY_TYPE_LIGHT[w.dayType]}`}>
                        {DAY_TYPE_LABELS[w.dayType]}
                      </span>
                      <button
                        onClick={() => deleteWorkday(w.id)}
                        className="p-1 rounded hover:bg-red-50 dark:hover:bg-red-900/20 text-slate-400 hover:text-red-500 transition-colors"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </>
      ) : (
        /* Team Summary (Admin) */
        <div className="space-y-4">
          {loading ? (
            <div className="text-center py-12 text-slate-500">Yükleniyor...</div>
          ) : summaries.length === 0 ? (
            <div className="text-center py-12 text-slate-500 dark:text-slate-400">
              Bu ay için ekip kaydı bulunmuyor.
            </div>
          ) : (
            summaries.map(s => (
              <div key={s.userId} className="bg-white dark:bg-slate-800 rounded-xl border border-slate-200 dark:border-slate-700 p-4">
                <div className="flex items-start justify-between mb-3">
                  <div className="flex items-center gap-2">
                    <Star className="h-4 w-4 text-amber-500" />
                    <span className="font-semibold text-slate-900 dark:text-white">{s.fullName}</span>
                    <span className="text-xs text-slate-500 dark:text-slate-400">@{s.username}</span>
                  </div>
                  <div className="flex gap-3 text-sm">
                    {s.resmiTatilCount > 0 && (
                      <span className="text-red-600 dark:text-red-400 font-medium">{s.resmiTatilCount} Resmi Tatil</span>
                    )}
                    {s.haftaSonuCount > 0 && (
                      <span className="text-amber-600 dark:text-amber-400 font-medium">{s.haftaSonuCount} Hafta Sonu</span>
                    )}
                    {s.mesaiCount > 0 && (
                      <span className="text-blue-600 dark:text-blue-400 font-medium">{s.mesaiCount} Mesai</span>
                    )}
                    <span className="text-slate-700 dark:text-slate-200 font-bold">= {s.totalDays} gün</span>
                  </div>
                </div>
                <div className="flex flex-wrap gap-1">
                  {s.workDates.map(d => {
                    const entry = workdays.find(w => w.username === s.username && w.workDate.startsWith(d.substring(0, 10)));
                    const dayType = entry?.dayType ?? 'Mesai';
                    return (
                      <span
                        key={d}
                        className={`text-xs px-2 py-0.5 rounded-full font-medium ${DAY_TYPE_LIGHT[dayType]}`}
                      >
                        {new Date(d).toLocaleDateString('tr-TR', { day: 'numeric', month: 'short', weekday: 'short' })}
                      </span>
                    );
                  })}
                </div>
              </div>
            ))
          )}
        </div>
      )}

      {/* Add Modal */}
      {addModal && selectedDate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="absolute inset-0 bg-black/50" onClick={() => setAddModal(false)} />
          <div className="relative bg-white dark:bg-slate-800 rounded-2xl shadow-2xl w-full max-w-sm p-6">
            {myEntry ? (
              <>
                <h3 className="text-lg font-semibold text-slate-900 dark:text-white mb-1">Çalışma Kaydı</h3>
                <p className="text-sm text-slate-500 dark:text-slate-400 mb-4">
                  {new Date(selectedDate).toLocaleDateString('tr-TR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}
                </p>
                <div className={`p-3 rounded-lg ${DAY_TYPE_LIGHT[myEntry.dayType]} mb-4`}>
                  <div className="font-medium">{DAY_TYPE_LABELS[myEntry.dayType]}</div>
                  {myEntry.notes && <div className="text-sm mt-0.5">{myEntry.notes}</div>}
                </div>
                <div className="flex gap-2">
                  <button onClick={() => setAddModal(false)} className="flex-1 px-4 py-2 border border-slate-300 dark:border-slate-600 rounded-lg text-sm text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors">
                    Kapat
                  </button>
                  <button onClick={() => deleteWorkday(myEntry.id).then(() => setAddModal(false))} className="flex-1 px-4 py-2 bg-red-600 text-white rounded-lg text-sm hover:bg-red-700 transition-colors flex items-center justify-center gap-1">
                    <Trash2 className="h-4 w-4" /> Sil
                  </button>
                </div>
              </>
            ) : (
              <>
                <h3 className="text-lg font-semibold text-slate-900 dark:text-white mb-1">Çalışma Günü Ekle</h3>
                <p className="text-sm text-slate-500 dark:text-slate-400 mb-4">
                  {new Date(selectedDate).toLocaleDateString('tr-TR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}
                </p>

                {error && <div className="mb-3 p-2 bg-red-50 dark:bg-red-900/20 text-red-600 dark:text-red-400 text-sm rounded-lg">{error}</div>}

                <div className="space-y-3">
                  {isAdmin && (
                    <div>
                      <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1">Personel (boş = kendin)</label>
                      <select
                        value={addForm.username}
                        onChange={e => setAddForm(f => ({ ...f, username: e.target.value }))}
                        className="w-full border border-slate-300 dark:border-slate-600 rounded-lg px-3 py-2 text-sm bg-white dark:bg-slate-700 text-slate-900 dark:text-white"
                      >
                        <option value="">Kendim</option>
                        {users.map(u => (
                          <option key={u.id} value={u.username}>{u.fullName} (@{u.username})</option>
                        ))}
                      </select>
                    </div>
                  )}

                  <div>
                    <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1">Gün Türü</label>
                    <div className="flex gap-2">
                      {Object.entries(DAY_TYPE_LABELS).map(([k, v]) => (
                        <button
                          key={k}
                          onClick={() => setAddForm(f => ({ ...f, dayType: k }))}
                          className={`flex-1 py-2 px-2 rounded-lg text-xs font-medium border transition-colors ${
                            addForm.dayType === k
                              ? `${DAY_TYPE_COLORS[k]} text-white border-transparent`
                              : 'border-slate-300 dark:border-slate-600 text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700'
                          }`}
                        >
                          {v}
                        </button>
                      ))}
                    </div>
                  </div>

                  <div>
                    <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1">Not (isteğe bağlı)</label>
                    <input
                      type="text"
                      value={addForm.notes}
                      onChange={e => setAddForm(f => ({ ...f, notes: e.target.value }))}
                      placeholder="Ör. Bayram nöbeti, açılış desteği..."
                      className="w-full border border-slate-300 dark:border-slate-600 rounded-lg px-3 py-2 text-sm bg-white dark:bg-slate-700 text-slate-900 dark:text-white placeholder-slate-400"
                    />
                  </div>
                </div>

                <div className="flex gap-2 mt-5">
                  <button onClick={() => setAddModal(false)} className="flex-1 px-4 py-2 border border-slate-300 dark:border-slate-600 rounded-lg text-sm text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors">
                    İptal
                  </button>
                  <button
                    onClick={submitAdd}
                    disabled={addLoading}
                    className="flex-1 px-4 py-2 bg-sky-600 text-white rounded-lg text-sm hover:bg-sky-700 disabled:opacity-50 transition-colors flex items-center justify-center gap-1"
                  >
                    {addLoading ? 'Kaydediliyor...' : <><Plus className="h-4 w-4" /> Ekle</>}
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
