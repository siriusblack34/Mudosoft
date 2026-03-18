import React, { useEffect, useState } from 'react';
import { apiClient } from '../lib/apiClient';
import { useAuth } from '../contexts/AuthContext';
import {
  Users, Store, Plus, Trash2, KeyRound, Edit3, Save, X,
  Shield, Wrench, Clock, CheckCircle, XCircle, Search, Lock
} from 'lucide-react';

// ─── Types ───
interface UserItem {
  id: number; username: string; fullName: string; role: string;
  isActive: boolean; createdAt: string; lastLoginAt: string | null;
}
interface LoginHistoryItem {
  id: number; username: string; loginAt: string; ipAddress: string | null; success: boolean;
}
interface StoreDeviceItem {
  deviceId: string; storeCode: number; storeName: string; deviceType: string;
  deviceName: string; calculatedIpAddress: string; dbConnectionString: string;
  isTemporarilyClosed: boolean; temporaryCloseReason: string | null; lastSeen: string | null;
}

type Tab = 'password' | 'users' | 'stores' | 'logins';

const SettingsPage: React.FC = () => {
  const { isAdmin } = useAuth();
  const [tab, setTab] = useState<Tab>('password');

  const allTabs = [
    { id: 'password' as Tab, label: 'Şifre Değiştir', icon: Lock, adminOnly: false },
    { id: 'users' as Tab, label: 'Kullanıcı Yönetimi', icon: Users, adminOnly: true },
    { id: 'stores' as Tab, label: 'Mağaza / Kasa', icon: Store, adminOnly: true },
    { id: 'logins' as Tab, label: 'Giriş Geçmişi', icon: Clock, adminOnly: false },
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
      {tab === 'logins' && <LoginHistoryPanel />}
    </div>
  );
};

// ════════════════════════════════════════
// USER MANAGEMENT
// ════════════════════════════════════════
const UserManagement: React.FC = () => {
  const [users, setUsers] = useState<UserItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [resetId, setResetId] = useState<number | null>(null);
  const [form, setForm] = useState({ username: '', password: '', fullName: '', role: 'Teknisyen' });
  const [editForm, setEditForm] = useState({ fullName: '', role: '', isActive: true });
  const [newPass, setNewPass] = useState('');
  const [msg, setMsg] = useState('');

  const load = async () => {
    try {
      const data = await apiClient.get<UserItem[]>('/api/users');
      setUsers(data);
    } catch { }
    setLoading(false);
  };

  useEffect(() => { load(); }, []);

  const handleCreate = async () => {
    try {
      await apiClient.post('/api/users', form);
      setShowCreate(false);
      setForm({ username: '', password: '', fullName: '', role: 'Teknisyen' });
      load();
    } catch (e: any) { setMsg(e.message); }
  };

  const handleEdit = async (id: number) => {
    try {
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
              <th className="text-left px-4 py-3">Kullanıcı</th>
              <th className="text-left px-4 py-3">Ad Soyad</th>
              <th className="text-left px-4 py-3">Rol</th>
              <th className="text-left px-4 py-3">Durum</th>
              <th className="text-left px-4 py-3">Son Giriş</th>
              <th className="text-right px-4 py-3">İşlem</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={6} className="text-center py-8 text-ms-text-muted">Yükleniyor...</td></tr>
            ) : users.map(u => (
              <tr key={u.id} className="border-b border-ms-border/50 hover:bg-ms-border/30 transition-colors">
                <td className="px-4 py-3 font-mono text-ms-text">{u.username}</td>
                <td className="px-4 py-3 text-ms-text">
                  {editId === u.id ? (
                    <input value={editForm.fullName} onChange={e => setEditForm({ ...editForm, fullName: e.target.value })}
                      className="text-sm w-full" />
                  ) : u.fullName}
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
                        <button onClick={() => { setEditId(u.id); setEditForm({ fullName: u.fullName, role: u.role, isActive: u.isActive }); }}
                          className="p-1.5 rounded-lg hover:bg-sky-500/10 text-sky-400" title="Düzenle"><Edit3 className="w-3.5 h-3.5" /></button>
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
  const [editId, setEditId] = useState<string | null>(null);
  const [form, setForm] = useState({ storeCode: '', storeName: '', deviceType: 'Kasa-1', deviceName: '', calculatedIpAddress: '', dbConnectionString: '' });
  const [editForm, setEditForm] = useState({ storeName: '', deviceName: '', deviceType: '', calculatedIpAddress: '', dbConnectionString: '' });
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
        <button onClick={() => setShowCreate(true)} className="btn-primary text-sm shrink-0">
          <Plus className="w-4 h-4" /> Yeni Cihaz
        </button>
      </div>

      {msg && (
        <div className="text-xs text-amber-400 bg-amber-500/10 border border-amber-500/20 rounded-lg px-3 py-2">
          {msg} <button onClick={() => setMsg('')} className="ml-2 text-ms-text-muted">x</button>
        </div>
      )}

      {/* Create Form */}
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
                              <button onClick={() => { setEditId(d.deviceId); setEditForm({ storeName: d.storeName, deviceName: d.deviceName, deviceType: d.deviceType, calculatedIpAddress: d.calculatedIpAddress, dbConnectionString: d.dbConnectionString }); }}
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

export default SettingsPage;
