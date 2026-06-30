import React, { useEffect, useState } from 'react';
import { apiClient } from '../lib/apiClient';
import { useAuth } from '../contexts/AuthContext';
import {
  Users, Store, Plus, Trash2, KeyRound, Edit3, Save, X,
  Shield, Wrench, Clock, CheckCircle, XCircle, Search, Lock, Menu, Eye, EyeOff,
  Bell, Mail, Send, Wifi, RefreshCw, Layers, SlidersHorizontal
} from 'lucide-react';
import { getConfigurableCatalog, MenuCatalogItem } from '../lib/menuCatalog';

// ─── Types ───
interface UserItem {
  id: number; username: string; fullName: string; role: string;
  isActive: boolean; createdAt: string; lastLoginAt: string | null;
  email: string | null;
  menuProfileId: number | null;
  menuProfileName: string | null;
  menuGrants: string[];
  menuDenials: string[];
}

interface MenuProfileDto {
  id: number; name: string; description: string | null; isSystem: boolean;
  allowAllByDefault: boolean; allowedMenus: string[]; hiddenMenus: string[];
  userCount: number; updatedAt: string;
}
interface LoginHistoryItem {
  id: number; username: string; loginAt: string; ipAddress: string | null; success: boolean;
}
interface StoreDeviceItem {
  deviceId: string; storeCode: number; storeName: string; deviceType: string;
  deviceName: string; calculatedIpAddress: string; dbConnectionString: string;
  isTemporarilyClosed: boolean; temporaryCloseReason: string | null; lastSeen: string | null;
}

type Tab = 'password' | 'users' | 'stores' | 'logins' | 'menus' | 'alarms';

