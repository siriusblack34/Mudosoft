import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle,
  CalendarClock,
  CheckCircle2,
  Edit,
  Filter,
  Megaphone,
  Plus,
  Save,
  Search,
  Trash2,
  UserRound,
  X,
} from 'lucide-react';
import {
  AGENDA_UPDATED_EVENT,
  CATEGORY_OPTIONS,
  createAgendaItem,
  deleteAgendaItem,
  EMPTY_AGENDA_FORM,
  fetchAgendaItems,
  type AgendaCategory,
  type AgendaFormData,
  type AgendaItem,
  type AgendaPriority,
  type AgendaStatus,
  PRIORITY_OPTIONS,
  sortAgendaItems,
  STATUS_OPTIONS,
  updateAgendaItem,
} from '../lib/agendaStore';

function formatDate(value: string) {
  if (!value) return '-';

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '-';

  return date.toLocaleDateString('tr-TR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
}

function formatDateTime(value: string) {
  if (!value) return '-';

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '-';

  return date.toLocaleString('tr-TR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function getStatusTone(status: AgendaStatus) {
  if (status === 'Tamamlandi') {
    return 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30';
  }

  if (status === 'Planlandi') {
    return 'bg-sky-500/15 text-sky-300 border-sky-500/30';
  }

  return 'bg-amber-500/15 text-amber-300 border-amber-500/30';
}

function getPriorityTone(priority: AgendaPriority) {
  if (priority === 'Yuksek') {
    return 'bg-rose-500/15 text-rose-300 border-rose-500/30';
  }

  if (priority === 'Dusuk') {
    return 'bg-slate-500/15 text-slate-300 border-slate-500/30';
  }

  return 'bg-violet-500/15 text-violet-300 border-violet-500/30';
}

function getCategoryTone(category: AgendaCategory) {
  if (category === 'Guvenlik') return 'from-rose-600 to-orange-500';
  if (category === 'Altyapi') return 'from-cyan-600 to-sky-500';
  if (category === 'Magaza Talebi') return 'from-emerald-600 to-teal-500';
  if (category === 'Bakim') return 'from-amber-600 to-yellow-500';
  if (category === 'Proje') return 'from-fuchsia-600 to-violet-500';
  return 'from-indigo-600 to-violet-500';
}

export default function GundemPage() {
  const [items, setItems] = useState<AgendaItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [agendaError, setAgendaError] = useState<string | null>(null);
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | AgendaStatus>('all');
  const [categoryFilter, setCategoryFilter] = useState<'all' | AgendaCategory>('all');
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingItem, setEditingItem] = useState<AgendaItem | null>(null);
  const [formData, setFormData] = useState<AgendaFormData>(EMPTY_AGENDA_FORM);

  const loadAgenda = useCallback(async () => {
    try {
      setAgendaError(null);
      setItems(sortAgendaItems(await fetchAgendaItems()));
    } catch (error) {
      setAgendaError(error instanceof Error ? error.message : 'Gundem yuklenemedi.');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadAgenda();
    const syncAgenda = () => { void loadAgenda(); };
    window.addEventListener(AGENDA_UPDATED_EVENT, syncAgenda);
    return () => {
      window.removeEventListener(AGENDA_UPDATED_EVENT, syncAgenda);
    };
  }, [loadAgenda]);

  const filteredItems = useMemo(() => {
    return items.filter((item) => {
      const search = searchTerm.trim().toLowerCase();
      const matchesSearch = !search || `${item.title} ${item.content} ${item.createdBy}`.toLowerCase().includes(search);
      const matchesStatus = statusFilter === 'all' || item.status === statusFilter;
      const matchesCategory = categoryFilter === 'all' || item.category === categoryFilter;
      return matchesSearch && matchesStatus && matchesCategory;
    });
  }, [items, searchTerm, statusFilter, categoryFilter]);

  const stats = useMemo(() => ({
    total: items.length,
    active: items.filter(item => item.status !== 'Tamamlandi').length,
    urgent: items.filter(item => item.priority === 'Yuksek' && item.status !== 'Tamamlandi').length,
    done: items.filter(item => item.status === 'Tamamlandi').length,
  }), [items]);

  const handleAddNew = () => {
    setEditingItem(null);
    setFormData(EMPTY_AGENDA_FORM);
    setIsModalOpen(true);
  };

  const handleEdit = (item: AgendaItem) => {
    setEditingItem(item);
    setFormData({
      title: item.title,
      content: item.content,
      status: item.status,
      priority: item.priority,
      category: item.category,
      dueDate: item.dueDate,
    });
    setIsModalOpen(true);
  };

  const handleCloseModal = () => {
    setIsModalOpen(false);
    setEditingItem(null);
    setFormData(EMPTY_AGENDA_FORM);
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();

    setIsSaving(true);
    try {
      if (editingItem) await updateAgendaItem(editingItem.id, formData);
      else await createAgendaItem(formData);
      await loadAgenda();
      handleCloseModal();
    } catch (error) {
      console.error('Gundem kaydedilemedi:', error);
      window.alert(error instanceof Error ? error.message : 'Gundem kaydedilemedi.');
    } finally {
      setIsSaving(false);
    }
  };

  const handleDelete = async (id: string) => {
    if (!window.confirm('Bu gundem maddesini silmek istiyor musunuz?')) return;
    try {
      await deleteAgendaItem(id);
      await loadAgenda();
    } catch (error) {
      console.error('Gundem silinemedi:', error);
      window.alert(error instanceof Error ? error.message : 'Gundem silinemedi.');
    }
  };

  const handleStatusChange = async (item: AgendaItem, status: AgendaStatus) => {
    try {
      await updateAgendaItem(item.id, {
        title: item.title,
        content: item.content,
        status,
        priority: item.priority,
        category: item.category,
        dueDate: item.dueDate,
      });
      await loadAgenda();
    } catch (error) {
      console.error('Gundem guncellenemedi:', error);
      window.alert(error instanceof Error ? error.message : 'Gundem guncellenemedi.');
    }
  };

  return (
    <div className="min-h-full p-6 space-y-5">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div className="flex items-start gap-3">
          <div className="h-11 w-11 rounded-2xl bg-gradient-to-br from-orange-500 to-rose-600 flex items-center justify-center shadow-lg shadow-rose-600/20 shrink-0">
            <Megaphone className="w-5 h-5 text-white" />
          </div>
          <div>
            <h1 className="text-xl font-bold" style={{ color: 'var(--ms-text)' }}>Havadisler / IT Gundemi</h1>
            <p className="text-xs font-medium" style={{ color: 'var(--ms-text-muted)' }}>
              BT ekibinin duyuru, aksiyon ve takip basliklarini tek ekranda toplayin.
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2 flex-wrap">
          {[
            { label: 'Toplam', value: stats.total },
            { label: 'Aktif', value: stats.active },
            { label: 'Acil', value: stats.urgent },
            { label: 'Tamamlandi', value: stats.done },
          ].map((stat) => (
            <div
              key={stat.label}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-medium"
              style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text-muted)' }}
            >
              <span className="text-[13px]" style={{ color: 'var(--ms-text)' }}>{stat.value}</span>
              <span>{stat.label}</span>
            </div>
          ))}
        </div>
      </div>

      <div
        className="rounded-2xl p-4 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between"
        style={{ background: 'linear-gradient(135deg, rgba(249,115,22,0.10), rgba(168,85,247,0.08))', border: '1px solid var(--ms-border)' }}
      >
        <div className="space-y-1">
          <div className="text-sm font-semibold" style={{ color: 'var(--ms-text)' }}>
            Haftalik BT akislarini burada tutabilirsiniz
          </div>
          <div className="text-xs" style={{ color: 'var(--ms-text-muted)' }}>
            Kayitlar veritabaninda ortak saklanir; admin ve teknisyenler ayni gundemi gorur.
          </div>
        </div>

        <button
          onClick={handleAddNew}
          className="inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-orange-900/20 transition-all hover:scale-[1.01]"
          style={{ background: 'linear-gradient(135deg, #f97316, #ea580c)' }}
        >
          <Plus className="w-4 h-4" />
          Yeni Gundem Maddesi
        </button>
      </div>

      <div className="flex flex-col gap-3 xl:flex-row">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4" style={{ color: 'var(--ms-text-muted)' }} />
          <input
            type="text"
            placeholder="Baslik, icerik veya olusturan kisi ara..."
            value={searchTerm}
            onChange={(event) => setSearchTerm(event.target.value)}
            className="w-full pl-10 pr-3 py-2.5 rounded-xl text-sm outline-none transition-all focus:ring-2 focus:ring-orange-500/30"
            style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text)' }}
          />
        </div>

        <div className="flex gap-3 flex-col sm:flex-row xl:w-auto">
          <div className="relative">
            <Filter className="absolute left-3 top-1/2 -translate-y-1/2 w-3.5 h-3.5" style={{ color: 'var(--ms-text-muted)' }} />
            <select
              value={statusFilter}
              onChange={(event) => setStatusFilter(event.target.value as 'all' | AgendaStatus)}
              className="w-full sm:w-44 pl-9 pr-8 py-2.5 rounded-xl text-sm outline-none appearance-none"
              style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text)' }}
            >
              <option value="all">Tum Durumlar</option>
              {STATUS_OPTIONS.map((status) => (
                <option key={status} value={status}>{status}</option>
              ))}
            </select>
          </div>

          <div className="relative">
            <Filter className="absolute left-3 top-1/2 -translate-y-1/2 w-3.5 h-3.5" style={{ color: 'var(--ms-text-muted)' }} />
            <select
              value={categoryFilter}
              onChange={(event) => setCategoryFilter(event.target.value as 'all' | AgendaCategory)}
              className="w-full sm:w-48 pl-9 pr-8 py-2.5 rounded-xl text-sm outline-none appearance-none"
              style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text)' }}
            >
              <option value="all">Tum Kategoriler</option>
              {CATEGORY_OPTIONS.map((category) => (
                <option key={category} value={category}>{category}</option>
              ))}
            </select>
          </div>
        </div>
      </div>

      {agendaError && (
        <div
          className="rounded-xl px-4 py-3 text-sm"
          style={{ background: 'rgba(244,63,94,0.10)', border: '1px solid rgba(244,63,94,0.20)', color: '#fda4af' }}
        >
          {agendaError}
        </div>
      )}

      {isLoading ? (
        <div
          className="rounded-2xl py-16 px-6 text-center"
          style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text-muted)' }}
        >
          Gundem yukleniyor...
        </div>
      ) : filteredItems.length === 0 ? (
        <div
          className="rounded-2xl py-20 px-6 text-center"
          style={{ background: 'var(--ms-bg-soft)', border: '1px dashed var(--ms-border)' }}
        >
          <Megaphone className="w-12 h-12 mx-auto mb-4" style={{ color: 'var(--ms-text-muted)', opacity: 0.35 }} />
          <div className="text-base font-semibold mb-1" style={{ color: 'var(--ms-text)' }}>
            Henuz gundem maddesi yok
          </div>
          <p className="text-sm mb-4" style={{ color: 'var(--ms-text-muted)' }}>
            Ilk BT basligini ekleyip ekip icin ortak bir takip alani olusturabilirsiniz.
          </p>
          <button
            onClick={handleAddNew}
            className="inline-flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-semibold text-white"
            style={{ background: 'linear-gradient(135deg, #f97316, #ea580c)' }}
          >
            <Plus className="w-4 h-4" />
            Ilk Maddeyi Ekle
          </button>
        </div>
      ) : (
        <div className="grid grid-cols-1 xl:grid-cols-2 2xl:grid-cols-3 gap-4">
          {filteredItems.map((item) => (
            <div
              key={item.id}
              className="rounded-2xl p-4 transition-all duration-200 hover:-translate-y-0.5"
              style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' }}
            >
              <div className="flex items-start gap-3">
                <div className={`h-11 w-11 rounded-2xl bg-gradient-to-br ${getCategoryTone(item.category)} flex items-center justify-center shrink-0 shadow-md`}>
                  <Megaphone className="w-4 h-4 text-white" />
                </div>

                <div className="min-w-0 flex-1">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <h3 className="text-[15px] font-semibold leading-tight" style={{ color: 'var(--ms-text)' }}>
                        {item.title}
                      </h3>
                      <div className="mt-2 flex flex-wrap gap-2">
                        <span className={`inline-flex items-center rounded-full border px-2.5 py-1 text-[10px] font-semibold ${getStatusTone(item.status)}`}>
                          {item.status}
                        </span>
                        <span className={`inline-flex items-center rounded-full border px-2.5 py-1 text-[10px] font-semibold ${getPriorityTone(item.priority)}`}>
                          {item.priority} oncelik
                        </span>
                        <span
                          className="inline-flex items-center rounded-full border px-2.5 py-1 text-[10px] font-semibold"
                          style={{ borderColor: 'var(--ms-border)', color: 'var(--ms-text-muted)' }}
                        >
                          {item.category}
                        </span>
                      </div>
                    </div>

                    <div className="flex items-center gap-1 shrink-0">
                      {item.status !== 'Tamamlandi' && (
                        <button
                          onClick={() => handleStatusChange(item, 'Tamamlandi')}
                          className="p-2 rounded-lg transition-colors"
                          style={{ color: '#86efac', background: 'rgba(34,197,94,0.08)' }}
                          title="Tamamlandi olarak isaretle"
                        >
                          <CheckCircle2 className="w-4 h-4" />
                        </button>
                      )}
                      <button
                        onClick={() => handleEdit(item)}
                        className="p-2 rounded-lg transition-colors"
                        style={{ color: 'var(--ms-text-muted)', background: 'rgba(255,255,255,0.03)' }}
                        title="Duzenle"
                      >
                        <Edit className="w-4 h-4" />
                      </button>
                      <button
                        onClick={() => handleDelete(item.id)}
                        className="p-2 rounded-lg transition-colors"
                        style={{ color: '#fda4af', background: 'rgba(244,63,94,0.08)' }}
                        title="Sil"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  </div>
                </div>
              </div>

              <p className="mt-4 whitespace-pre-wrap text-sm leading-6" style={{ color: 'var(--ms-text-muted)' }}>
                {item.content || 'Aciklama girilmedi.'}
              </p>

              <div className="mt-4 grid grid-cols-1 sm:grid-cols-2 gap-2">
                <div
                  className="rounded-xl px-3 py-2 text-[11px] flex items-center gap-2"
                  style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid var(--ms-border)', color: 'var(--ms-text-muted)' }}
                >
                  <UserRound className="w-3.5 h-3.5 shrink-0" />
                  <span className="truncate">{item.createdBy}</span>
                </div>
                <div
                  className="rounded-xl px-3 py-2 text-[11px] flex items-center gap-2"
                  style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid var(--ms-border)', color: 'var(--ms-text-muted)' }}
                >
                  <CalendarClock className="w-3.5 h-3.5 shrink-0" />
                  <span className="truncate">Termin: {formatDate(item.dueDate)}</span>
                </div>
              </div>

              <div className="mt-4 pt-3 flex items-center justify-between text-[11px]" style={{ borderTop: '1px solid var(--ms-border)', color: 'var(--ms-text-muted)' }}>
                <span>Olusturma: {formatDateTime(item.createdAt)}</span>
                <span>Guncelleme: {formatDateTime(item.updatedAt)}</span>
              </div>
            </div>
          ))}
        </div>
      )}

      {items.length > 0 && (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
          <div
            className="rounded-2xl p-4"
            style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' }}
          >
            <div className="flex items-center gap-2 text-sm font-semibold mb-2" style={{ color: 'var(--ms-text)' }}>
              <AlertTriangle className="w-4 h-4 text-rose-300" />
              Acil takip
            </div>
            <div className="text-xs leading-6" style={{ color: 'var(--ms-text-muted)' }}>
              Yuksek oncelikli ve tamamlanmamis basliklar otomatik olarak ust tarafta kalir.
            </div>
          </div>

          <div
            className="rounded-2xl p-4"
            style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' }}
          >
            <div className="flex items-center gap-2 text-sm font-semibold mb-2" style={{ color: 'var(--ms-text)' }}>
              <Megaphone className="w-4 h-4 text-orange-300" />
              Havadis akisi
            </div>
            <div className="text-xs leading-6" style={{ color: 'var(--ms-text-muted)' }}>
              Duyuru, bakim veya proje gelismelerini ayni ekranda kategorilere ayirabilirsiniz.
            </div>
          </div>

          <div
            className="rounded-2xl p-4"
            style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' }}
          >
            <div className="flex items-center gap-2 text-sm font-semibold mb-2" style={{ color: 'var(--ms-text)' }}>
              <CheckCircle2 className="w-4 h-4 text-emerald-300" />
              Kapanan isler
            </div>
            <div className="text-xs leading-6" style={{ color: 'var(--ms-text-muted)' }}>
              Kart ustundeki tik ile tamamlanan maddeleri hizlica kapatabilirsiniz.
            </div>
          </div>
        </div>
      )}

      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/65 p-4 backdrop-blur-sm">
          <div
            className="relative w-full max-w-2xl rounded-3xl p-6"
            style={{ background: '#0f172a', border: '1px solid rgba(148,163,184,0.18)' }}
          >
            <button
              onClick={handleCloseModal}
              className="absolute right-4 top-4 p-2 rounded-lg transition-colors"
              style={{ color: 'var(--ms-text-muted)' }}
            >
              <X className="w-5 h-5" />
            </button>

            <div className="pr-10">
              <h2 className="text-xl font-bold" style={{ color: 'var(--ms-text)' }}>
                {editingItem ? 'Gundem Maddesini Duzenle' : 'Yeni Gundem Maddesi'}
              </h2>
              <p className="mt-1 text-sm" style={{ color: 'var(--ms-text-muted)' }}>
                BT ekibi icin takip edilecek konu, duyuru veya aksiyonu buradan kaydedin.
              </p>
            </div>

            <form onSubmit={handleSubmit} className="mt-6 space-y-4">
              <div>
                <label className="block text-sm font-medium mb-2" style={{ color: 'var(--ms-text-muted)' }}>
                  Baslik
                </label>
                <input
                  type="text"
                  required
                  value={formData.title}
                  onChange={(event) => setFormData(current => ({ ...current, title: event.target.value }))}
                  className="w-full rounded-xl px-4 py-3 text-sm outline-none focus:ring-2 focus:ring-orange-500/30"
                  style={{ background: 'rgba(15,23,42,0.72)', border: '1px solid rgba(148,163,184,0.18)', color: 'var(--ms-text)' }}
                  placeholder="Ornek: 212 magazasi internet kesintisi takibi"
                />
              </div>

              <div>
                <label className="block text-sm font-medium mb-2" style={{ color: 'var(--ms-text-muted)' }}>
                  Aciklama
                </label>
                <textarea
                  rows={6}
                  value={formData.content}
                  onChange={(event) => setFormData(current => ({ ...current, content: event.target.value }))}
                  className="w-full resize-none rounded-xl px-4 py-3 text-sm outline-none focus:ring-2 focus:ring-orange-500/30"
                  style={{ background: 'rgba(15,23,42,0.72)', border: '1px solid rgba(148,163,184,0.18)', color: 'var(--ms-text)' }}
                  placeholder="Detay, sorumlu ekip, beklenen aksiyon veya notlar..."
                />
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-3">
                <div>
                  <label className="block text-sm font-medium mb-2" style={{ color: 'var(--ms-text-muted)' }}>
                    Durum
                  </label>
                  <select
                    value={formData.status}
                    onChange={(event) => setFormData(current => ({ ...current, status: event.target.value as AgendaStatus }))}
                    className="w-full rounded-xl px-3 py-3 text-sm outline-none"
                    style={{ background: 'rgba(15,23,42,0.72)', border: '1px solid rgba(148,163,184,0.18)', color: 'var(--ms-text)' }}
                  >
                    {STATUS_OPTIONS.map((status) => (
                      <option key={status} value={status}>{status}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium mb-2" style={{ color: 'var(--ms-text-muted)' }}>
                    Oncelik
                  </label>
                  <select
                    value={formData.priority}
                    onChange={(event) => setFormData(current => ({ ...current, priority: event.target.value as AgendaPriority }))}
                    className="w-full rounded-xl px-3 py-3 text-sm outline-none"
                    style={{ background: 'rgba(15,23,42,0.72)', border: '1px solid rgba(148,163,184,0.18)', color: 'var(--ms-text)' }}
                  >
                    {PRIORITY_OPTIONS.map((priority) => (
                      <option key={priority} value={priority}>{priority}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium mb-2" style={{ color: 'var(--ms-text-muted)' }}>
                    Kategori
                  </label>
                  <select
                    value={formData.category}
                    onChange={(event) => setFormData(current => ({ ...current, category: event.target.value as AgendaCategory }))}
                    className="w-full rounded-xl px-3 py-3 text-sm outline-none"
                    style={{ background: 'rgba(15,23,42,0.72)', border: '1px solid rgba(148,163,184,0.18)', color: 'var(--ms-text)' }}
                  >
                    {CATEGORY_OPTIONS.map((category) => (
                      <option key={category} value={category}>{category}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium mb-2" style={{ color: 'var(--ms-text-muted)' }}>
                    Termin
                  </label>
                  <input
                    type="date"
                    value={formData.dueDate}
                    onChange={(event) => setFormData(current => ({ ...current, dueDate: event.target.value }))}
                    className="w-full rounded-xl px-3 py-3 text-sm outline-none"
                    style={{ background: 'rgba(15,23,42,0.72)', border: '1px solid rgba(148,163,184,0.18)', color: 'var(--ms-text)' }}
                  />
                </div>
              </div>

              <div className="flex items-center justify-end gap-3 pt-2">
                <button
                  type="button"
                  onClick={handleCloseModal}
                  className="rounded-xl px-4 py-2.5 text-sm font-medium transition-colors"
                  style={{ color: 'var(--ms-text-muted)' }}
                >
                  Iptal
                </button>
                <button
                  type="submit"
                  disabled={isSaving}
                  className="inline-flex items-center gap-2 rounded-xl px-5 py-2.5 text-sm font-semibold text-white"
                  style={{ background: 'linear-gradient(135deg, #f97316, #ea580c)', opacity: isSaving ? 0.65 : 1 }}
                >
                  <Save className="w-4 h-4" />
                  {isSaving ? 'Kaydediliyor...' : 'Kaydet'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