const SettingsPage: React.FC = () => {
  const { isAdmin } = useAuth();
  const [tab, setTab] = useState<Tab>('password');

  const allTabs = [
    { id: 'password' as Tab, label: 'Şifre Değiştir', icon: Lock, adminOnly: false },
    { id: 'users' as Tab, label: 'Kullanıcı Yönetimi', icon: Users, adminOnly: true },
    { id: 'stores' as Tab, label: 'Mağaza / Kasa', icon: Store, adminOnly: true },
    { id: 'menus' as Tab, label: 'Menü Yönetimi', icon: Menu, adminOnly: true },
    { id: 'logins' as Tab, label: 'Giriş Geçmişi', icon: Clock, adminOnly: false },
    { id: 'alarms' as Tab, label: 'E-posta Alarmları', icon: Bell, adminOnly: true },
  ];

  const tabs = allTabs.filter(t => !t.adminOnly || isAdmin);

  return (
    <div className="space-y-4">
      {/* Tabs */}
      <div className="flex gap-1 bg-ms-bg-soft p-1 rounded-xl border border-ms-border w-fit">
        {tabs.map(t => (
          <button
            key={t.id}
            onClick={() => setTab(t.id)}
            className={`flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-all ${
              tab === t.id
                ? 'bg-violet-600 text-white shadow-lg shadow-violet-600/20'
                : 'text-ms-text-muted hover:text-ms-text hover:bg-ms-border/50'
            }`}
          >
            <t.icon className="w-4 h-4" />
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'password' && <ChangePasswordPanel />}
      {tab === 'users' && isAdmin && <UserManagement />}
      {tab === 'stores' && isAdmin && <StoreManagement />}
      {tab === 'menus' && isAdmin && <MenuManagementPanel />}
      {tab === 'logins' && <LoginHistoryPanel />}
      {tab === 'alarms' && isAdmin && <EmailAlarmPanel />}
    </div>
  );
};

// ════════════════════════════════════════
// USER MANAGEMENT
// ════════════════════════════════════════
const UserManagement: React.FC = () => {
  const [users, setUsers] = useState<UserItem[]>([]);
  const [profiles, setProfiles] = useState<MenuProfileDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [resetId, setResetId] = useState<number | null>(null);
  const [overrideUser, setOverrideUser] = useState<UserItem | null>(null);
  const [form, setForm] = useState({ username: '', password: '', fullName: '', role: 'Teknisyen', email: '', menuProfileId: '' });
  const [editForm, setEditForm] = useState({ fullName: '', role: '', isActive: true, email: '', menuProfileId: -1 });
  const [newPass, setNewPass] = useState('');
  const [msg, setMsg] = useState('');
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [bulkProfileId, setBulkProfileId] = useState<number>(-1);
  const [bulkBusy, setBulkBusy] = useState(false);

  const load = async () => {
    try {
      const [u, p] = await Promise.all([
        apiClient.get<UserItem[]>('/api/users'),
        apiClient.get<MenuProfileDto[]>('/api/menu-profiles').catch(() => [] as MenuProfileDto[]),
      ]);
      setUsers(u);
      setProfiles(p);
    } catch { }
    setLoading(false);
  };

  useEffect(() => { load(); }, []);

  // Bir kullanıcının etkin profilini bulur (atanmamışsa sistem Teknisyen profili).
  const profileForUser = (u: UserItem): MenuProfileDto | null =>
    profiles.find(p => p.id === u.menuProfileId)
    ?? profiles.find(p => p.isSystem && p.name === 'Teknisyen')
    ?? null;

  const handleCreate = async () => {
    try {
      const payload: any = { username: form.username, password: form.password, fullName: form.fullName, role: form.role, email: form.email };
      if (form.role === 'Teknisyen' && form.menuProfileId) payload.menuProfileId = Number(form.menuProfileId);
      await apiClient.post('/api/users', payload);
      setShowCreate(false);
      setForm({ username: '', password: '', fullName: '', role: 'Teknisyen', email: '', menuProfileId: '' });
      load();
    } catch (e: any) { setMsg(e.message); }
  };

  const handleEdit = async (id: number) => {
    try {
      // menuProfileId: -1 = varsayılana dön (profili kaldır), >0 = ata
      await apiClient.put(`/api/users/${id}`, editForm);
      setEditId(null);
      load();
    } catch (e: any) { setMsg(e.message); }
  };

  const handleResetPassword = async (id: number) => {
    try {
      await apiClient.post(`/api/users/${id}/reset-password`, { newPassword: newPass });
      setResetId(null);
      setNewPass('');
      setMsg('Şifre sıfırlandı');
      setTimeout(() => setMsg(''), 3000);
    } catch (e: any) { setMsg(e.message); }
  };

  const handleDelete = async (id: number, username: string) => {
    if (!confirm(`${username} kullanıcısını silmek istediğinize emin misiniz?`)) return;
    try {
      await apiClient.delete(`/api/users/${id}`);
      load();
    } catch (e: any) { setMsg(e.message); }
  };

  // ── Çoklu seçim + toplu profil atama ──
  const toggleSelect = (id: number) => setSelectedIds(prev => {
    const n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n;
  });
  const allSelected = users.length > 0 && users.every(u => selectedIds.has(u.id));
  const toggleSelectAll = () => setSelectedIds(allSelected ? new Set() : new Set(users.map(u => u.id)));

  const applyBulkProfile = async () => {
    // Profil yalnızca teknisyenler için anlamlı; admin'ler atlanır.
    const targets = users.filter(u => selectedIds.has(u.id) && u.role !== 'Admin');
    if (targets.length === 0) { setMsg('Profil atanacak teknisyen seçilmedi (admin kullanıcılar atlanır).'); return; }
    setBulkBusy(true); setMsg('');
    let ok = 0, fail = 0;
    for (const u of targets) {
      try { await apiClient.put(`/api/users/${u.id}`, { menuProfileId: bulkProfileId }); ok++; }
      catch { fail++; }
    }
    const pname = bulkProfileId <= 0 ? 'Varsayılan (Teknisyen)' : (profiles.find(p => p.id === bulkProfileId)?.name ?? 'profil');
    setBulkBusy(false);
    setSelectedIds(new Set());
    setMsg(`${ok} kullanıcıya "${pname}" profili atandı${fail ? `, ${fail} hata` : ''}.`);
    setTimeout(() => setMsg(''), 4000);
    load();
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-bold text-ms-text">Kullanıcılar</h2>
        <button onClick={() => setShowCreate(true)} className="btn-primary text-sm">
          <Plus className="w-4 h-4" /> Yeni Kullanıcı
        </button>
      </div>

      {msg && (
        <div className="text-xs text-amber-400 bg-amber-500/10 border border-amber-500/20 rounded-lg px-3 py-2">
          {msg}
        </div>
      )}

      {/* Toplu işlem çubuğu — seçim varken */}
      {selectedIds.size > 0 && (
        <div className="flex flex-wrap items-center gap-2 bg-violet-500/10 border border-violet-500/20 rounded-xl px-4 py-2.5">
          <span className="text-sm font-semibold text-violet-300">{selectedIds.size} seçili</span>
          <span className="text-xs text-ms-text-muted">→ Menü profili ata:</span>
          <select value={bulkProfileId} onChange={e => setBulkProfileId(Number(e.target.value))} className="text-sm">
            <option value={-1}>Varsayılan (Teknisyen)</option>
            {profiles.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
          <button onClick={applyBulkProfile} disabled={bulkBusy} className="btn-primary text-xs disabled:opacity-40">
            <Layers className="w-3.5 h-3.5" /> {bulkBusy ? 'Atanıyor...' : 'Seçilenlere Uygula'}
          </button>
          <button onClick={() => setSelectedIds(new Set())} className="btn-secondary text-xs">
            <X className="w-3.5 h-3.5" /> Seçimi Temizle
          </button>
          <span className="text-[10px] text-ms-text-muted/70">(admin kullanıcılar atlanır)</span>
        </div>
      )}

      {/* Create Modal */}
      {showCreate && (
        <div className="bg-ms-bg-soft border border-ms-border rounded-xl p-4 space-y-3">
          <h3 className="text-sm font-bold text-ms-text">Yeni Kullanıcı Oluştur</h3>
          <div className="grid grid-cols-2 gap-3">
            <input placeholder="Kullanıcı adı" value={form.username}
              onChange={e => setForm({ ...form, username: e.target.value })} className="text-sm" />
            <input placeholder="Şifre" type="password" value={form.password}
              onChange={e => setForm({ ...form, password: e.target.value })} className="text-sm" />
            <input placeholder="Ad Soyad" value={form.fullName}
              onChange={e => setForm({ ...form, fullName: e.target.value })} className="text-sm" />
            <select value={form.role} onChange={e => setForm({ ...form, role: e.target.value })}
              className="text-sm">
              <option value="Admin">Admin</option>
              <option value="Teknisyen">Teknisyen</option>
            </select>
            {form.role === 'Teknisyen' && (
              <select value={form.menuProfileId} onChange={e => setForm({ ...form, menuProfileId: e.target.value })}
                className="text-sm col-span-2" title="Menü Profili">
                <option value="">Varsayılan (Teknisyen)</option>
                {profiles.map(p => <option key={p.id} value={p.id}>Menü Profili: {p.name}</option>)}
              </select>
            )}
            <input placeholder="E-posta (opsiyonel)" type="email" value={form.email}
              onChange={e => setForm({ ...form, email: e.target.value })} className="text-sm col-span-2" />
          </div>
          <div className="flex gap-2">
            <button onClick={handleCreate} className="btn-primary text-sm"><Save className="w-3.5 h-3.5" /> Kaydet</button>
            <button onClick={() => setShowCreate(false)} className="btn-secondary text-sm"><X className="w-3.5 h-3.5" /> İptal</button>
          </div>
        </div>
      )}

      {/* Users Table */}
      <div className="bg-ms-bg-soft border border-ms-border rounded-xl overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-ms-border text-ms-text-muted text-xs">
              <th className="px-3 py-3 w-9">
                <input type="checkbox" checked={allSelected} onChange={toggleSelectAll} title="Tümünü seç" />
              </th>
              <th className="text-left px-4 py-3">Kullanıcı</th>
              <th className="text-left px-4 py-3">Ad Soyad</th>
              <th className="text-left px-4 py-3">E-posta</th>
              <th className="text-left px-4 py-3">Rol</th>
              <th className="text-left px-4 py-3">Menü Profili</th>
              <th className="text-left px-4 py-3">Durum</th>
              <th className="text-left px-4 py-3">Son Giriş</th>
              <th className="text-right px-4 py-3">İşlem</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={9} className="text-center py-8 text-ms-text-muted">Yükleniyor...</td></tr>
            ) : users.map(u => (
              <tr key={u.id} className={`border-b border-ms-border/50 hover:bg-ms-border/30 transition-colors ${selectedIds.has(u.id) ? 'bg-violet-500/5' : ''}`}>
                <td className="px-3 py-3 w-9">
                  <input type="checkbox" checked={selectedIds.has(u.id)} onChange={() => toggleSelect(u.id)} />
                </td>
                <td className="px-4 py-3 font-mono text-ms-text">{u.username}</td>
                <td className="px-4 py-3 text-ms-text">
                  {editId === u.id ? (
                    <input value={editForm.fullName} onChange={e => setEditForm({ ...editForm, fullName: e.target.value })}
                      className="text-sm w-full" />
                  ) : u.fullName}
                </td>
                <td className="px-4 py-3 text-ms-text-muted text-xs">
                  {editId === u.id ? (
                    <input value={editForm.email} onChange={e => setEditForm({ ...editForm, email: e.target.value })}
                      className="text-sm w-full" type="email" placeholder="E-posta" />
                  ) : (u.email || <span className="text-ms-text-muted/50">—</span>)}
                </td>
                <td className="px-4 py-3">
                  {editId === u.id ? (
                    <select value={editForm.role} onChange={e => setEditForm({ ...editForm, role: e.target.value })}
                      className="text-sm">
                      <option value="Admin">Admin</option>
                      <option value="Teknisyen">Teknisyen</option>
                    </select>
                  ) : (
                    <span className={`inline-flex items-center gap-1 text-xs font-medium px-2 py-0.5 rounded-full ${
                      u.role === 'Admin' ? 'bg-violet-500/10 text-violet-400' : 'bg-sky-500/10 text-sky-400'
                    }`}>
                      {u.role === 'Admin' ? <Shield className="w-3 h-3" /> : <Wrench className="w-3 h-3" />}
                      {u.role}
                    </span>
                  )}
                </td>
                <td className="px-4 py-3 text-xs">
                  {u.role === 'Admin' ? (
                    <span className="text-ms-text-muted/60">tüm menüler</span>
                  ) : editId === u.id && editForm.role === 'Teknisyen' ? (
                    <select value={editForm.menuProfileId} onChange={e => setEditForm({ ...editForm, menuProfileId: Number(e.target.value) })}
                      className="text-xs">
                      <option value={-1}>Varsayılan (Teknisyen)</option>
                      {profiles.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                    </select>
                  ) : (
                    <span className="inline-flex items-center gap-1 text-ms-text-muted">
                      <Layers className="w-3 h-3 text-violet-400/70" />
                      {u.menuProfileName ?? 'Teknisyen (varsayılan)'}
                    </span>
                  )}
                </td>
                <td className="px-4 py-3">
                  <span className={`w-2 h-2 rounded-full inline-block ${u.isActive ? 'bg-emerald-500' : 'bg-rose-500'}`} />
                  <span className="ml-2 text-xs text-ms-text-muted">{u.isActive ? 'Aktif' : 'Pasif'}</span>
                </td>
                <td className="px-4 py-3 text-xs text-ms-text-muted font-mono">
                  {u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString('tr-TR') : '-'}
                </td>
                <td className="px-4 py-3">
                  <div className="flex items-center justify-end gap-1">
                    {editId === u.id ? (
                      <>
                        <button onClick={() => handleEdit(u.id)} className="p-1.5 rounded-lg hover:bg-emerald-500/10 text-emerald-400" title="Kaydet"><Save className="w-3.5 h-3.5" /></button>
                        <button onClick={() => setEditId(null)} className="p-1.5 rounded-lg hover:bg-ms-border text-ms-text-muted" title="İptal"><X className="w-3.5 h-3.5" /></button>
                      </>
                    ) : resetId === u.id ? (
                      <div className="flex items-center gap-1">
                        <input type="password" placeholder="Yeni şifre" value={newPass}
                          onChange={e => setNewPass(e.target.value)}
                          className="text-xs w-28 py-1" />
                        <button onClick={() => handleResetPassword(u.id)} className="p-1.5 rounded-lg hover:bg-emerald-500/10 text-emerald-400" title="Kaydet"><Save className="w-3.5 h-3.5" /></button>
                        <button onClick={() => { setResetId(null); setNewPass(''); }} className="p-1.5 rounded-lg hover:bg-ms-border text-ms-text-muted" title="İptal"><X className="w-3.5 h-3.5" /></button>
                      </div>
                    ) : (
                      <>
                        <button onClick={() => { setEditId(u.id); setEditForm({ fullName: u.fullName, role: u.role, isActive: u.isActive, email: u.email || '', menuProfileId: u.menuProfileId ?? -1 }); }}
                          className="p-1.5 rounded-lg hover:bg-sky-500/10 text-sky-400" title="Düzenle"><Edit3 className="w-3.5 h-3.5" /></button>
                        {u.role !== 'Admin' && (
                          <button onClick={() => setOverrideUser(u)}
                            className="p-1.5 rounded-lg hover:bg-violet-500/10 text-violet-400" title="Özel İzinler (kişiye özel menü aç/kapat)"><SlidersHorizontal className="w-3.5 h-3.5" /></button>
                        )}
                        <button onClick={() => setResetId(u.id)}
                          className="p-1.5 rounded-lg hover:bg-amber-500/10 text-amber-400" title="Şifre Sıfırla"><KeyRound className="w-3.5 h-3.5" /></button>
                        <button onClick={() => handleDelete(u.id, u.username)}
                          className="p-1.5 rounded-lg hover:bg-rose-500/10 text-rose-400" title="Sil"><Trash2 className="w-3.5 h-3.5" /></button>
                      </>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {overrideUser && (
        <UserMenuOverrideModal
          user={overrideUser}
          profile={profileForUser(overrideUser)}
          onClose={() => setOverrideUser(null)}
          onSaved={load}
        />
      )}
    </div>
  );
};

// ════════════════════════════════════════
// STORE / DEVICE MANAGEMENT
// ════════════════════════════════════════
const StoreManagement: React.FC = () => {
  const [devices, setDevices] = useState<StoreDeviceItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [showProvision, setShowProvision] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [form, setForm] = useState({ storeCode: '', storeName: '', deviceType: 'Kasa-1', deviceName: '', calculatedIpAddress: '', dbConnectionString: '' });
  const [editForm, setEditForm] = useState({ storeName: '', deviceName: '', deviceType: '', calculatedIpAddress: '', dbConnectionString: '' });
  const [provForm, setProvForm] = useState({ storeCode: '', storeName: '', ipBlock: '', kasaCount: '3' });
  const [provResult, setProvResult] = useState<any>(null);
  const [provLoading, setProvLoading] = useState(false);
  const [msg, setMsg] = useState('');

  const load = async () => {
    try {
      const data = await apiClient.get<StoreDeviceItem[]>('/api/store-devices');
      setDevices(data);
    } catch { }
    setLoading(false);
  };

  useEffect(() => { load(); }, []);

  const filtered = devices.filter(d => {
    const q = search.toLowerCase();
    return !q || d.storeCode.toString().includes(q) || d.storeName.toLowerCase().includes(q) ||
      d.deviceId.toLowerCase().includes(q) || d.calculatedIpAddress.includes(q);
  });

  // Group by store
  const stores = new Map<number, { storeName: string; devices: StoreDeviceItem[] }>();
  for (const d of filtered) {
    if (!stores.has(d.storeCode)) stores.set(d.storeCode, { storeName: d.storeName, devices: [] });
    stores.get(d.storeCode)!.devices.push(d);
  }

  const handleProvision = async () => {
    setProvLoading(true); setProvResult(null); setMsg('');
    try {
      const res = await apiClient.post<any>('/api/store-devices/provision', {
        storeCode: parseInt(provForm.storeCode) || 0,
        storeName: provForm.storeName,
        ipBlock: provForm.ipBlock || undefined,
        kasaCount: parseInt(provForm.kasaCount) || 3,
      });
      setProvResult(res);
      load();
    } catch (e: any) { setMsg(e.message); }
    finally { setProvLoading(false); }
  };

  const handleCreate = async () => {
    try {
      await apiClient.post('/api/store-devices', {
        ...form,
        storeCode: parseInt(form.storeCode) || 0,
      });
      setShowCreate(false);
      setForm({ storeCode: '', storeName: '', deviceType: 'Kasa-1', deviceName: '', calculatedIpAddress: '', dbConnectionString: '' });
      load();
    } catch (e: any) { setMsg(e.message); }
  };

  const handleEdit = async (deviceId: string) => {
    try {
      await apiClient.put(`/api/store-devices/${encodeURIComponent(deviceId)}`, editForm);
      setEditId(null);
      load();
    } catch (e: any) { setMsg(e.message); }
  };

  const handleDelete = async (deviceId: string) => {
    if (!confirm(`${deviceId} cihazını silmek istediğinize emin misiniz?`)) return;
    try {
      await apiClient.delete(`/api/store-devices/${encodeURIComponent(deviceId)}`);
      load();
    } catch (e: any) { setMsg(e.message); }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-lg font-bold text-ms-text shrink-0">Mağaza / Kasa Yönetimi</h2>
        <div className="relative flex-1 max-w-xs">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-ms-text-muted" />
          <input placeholder="Ara (kod, isim, IP)..." value={search}
            onChange={e => setSearch(e.target.value)}
            className="w-full pl-9 text-sm" />
        </div>
        <div className="flex gap-2 shrink-0">
          <button onClick={() => { setShowProvision(true); setShowCreate(false); setProvResult(null); }} className="btn-primary text-sm">
            <Plus className="w-4 h-4" /> Yeni Mağaza
          </button>
          <button onClick={() => { setShowCreate(true); setShowProvision(false); }} className="btn-secondary text-sm">
            <Plus className="w-4 h-4" /> Tek Cihaz
          </button>
        </div>
      </div>

      {msg && (
        <div className="text-xs text-amber-400 bg-amber-500/10 border border-amber-500/20 rounded-lg px-3 py-2">
          {msg} <button onClick={() => setMsg('')} className="ml-2 text-ms-text-muted">x</button>
        </div>
      )}

      {/* Provision Form — Yeni Mağaza Prosedürü */}
      {showProvision && (
        <div className="bg-ms-bg-soft border border-violet-500/20 rounded-xl p-4 space-y-3">
          <h3 className="text-sm font-bold text-ms-text">Yeni Mağaza Oluştur</h3>
          <p className="text-[11px] text-ms-text-muted">
            Router + PC + kasalar otomatik oluşturulur. IP: bloğu.1 (Router), bloğu.2 (PC), bloğu.31-3N (Kasalar)
          </p>
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
            <div>
              <label className="form-label">Mağaza Kodu</label>
              <input type="number" placeholder="ör. 258" value={provForm.storeCode}
                onChange={e => setProvForm({ ...provForm, storeCode: e.target.value })} className="text-sm" />
            </div>
            <div>
              <label className="form-label">Mağaza Adı</label>
              <input placeholder="ör. Datça Marina" value={provForm.storeName}
                onChange={e => setProvForm({ ...provForm, storeName: e.target.value })} className="text-sm" />
            </div>
            <div>
              <label className="form-label">IP Bloğu</label>
              <input placeholder="ör. 192.168.240" value={provForm.ipBlock}
                onChange={e => setProvForm({ ...provForm, ipBlock: e.target.value })} className="text-sm" />
              <p className="text-[10px] text-ms-text-muted mt-0.5">Boşsa 192.168.{'{kod}'} kullanılır</p>
            </div>
            <div>
              <label className="form-label">Kasa Sayısı</label>
              <select value={provForm.kasaCount} onChange={e => setProvForm({ ...provForm, kasaCount: e.target.value })}
                className="text-sm">
                {[0,1,2,3,4,5].map(n => <option key={n} value={n}>{n} kasa</option>)}
              </select>
            </div>
          </div>

          {/* Önizleme */}
          {provForm.storeCode && provForm.storeName && (
            <div className="bg-ms-panel border border-ms-border rounded-lg p-3 space-y-1 text-[12px] text-ms-text-muted">
              <div className="text-[10px] font-semibold uppercase tracking-wider text-ms-text-muted mb-1">Oluşturulacak cihazlar</div>
              {(() => {
                const block = provForm.ipBlock || `192.168.${provForm.storeCode}`;
                const code = provForm.storeCode;
                const kasaCount = parseInt(provForm.kasaCount) || 3;
                const items = [
                  { id: `${code}-Router`, type: 'Router', ip: `${block}.1` },
                  { id: `${code}-PC`, type: 'PC', ip: `${block}.2` },
                  ...Array.from({ length: kasaCount }, (_, i) => ({
                    id: `${code}-K${i+1}`, type: `Kasa-${i+1}`, ip: `${block}.${31+i}`,
                  })),
                ];
                return (
                  <div className="grid grid-cols-2 lg:grid-cols-3 gap-x-4 gap-y-0.5">
                    {items.map(it => (
                      <div key={it.id} className="flex items-center gap-2">
                        <span className={`text-[10px] font-medium px-1.5 py-0.5 rounded ${
                          it.type === 'Router' ? 'bg-emerald-500/10 text-emerald-400'
                          : it.type === 'PC' ? 'bg-sky-500/10 text-sky-400'
                          : 'bg-amber-500/10 text-amber-400'
                        }`}>{it.type}</span>
                        <span className="font-mono text-ms-text">{it.ip}</span>
                      </div>
                    ))}
                  </div>
                );
              })()}
            </div>
          )}

          <div className="flex gap-2">
            <button onClick={handleProvision} disabled={provLoading || !provForm.storeCode || !provForm.storeName}
              className="btn-primary text-sm">
              {provLoading ? <><RefreshCw className="w-3.5 h-3.5 animate-spin" /> Oluşturuluyor...</> : <><Save className="w-3.5 h-3.5" /> Mağaza Oluştur</>}
            </button>
            <button onClick={() => { setShowProvision(false); setProvResult(null); }} className="btn-secondary text-sm">
              <X className="w-3.5 h-3.5" /> İptal
            </button>
          </div>

          {provResult && (
            <div className="bg-emerald-500/10 border border-emerald-500/20 rounded-lg p-3 text-sm text-emerald-300">
              <span className="font-semibold">{provResult.storeName}</span> ({provResult.storeCode}) — {provResult.devicesCreated?.length} cihaz oluşturuldu.
              <span className="ml-2 font-mono text-[11px] text-emerald-400">IP: {provResult.ipBlock}.*</span>
            </div>
          )}
        </div>
      )}

      {/* Create Form — Tek Cihaz */}
      {showCreate && (
        <div className="bg-ms-bg-soft border border-ms-border rounded-xl p-4 space-y-3">
          <h3 className="text-sm font-bold text-ms-text">Yeni Mağaza / Cihaz Ekle</h3>
          <div className="grid grid-cols-3 gap-3">
            <input placeholder="Mağaza Kodu" value={form.storeCode} onChange={e => setForm({ ...form, storeCode: e.target.value })} className="text-sm" />
            <input placeholder="Mağaza Adı" value={form.storeName} onChange={e => setForm({ ...form, storeName: e.target.value })} className="text-sm" />
            <select value={form.deviceType} onChange={e => setForm({ ...form, deviceType: e.target.value })}
              className="text-sm">
              <option value="PC">PC</option>
              <option value="Kasa-1">Kasa-1</option>
              <option value="Kasa-2">Kasa-2</option>
              <option value="Kasa-3">Kasa-3</option>
            </select>
            <input placeholder="Cihaz Adı" value={form.deviceName} onChange={e => setForm({ ...form, deviceName: e.target.value })} className="text-sm" />
            <input placeholder="IP Adresi (opsiyonel)" value={form.calculatedIpAddress} onChange={e => setForm({ ...form, calculatedIpAddress: e.target.value })} className="text-sm" />
            <input placeholder="DB Bağlantı (opsiyonel)" value={form.dbConnectionString} onChange={e => setForm({ ...form, dbConnectionString: e.target.value })} className="text-sm" />
          </div>
          <div className="flex gap-2">
            <button onClick={handleCreate} className="btn-primary text-sm"><Save className="w-3.5 h-3.5" /> Kaydet</button>
            <button onClick={() => setShowCreate(false)} className="btn-secondary text-sm"><X className="w-3.5 h-3.5" /> İptal</button>
          </div>
        </div>
      )}

      {/* Store Groups */}
      {loading ? (
        <div className="text-center py-8 text-ms-text-muted">Yükleniyor...</div>
      ) : (
        <div className="space-y-3">
          {Array.from(stores.entries()).map(([storeCode, { storeName, devices: devs }]) => (
            <div key={storeCode} className="bg-ms-bg-soft border border-ms-border rounded-xl overflow-hidden">
              <div className="flex items-center gap-3 px-4 py-2.5 border-b border-ms-border/50 bg-ms-panel">
                <span className="font-mono text-sm text-violet-400 font-bold">{storeCode}</span>
                <span className="text-sm text-ms-text font-medium">{storeName}</span>
                <span className="ml-auto text-xs text-ms-text-muted">{devs.length} cihaz</span>
              </div>
              <table className="w-full text-sm">
                <tbody>
                  {devs.map(d => (
                    <tr key={d.deviceId} className="border-b border-ms-border/30 hover:bg-ms-border/20 transition-colors">
                      <td className="px-4 py-2 w-24">
                        <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${
                          d.deviceType === 'PC' ? 'bg-sky-500/10 text-sky-400' : 'bg-amber-500/10 text-amber-400'
                        }`}>{d.deviceType}</span>
                      </td>
                      <td className="px-4 py-2 text-ms-text">
                        {editId === d.deviceId ? (
                          <input value={editForm.deviceName} onChange={e => setEditForm({ ...editForm, deviceName: e.target.value })} className="text-sm w-full" />
                        ) : d.deviceName}
                      </td>
                      <td className="px-4 py-2 font-mono text-ms-text-muted text-xs">
                        {editId === d.deviceId ? (
                          <input value={editForm.calculatedIpAddress} onChange={e => setEditForm({ ...editForm, calculatedIpAddress: e.target.value })} className="text-sm w-full" />
                        ) : d.calculatedIpAddress}
                      </td>
                      <td className="px-4 py-2 text-right">
                        <div className="flex items-center justify-end gap-1">
                          {editId === d.deviceId ? (
                            <>
                              <button onClick={() => handleEdit(d.deviceId)} className="p-1.5 rounded-lg hover:bg-emerald-500/10 text-emerald-400"><Save className="w-3.5 h-3.5" /></button>
                              <button onClick={() => setEditId(null)} className="p-1.5 rounded-lg hover:bg-ms-border text-ms-text-muted"><X className="w-3.5 h-3.5" /></button>
                            </>
                          ) : (
                            <>
                              <button onClick={() => { setEditId(d.deviceId); setEditForm({ storeName: d.storeName, deviceName: d.deviceName, deviceType: d.deviceType, calculatedIpAddress: d.calculatedIpAddress, dbConnectionString: d.dbConnectionString ?? '' }); }}
                                className="p-1.5 rounded-lg hover:bg-sky-500/10 text-sky-400" title="Düzenle"><Edit3 className="w-3.5 h-3.5" /></button>
                              <button onClick={() => handleDelete(d.deviceId)}
                                className="p-1.5 rounded-lg hover:bg-rose-500/10 text-rose-400" title="Sil"><Trash2 className="w-3.5 h-3.5" /></button>
                            </>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

// ════════════════════════════════════════
// CHANGE PASSWORD
// ════════════════════════════════════════
const ChangePasswordPanel: React.FC = () => {
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [msg, setMsg] = useState('');
  const [msgType, setMsgType] = useState<'success' | 'error'>('error');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setMsg('');

    if (!currentPassword || !newPassword) {
      setMsg('Tüm alanları doldurun'); setMsgType('error'); return;
    }
    if (newPassword.length < 4) {
      setMsg('Yeni şifre en az 4 karakter olmalı'); setMsgType('error'); return;
    }
    if (newPassword !== confirmPassword) {
      setMsg('Yeni şifreler eşleşmiyor'); setMsgType('error'); return;
    }

    setLoading(true);
    try {
      const res = await apiClient.post<{ message: string }>('/api/auth/change-password', {
        currentPassword, newPassword,
      });
      setMsg(res.message || 'Şifreniz değiştirildi');
      setMsgType('success');
      setCurrentPassword(''); setNewPassword(''); setConfirmPassword('');
    } catch (e: any) {
      setMsg(e.message || 'Bir hata oluştu');
      setMsgType('error');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-md">
      <h2 className="text-lg font-bold text-ms-text mb-4">Şifre Değiştir</h2>
      <form onSubmit={handleSubmit} className="bg-ms-bg-soft border border-ms-border rounded-xl p-5 space-y-4">
        <div>
          <label className="form-label">Mevcut Şifre</label>
          <input type="password" value={currentPassword}
            onChange={e => setCurrentPassword(e.target.value)}
            className="w-full text-sm" placeholder="Mevcut şifreniz" />
        </div>
        <div>
          <label className="form-label">Yeni Şifre</label>
          <input type="password" value={newPassword}
            onChange={e => setNewPassword(e.target.value)}
            className="w-full text-sm" placeholder="Yeni şifre (min 4 karakter)" />
        </div>
        <div>
          <label className="form-label">Yeni Şifre (Tekrar)</label>
          <input type="password" value={confirmPassword}
            onChange={e => setConfirmPassword(e.target.value)}
            className="w-full text-sm" placeholder="Yeni şifreyi tekrar girin" />
        </div>

        {msg && (
          <div className={`text-xs rounded-lg px-3 py-2 border ${
            msgType === 'success'
              ? 'text-emerald-400 bg-emerald-500/10 border-emerald-500/20'
              : 'text-red-400 bg-red-500/10 border-red-500/20'
          }`}>
            {msg}
          </div>
        )}

        <button type="submit" disabled={loading} className="btn-primary text-sm w-full justify-center">
          <KeyRound className="w-4 h-4" />
          {loading ? 'Değiştiriliyor...' : 'Şifreyi Değiştir'}
        </button>
      </form>
    </div>
  );
};

// ════════════════════════════════════════
// LOGIN HISTORY
// ════════════════════════════════════════
const LoginHistoryPanel: React.FC = () => {
  const [history, setHistory] = useState<LoginHistoryItem[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    apiClient.get<LoginHistoryItem[]>('/api/users/login-history?limit=100')
      .then(setHistory)
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-bold text-ms-text">Giriş Geçmişi</h2>
      <div className="bg-ms-bg-soft border border-ms-border rounded-xl overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-ms-border text-ms-text-muted text-xs">
              <th className="text-left px-4 py-3">Kullanıcı</th>
              <th className="text-left px-4 py-3">Tarih</th>
              <th className="text-left px-4 py-3">IP Adresi</th>
              <th className="text-left px-4 py-3">Durum</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={4} className="text-center py-8 text-ms-text-muted">Yükleniyor...</td></tr>
            ) : history.length === 0 ? (
              <tr><td colSpan={4} className="text-center py-8 text-ms-text-muted">Henüz giriş kaydı yok</td></tr>
            ) : history.map(h => (
              <tr key={h.id} className="border-b border-ms-border/50 hover:bg-ms-border/30 transition-colors">
                <td className="px-4 py-2.5 font-mono text-ms-text">{h.username}</td>
                <td className="px-4 py-2.5 text-ms-text-muted text-xs font-mono">{new Date(h.loginAt).toLocaleString('tr-TR')}</td>
                <td className="px-4 py-2.5 text-ms-text-muted font-mono text-xs">{h.ipAddress || '-'}</td>
                <td className="px-4 py-2.5">
                  {h.success ? (
                    <span className="inline-flex items-center gap-1 text-xs text-emerald-400"><CheckCircle className="w-3.5 h-3.5" /> Başarılı</span>
                  ) : (
                    <span className="inline-flex items-center gap-1 text-xs text-rose-400"><XCircle className="w-3.5 h-3.5" /> Başarısız</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};

// ════════════════════════════════════════
// MENU MANAGEMENT — Profiller (Yetki Grupları) + ortak araçlar
// ════════════════════════════════════════

const CONFIGURABLE: MenuCatalogItem[] = getConfigurableCatalog();
const ALL_PATHS: string[] = CONFIGURABLE.map(i => i.to);
const CATALOG_BY_GROUP: { group: string; items: MenuCatalogItem[] }[] = (() => {
  const map = new Map<string, MenuCatalogItem[]>();
  for (const it of CONFIGURABLE) {
    if (!map.has(it.group)) map.set(it.group, []);
    map.get(it.group)!.push(it);
  }
  return Array.from(map, ([group, items]) => ({ group, items }));
})();

/** Profil → görünür menü seti. */
function profileVisibleSet(p: MenuProfileDto): Set<string> {
  if (p.allowAllByDefault) return new Set(ALL_PATHS.filter(to => !p.hiddenMenus.includes(to)));
  return new Set(p.allowedMenus.filter(to => ALL_PATHS.includes(to)));
}
/** Görünür set → allowAllByDefault'a göre {allowedMenus, hiddenMenus}. */
function serializeVisible(visible: Set<string>, allowAll: boolean) {
  return allowAll
    ? { allowedMenus: [] as string[], hiddenMenus: ALL_PATHS.filter(p => !visible.has(p)) }
    : { allowedMenus: ALL_PATHS.filter(p => visible.has(p)), hiddenMenus: [] as string[] };
}

/** ON = görünür. Gruplu menü toggle ızgarası (profil editörü için). */
const MenuToggleGrid: React.FC<{ visible: Set<string>; onToggle: (path: string) => void }> = ({ visible, onToggle }) => (
  <div className="space-y-3">
    {CATALOG_BY_GROUP.map(({ group, items }) => {
      const visCount = items.filter(i => visible.has(i.to)).length;
      return (
        <div key={group} className="bg-ms-bg-soft border border-ms-border rounded-xl overflow-hidden">
          <div className="flex items-center justify-between px-4 py-2.5 border-b border-ms-border/50" style={{ background: 'rgba(255,255,255,0.01)' }}>
            <span className="text-sm font-semibold text-ms-text">{group}</span>
            <span className="text-[10px] text-ms-text-muted">{visCount}/{items.length} görünür</span>
          </div>
          <div className="divide-y divide-ms-border/30">
            {items.map(item => {
              const isVisible = visible.has(item.to);
              return (
                <button key={item.to} onClick={() => onToggle(item.to)}
                  className={`w-full flex items-center gap-3 px-4 py-2.5 text-left transition-all hover:bg-white/[0.02] ${isVisible ? '' : 'opacity-40'}`}>
                  <div className={`w-8 h-5 rounded-full relative transition-colors ${isVisible ? 'bg-violet-600' : 'bg-slate-700'}`}>
                    <div className={`absolute top-0.5 w-4 h-4 rounded-full bg-white shadow transition-transform ${isVisible ? 'left-3.5' : 'left-0.5'}`} />
                  </div>
                  <span className="text-sm text-ms-text font-medium flex-1">{item.label}</span>
                  <span className="text-[10px] font-mono text-ms-text-muted">{item.to}</span>
                  {isVisible ? <Eye className="w-3.5 h-3.5 text-violet-400" /> : <EyeOff className="w-3.5 h-3.5 text-slate-500" />}
                </button>
              );
            })}
          </div>
        </div>
      );
    })}
  </div>
);

const MenuManagementPanel: React.FC = () => {
  const [profiles, setProfiles] = useState<MenuProfileDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [isNew, setIsNew] = useState(false);
  const [draft, setDraft] = useState<{ name: string; description: string; allowAllByDefault: boolean; visible: Set<string> } | null>(null);
  const [saving, setSaving] = useState(false);
  const [msg, setMsg] = useState('');

  const load = async () => {
    try { setProfiles(await apiClient.get<MenuProfileDto[]>('/api/menu-profiles')); }
    catch (e: any) { setMsg(e.message); }
    finally { setLoading(false); }
  };
  useEffect(() => { load(); }, []);

  const editProfile = (p: MenuProfileDto) => {
    setIsNew(false); setSelectedId(p.id); setMsg('');
    setDraft({ name: p.name, description: p.description ?? '', allowAllByDefault: p.allowAllByDefault, visible: profileVisibleSet(p) });
  };
  const startNew = () => {
    setIsNew(true); setSelectedId(null); setMsg('');
    setDraft({ name: '', description: '', allowAllByDefault: false, visible: new Set() });
  };
  const cancel = () => { setDraft(null); setSelectedId(null); setIsNew(false); setMsg(''); };

  const toggle = (path: string) => setDraft(d => {
    if (!d) return d;
    const v = new Set(d.visible);
    v.has(path) ? v.delete(path) : v.add(path);
    return { ...d, visible: v };
  });

  const save = async () => {
    if (!draft) return;
    if (!draft.name.trim()) { setMsg('Profil adı gerekli'); return; }
    setSaving(true); setMsg('');
    const { allowedMenus, hiddenMenus } = serializeVisible(draft.visible, draft.allowAllByDefault);
    const body = { name: draft.name.trim(), description: draft.description.trim(), allowAllByDefault: draft.allowAllByDefault, allowedMenus, hiddenMenus };
    try {
      if (isNew) {
        const created = await apiClient.post<MenuProfileDto>('/api/menu-profiles', body);
        await load();
        setIsNew(false); setSelectedId(created.id);
        setDraft({ name: created.name, description: created.description ?? '', allowAllByDefault: created.allowAllByDefault, visible: profileVisibleSet(created) });
      } else if (selectedId != null) {
        await apiClient.put<MenuProfileDto>(`/api/menu-profiles/${selectedId}`, body);
        await load();
      }
      setMsg('Kaydedildi'); setTimeout(() => setMsg(''), 2500);
    } catch (e: any) { setMsg(e.message); }
    finally { setSaving(false); }
  };

  const del = async (p: MenuProfileDto) => {
    if (p.isSystem) return;
    if (!confirm(`"${p.name}" profilini silmek istiyor musunuz?\nBu profile bağlı kullanıcılar varsayılan Teknisyen profiline döner.`)) return;
    try { await apiClient.delete(`/api/menu-profiles/${p.id}`); cancel(); load(); }
    catch (e: any) { setMsg(e.message); }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-bold text-ms-text">Menü Profilleri (Yetki Grupları)</h2>
          <p className="text-xs text-ms-text-muted mt-0.5">
            Grup tanımlayın ve her gruba hangi menülerin görüneceğini seçin. Kullanıcıları Kullanıcı Yönetimi'nden bu gruplara atayın.
            Admin her zaman tüm menüleri görür.
          </p>
        </div>
        <button onClick={startNew} className="btn-primary text-sm shrink-0"><Plus className="w-4 h-4" /> Yeni Profil</button>
      </div>

      {msg && (
        <div className="text-xs text-emerald-400 bg-emerald-500/10 border border-emerald-500/20 rounded-lg px-3 py-2">{msg}</div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-[260px_1fr] gap-4">
        {/* Profil listesi */}
        <div className="space-y-2">
          {loading ? (
            <div className="text-xs text-ms-text-muted px-2 py-4">Yükleniyor...</div>
          ) : profiles.map(p => (
            <button key={p.id} onClick={() => editProfile(p)}
              className={`w-full text-left rounded-xl border px-3 py-2.5 transition-colors ${
                selectedId === p.id ? 'border-violet-500/60 bg-violet-500/10' : 'border-ms-border bg-ms-bg-soft hover:border-violet-500/30'
              }`}>
              <div className="flex items-center gap-2">
                <Layers className="w-4 h-4 text-violet-400 shrink-0" />
                <span className="text-sm font-semibold text-ms-text flex-1 truncate">{p.name}</span>
                {p.isSystem && <span className="text-[9px] uppercase tracking-wide text-violet-400 bg-violet-500/10 px-1.5 py-0.5 rounded">sistem</span>}
              </div>
              <div className="text-[10px] text-ms-text-muted mt-1 flex items-center gap-2">
                <span>{p.userCount} kullanıcı</span>
                {p.allowAllByDefault && <span className="text-amber-400">• yeni menüler açık</span>}
              </div>
            </button>
          ))}
        </div>

        {/* Profil editörü */}
        <div>
          {!draft ? (
            <div className="h-full flex items-center justify-center text-sm text-ms-text-muted border border-dashed border-ms-border rounded-xl py-16">
              Düzenlemek için soldan bir profil seçin veya yeni profil oluşturun.
            </div>
          ) : (
            <div className="space-y-4">
              <div className="bg-ms-bg-soft border border-ms-border rounded-xl p-4 space-y-3">
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <div>
                    <label className="form-label">Profil Adı</label>
                    <input value={draft.name} disabled={!isNew && profiles.find(p => p.id === selectedId)?.isSystem}
                      onChange={e => setDraft(d => d ? { ...d, name: e.target.value } : d)}
                      className="text-sm w-full disabled:opacity-50" placeholder="ör. Superuser, Depo" />
                  </div>
                  <div>
                    <label className="form-label">Açıklama</label>
                    <input value={draft.description}
                      onChange={e => setDraft(d => d ? { ...d, description: e.target.value } : d)}
                      className="text-sm w-full" placeholder="opsiyonel" />
                  </div>
                </div>
                <label className="flex items-center gap-2 text-xs text-ms-text-muted cursor-pointer">
                  <input type="checkbox" checked={draft.allowAllByDefault}
                    onChange={e => setDraft(d => d ? { ...d, allowAllByDefault: e.target.checked } : d)} />
                  Yeni eklenen menüler bu profilde otomatik <strong className="text-ms-text">açık</strong> olsun
                  <span className="text-ms-text-muted/70">(kapalı: yalnızca seçtiklerin görünür — dar gruplar için güvenli)</span>
                </label>
                <div className="flex items-center gap-2">
                  <button onClick={() => setDraft(d => d ? { ...d, visible: new Set(ALL_PATHS) } : d)} className="btn-secondary text-xs"><Eye className="w-3.5 h-3.5" /> Tümünü Aç</button>
                  <button onClick={() => setDraft(d => d ? { ...d, visible: new Set() } : d)} className="btn-secondary text-xs"><EyeOff className="w-3.5 h-3.5" /> Tümünü Kapat</button>
                  <div className="flex-1" />
                  {!isNew && !profiles.find(p => p.id === selectedId)?.isSystem && (
                    <button onClick={() => { const p = profiles.find(x => x.id === selectedId); if (p) del(p); }} className="btn-secondary text-xs text-rose-400"><Trash2 className="w-3.5 h-3.5" /> Sil</button>
                  )}
                  <button onClick={cancel} className="btn-secondary text-xs"><X className="w-3.5 h-3.5" /> Vazgeç</button>
                  <button onClick={save} disabled={saving} className="btn-primary text-xs disabled:opacity-40"><Save className="w-3.5 h-3.5" /> {saving ? 'Kaydediliyor...' : 'Kaydet'}</button>
                </div>
              </div>

              <MenuToggleGrid visible={draft.visible} onToggle={toggle} />
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

// ════════════════════════════════════════
// KULLANICI MENÜ OVERRIDE (Özel İzinler) — modal
// ════════════════════════════════════════
const UserMenuOverrideModal: React.FC<{
  user: UserItem;
  profile: MenuProfileDto | null;
  onClose: () => void;
  onSaved: () => void;
}> = ({ user, profile, onClose, onSaved }) => {
  const [grants, setGrants] = useState<Set<string>>(new Set(user.menuGrants));
  const [denials, setDenials] = useState<Set<string>>(new Set(user.menuDenials));
  const [saving, setSaving] = useState(false);
  const [msg, setMsg] = useState('');

  const baseVisible = profile ? profileVisibleSet(profile) : new Set<string>();

  const setState = (path: string, state: 'default' | 'on' | 'off') => {
    setGrants(prev => { const n = new Set(prev); n.delete(path); if (state === 'on') n.add(path); return n; });
    setDenials(prev => { const n = new Set(prev); n.delete(path); if (state === 'off') n.add(path); return n; });
  };
  const stateOf = (path: string): 'default' | 'on' | 'off' =>
    grants.has(path) ? 'on' : denials.has(path) ? 'off' : 'default';

  const save = async () => {
    setSaving(true); setMsg('');
    try {
      await apiClient.put(`/api/users/${user.id}`, { menuGrants: Array.from(grants), menuDenials: Array.from(denials) });
      onSaved(); onClose();
    } catch (e: any) { setMsg(e.message); setSaving(false); }
  };

  const Pill: React.FC<{ active: boolean; tone: 'default' | 'on' | 'off'; onClick: () => void; children: React.ReactNode }> = ({ active, tone, onClick, children }) => {
    const colors = tone === 'on'
      ? (active ? 'bg-emerald-600 text-white' : 'text-emerald-400')
      : tone === 'off'
      ? (active ? 'bg-rose-600 text-white' : 'text-rose-400')
      : (active ? 'bg-slate-600 text-white' : 'text-ms-text-muted');
    return <button onClick={onClick} className={`px-2 py-1 rounded-md text-[10px] font-semibold transition-colors ${colors} ${active ? '' : 'hover:bg-white/5'}`}>{children}</button>;
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={onClose}>
      <div className="bg-ms-panel border border-ms-border rounded-2xl w-full max-w-2xl max-h-[85vh] flex flex-col" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between px-5 py-3.5 border-b border-ms-border">
          <div>
            <h3 className="text-sm font-bold text-ms-text">Özel İzinler — {user.fullName || user.username}</h3>
            <p className="text-[11px] text-ms-text-muted mt-0.5">
              Profil: <strong className="text-ms-text">{profile?.name ?? 'Teknisyen (varsayılan)'}</strong>. Profilin üstüne kişiye özel aç/kapat.
            </p>
          </div>
          <button onClick={onClose} className="p-1.5 rounded-lg hover:bg-ms-border text-ms-text-muted"><X className="w-4 h-4" /></button>
        </div>

        {msg && <div className="mx-5 mt-3 text-xs text-rose-400 bg-rose-500/10 border border-rose-500/20 rounded-lg px-3 py-2">{msg}</div>}

        <div className="flex-1 overflow-y-auto px-5 py-3 space-y-3">
          {CATALOG_BY_GROUP.map(({ group, items }) => (
            <div key={group} className="bg-ms-bg-soft border border-ms-border rounded-xl overflow-hidden">
              <div className="px-4 py-2 border-b border-ms-border/50 text-xs font-semibold text-ms-text">{group}</div>
              <div className="divide-y divide-ms-border/30">
                {items.map(item => {
                  const st = stateOf(item.to);
                  const baseShown = baseVisible.has(item.to);
                  return (
                    <div key={item.to} className="flex items-center gap-3 px-4 py-2">
                      <span className="text-sm text-ms-text flex-1 truncate">{item.label}</span>
                      <span className="text-[9px] text-ms-text-muted/70">{baseShown ? 'varsayılan: açık' : 'varsayılan: gizli'}</span>
                      <div className="flex items-center gap-1 bg-ms-panel rounded-lg p-0.5 border border-ms-border">
                        <Pill active={st === 'default'} tone="default" onClick={() => setState(item.to, 'default')}>Varsayılan</Pill>
                        <Pill active={st === 'on'} tone="on" onClick={() => setState(item.to, 'on')}>Aç</Pill>
                        <Pill active={st === 'off'} tone="off" onClick={() => setState(item.to, 'off')}>Kapat</Pill>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          ))}
        </div>

        <div className="flex items-center justify-end gap-2 px-5 py-3 border-t border-ms-border">
          <button onClick={onClose} className="btn-secondary text-sm"><X className="w-3.5 h-3.5" /> İptal</button>
          <button onClick={save} disabled={saving} className="btn-primary text-sm disabled:opacity-40"><Save className="w-3.5 h-3.5" /> {saving ? 'Kaydediliyor...' : 'Kaydet'}</button>
        </div>
      </div>
    </div>
  );
};

// ════════════════════════════════════════
// EMAIL ALARM SETTINGS
// ════════════════════════════════════════
interface SmtpConfig {
  host: string; port: number; username: string; hasPassword: boolean;
  useSsl: boolean; fromAddress: string; fromName: string;
}
interface AlarmConfig {
  emailAlertsEnabled: boolean; alertRecipientRoles: string[]; cooldownMinutes: number;
}

const EmailAlarmPanel: React.FC = () => {
  const [smtp, setSmtp] = useState({ host: '', port: 587, username: '', password: '', useSsl: false, fromAddress: '', fromName: 'Orchestra' });
  const [hasPassword, setHasPassword] = useState(false);
  const [alarm, setAlarm] = useState<AlarmConfig>({ emailAlertsEnabled: false, alertRecipientRoles: ['Admin'], cooldownMinutes: 30 });
  const [users, setUsers] = useState<{ fullName: string; email: string | null; role: string }[]>([]);
  const [smtpMsg, setSmtpMsg] = useState('');
  const [smtpMsgType, setSmtpMsgType] = useState<'success' | 'error'>('error');
  const [alarmMsg, setAlarmMsg] = useState('');
  const [testMsg, setTestMsg] = useState('');
  const [testMsgType, setTestMsgType] = useState<'success' | 'error'>('error');
  const [deliveryMsg, setDeliveryMsg] = useState('');
  const [deliveryMsgType, setDeliveryMsgType] = useState<'success' | 'error'>('error');
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [sendingTestEmail, setSendingTestEmail] = useState(false);
  const [sendingBirthdayEmail, setSendingBirthdayEmail] = useState(false);
  const [birthdayMsg, setBirthdayMsg] = useState('');
  const [birthdayMsgType, setBirthdayMsgType] = useState<'success' | 'error'>('error');

  useEffect(() => {
    apiClient.get<SmtpConfig>('/api/app-settings/smtp').then(data => {
      setSmtp(prev => ({ ...prev, host: data.host, port: data.port, username: data.username, useSsl: data.useSsl, fromAddress: data.fromAddress, fromName: data.fromName }));
      setHasPassword(data.hasPassword);
    }).catch(() => {});
    apiClient.get<AlarmConfig>('/api/app-settings/alarm').then(setAlarm).catch(() => {});
    apiClient.get<{ fullName: string; email: string | null; role: string }[]>('/api/users').then(setUsers).catch(() => {});
  }, []);

  const saveSmtp = async () => {
    setSaving(true); setSmtpMsg('');
    try {
      await apiClient.put('/api/app-settings/smtp', smtp);
      setSmtpMsg('SMTP ayarları kaydedildi');
      setSmtpMsgType('success');
      if (smtp.password) setHasPassword(true);
      setSmtp(prev => ({ ...prev, password: '' }));
    } catch (e: any) { setSmtpMsg(e.message); setSmtpMsgType('error'); }
    finally { setSaving(false); setTimeout(() => setSmtpMsg(''), 4000); }
  };

  const testSmtp = async () => {
    setTesting(true); setTestMsg('');
    try {
      const res = await apiClient.post<{ success: boolean; message: string }>('/api/app-settings/smtp/test', {});
      setTestMsg(res.message);
      setTestMsgType(res.success ? 'success' : 'error');
    } catch (e: any) { setTestMsg(e.message); setTestMsgType('error'); }
    finally { setTesting(false); setTimeout(() => setTestMsg(''), 6000); }
  };

  const sendTestEmail = async () => {
    setSendingTestEmail(true); setDeliveryMsg('');
    try {
      const res = await apiClient.post<{ success: boolean; message: string }>('/api/app-settings/smtp/send-test-email', {});
      setDeliveryMsg(res.message);
      setDeliveryMsgType(res.success ? 'success' : 'error');
    } catch (e: any) { setDeliveryMsg(e.message); setDeliveryMsgType('error'); }
    finally { setSendingTestEmail(false); setTimeout(() => setDeliveryMsg(''), 8000); }
  };

  const sendBirthdayTestEmail = async () => {
    setSendingBirthdayEmail(true); setBirthdayMsg('');
    try {
      const res = await apiClient.post<{ success: boolean; message: string }>('/api/app-settings/smtp/send-birthday-test', {}, 90_000);
      setBirthdayMsg(res.message);
      setBirthdayMsgType(res.success ? 'success' : 'error');
    } catch (e: any) { setBirthdayMsg(e.message); setBirthdayMsgType('error'); }
    finally { setSendingBirthdayEmail(false); setTimeout(() => setBirthdayMsg(''), 8000); }
  };

  const saveAlarm = async () => {
    setAlarmMsg('');
    try {
      await apiClient.put('/api/app-settings/alarm', alarm);
      setAlarmMsg('Alarm ayarları kaydedildi');
      setTimeout(() => setAlarmMsg(''), 3000);
    } catch (e: any) { setAlarmMsg(e.message); }
  };

  const toggleRole = (role: string) => {
    setAlarm(prev => {
      const roles = prev.alertRecipientRoles.includes(role)
        ? prev.alertRecipientRoles.filter(r => r !== role)
        : [...prev.alertRecipientRoles, role];
      return { ...prev, alertRecipientRoles: roles };
    });
  };

  const recipientCount = users.filter(u => u.email && alarm.alertRecipientRoles.includes(u.role)).length;

  return (
    <div className="space-y-6 max-w-2xl">
      {/* SMTP Ayarlari */}
      <div>
        <h2 className="text-lg font-bold text-ms-text flex items-center gap-2 mb-4">
          <Mail className="w-5 h-5 text-violet-400" /> SMTP Ayarları
        </h2>
        <div className="bg-ms-bg-soft border border-ms-border rounded-xl p-5 space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="form-label">Sunucu (Host)</label>
              <input value={smtp.host} onChange={e => setSmtp({ ...smtp, host: e.target.value })}
                className="w-full text-sm" placeholder="smtp.mudo.com.tr" />
            </div>
            <div>
              <label className="form-label">Port</label>
              <input type="number" value={smtp.port} onChange={e => setSmtp({ ...smtp, port: parseInt(e.target.value) || 587 })}
                className="w-full text-sm" />
            </div>
            <div>
              <label className="form-label">Kullanıcı Adı</label>
              <input value={smtp.username} onChange={e => setSmtp({ ...smtp, username: e.target.value })}
                className="w-full text-sm" placeholder="orchestra@sirket.com" />
            </div>
            <div>
              <label className="form-label">Şifre {hasPassword && <span className="text-emerald-400 text-[10px]">(kayıtlı)</span>}</label>
              <input type="password" value={smtp.password} onChange={e => setSmtp({ ...smtp, password: e.target.value })}
                className="w-full text-sm" placeholder={hasPassword ? '••••••••' : 'SMTP şifresi'} />
            </div>
            <div>
              <label className="form-label">Gönderen Adres</label>
              <input value={smtp.fromAddress} onChange={e => setSmtp({ ...smtp, fromAddress: e.target.value })}
                className="w-full text-sm" placeholder="orchestra@sirket.com" />
            </div>
            <div>
              <label className="form-label">Gönderen İsim</label>
              <input value={smtp.fromName} onChange={e => setSmtp({ ...smtp, fromName: e.target.value })}
                className="w-full text-sm" placeholder="Orchestra" />
            </div>
          </div>
          <label className="flex items-center gap-2 text-sm text-ms-text cursor-pointer">
            <input type="checkbox" checked={smtp.useSsl} onChange={e => setSmtp({ ...smtp, useSsl: e.target.checked })}
              className="rounded" />
            SSL/TLS kullan (port 465 için)
          </label>

          {smtpMsg && (
            <div className={`text-xs rounded-lg px-3 py-2 border ${
              smtpMsgType === 'success' ? 'text-emerald-400 bg-emerald-500/10 border-emerald-500/20'
                : 'text-red-400 bg-red-500/10 border-red-500/20'
            }`}>{smtpMsg}</div>
          )}
          {testMsg && (
            <div className={`text-xs rounded-lg px-3 py-2 border ${
              testMsgType === 'success' ? 'text-emerald-400 bg-emerald-500/10 border-emerald-500/20'
                : 'text-red-400 bg-red-500/10 border-red-500/20'
            }`}>{testMsg}</div>
          )}
          {deliveryMsg && (
            <div className={`text-xs rounded-lg px-3 py-2 border ${
              deliveryMsgType === 'success' ? 'text-emerald-400 bg-emerald-500/10 border-emerald-500/20'
                : 'text-red-400 bg-red-500/10 border-red-500/20'
            }`}>{deliveryMsg}</div>
          )}
          {birthdayMsg && (
            <div className={`text-xs rounded-lg px-3 py-2 border ${
              birthdayMsgType === 'success' ? 'text-emerald-400 bg-emerald-500/10 border-emerald-500/20'
                : 'text-red-400 bg-red-500/10 border-red-500/20'
            }`}>{birthdayMsg}</div>
          )}

          <div className="flex gap-2 flex-wrap">
            <button onClick={saveSmtp} disabled={saving} className="btn-primary text-sm">
              <Save className="w-3.5 h-3.5" /> {saving ? 'Kaydediliyor...' : 'Kaydet'}
            </button>
            <button onClick={testSmtp} disabled={testing || !smtp.host} className="btn-secondary text-sm">
              <Wifi className="w-3.5 h-3.5" /> {testing ? 'Test ediliyor...' : 'SMTP Testi'}
            </button>
            <button onClick={sendTestEmail} disabled={sendingTestEmail || !smtp.host} className="btn-secondary text-sm">
              <Send className="w-3.5 h-3.5" /> {sendingTestEmail ? 'Gonderiliyor...' : 'Test Maili Gonder'}
            </button>
            <button onClick={sendBirthdayTestEmail} disabled={sendingBirthdayEmail || !smtp.host} className="btn-secondary text-sm">
              <span className="text-base leading-none">🎂</span> {sendingBirthdayEmail ? 'Gönderiliyor...' : 'Doğum Günü Test'}
            </button>
          </div>
          <p className="text-[11px] text-ms-text-muted">
            Test maili, oturum açmış admin kullanıcısının e-posta adresine gönderilir.
          </p>
        </div>
      </div>

      {/* Alarm Ayarlari */}
      <div>
        <h2 className="text-lg font-bold text-ms-text flex items-center gap-2 mb-4">
          <Bell className="w-5 h-5 text-violet-400" /> Alarm Ayarları
        </h2>
        <div className="bg-ms-bg-soft border border-ms-border rounded-xl p-5 space-y-4">
          <label className="flex items-center gap-3 cursor-pointer">
            <div className={`w-10 h-6 rounded-full relative transition-colors ${
              alarm.emailAlertsEnabled ? 'bg-violet-600' : 'bg-slate-700'
            }`} onClick={() => setAlarm(prev => ({ ...prev, emailAlertsEnabled: !prev.emailAlertsEnabled }))}>
              <div className={`absolute top-1 w-4 h-4 rounded-full bg-white shadow transition-transform ${
                alarm.emailAlertsEnabled ? 'left-5' : 'left-1'
              }`} />
            </div>
            <span className="text-sm text-ms-text font-medium">E-posta Bildirimleri Aktif</span>
          </label>

          <div>
            <label className="form-label">Alıcı Roller</label>
            <div className="flex gap-3">
              {['Admin', 'Teknisyen'].map(role => (
                <label key={role} className="flex items-center gap-2 text-sm text-ms-text cursor-pointer">
                  <input type="checkbox" checked={alarm.alertRecipientRoles.includes(role)}
                    onChange={() => toggleRole(role)} className="rounded" />
                  {role}
                </label>
              ))}
            </div>
            <p className="text-[11px] text-ms-text-muted mt-1">
              Secili rollerdeki e-posta adresi tanimli kullanici sayisi: <strong className="text-violet-400">{recipientCount}</strong>
            </p>
          </div>

          <div>
            <label className="form-label">Cooldown Süresi (dakika)</label>
            <input type="number" value={alarm.cooldownMinutes}
              onChange={e => setAlarm({ ...alarm, cooldownMinutes: parseInt(e.target.value) || 30 })}
              className="w-32 text-sm" min={5} max={1440} />
            <p className="text-[11px] text-ms-text-muted mt-1">
              Aynı cihaz için tekrar alarm gönderilmeden önce bekleme süresi
            </p>
          </div>

          {alarmMsg && (
            <div className="text-xs text-emerald-400 bg-emerald-500/10 border border-emerald-500/20 rounded-lg px-3 py-2">
              {alarmMsg}
            </div>
          )}

          <button onClick={saveAlarm} className="btn-primary text-sm">
            <Save className="w-3.5 h-3.5" /> Kaydet
          </button>
        </div>
      </div>

      {/* Kullanici E-postalari */}
      <div>
        <h2 className="text-lg font-bold text-ms-text flex items-center gap-2 mb-4">
          <Send className="w-5 h-5 text-violet-400" /> Kullanıcı E-postaları
        </h2>
        <div className="bg-ms-bg-soft border border-ms-border rounded-xl overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-ms-border text-ms-text-muted text-xs">
                <th className="text-left px-4 py-3">Kullanıcı</th>
                <th className="text-left px-4 py-3">Rol</th>
                <th className="text-left px-4 py-3">E-posta</th>
              </tr>
            </thead>
            <tbody>
              {users.map((u, i) => (
                <tr key={i} className="border-b border-ms-border/50 hover:bg-ms-border/30 transition-colors">
                  <td className="px-4 py-2.5 text-ms-text">{u.fullName}</td>
                  <td className="px-4 py-2.5">
                    <span className={`inline-flex items-center gap-1 text-xs font-medium px-2 py-0.5 rounded-full ${
                      u.role === 'Admin' ? 'bg-violet-500/10 text-violet-400' : 'bg-sky-500/10 text-sky-400'
                    }`}>{u.role}</span>
                  </td>
                  <td className="px-4 py-2.5 text-xs font-mono text-ms-text-muted">
                    {u.email || <span className="text-rose-400">Tanımsız</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="text-[11px] text-ms-text-muted mt-2">
          E-posta adreslerini düzenlemek için <strong className="text-violet-400">Kullanıcı Yönetimi</strong> sekmesini kullanın.
        </p>
      </div>
    </div>
  );
};

export default SettingsPage;
