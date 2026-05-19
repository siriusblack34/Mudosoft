import React, { useEffect, useState } from 'react';
import { apiClient } from '../lib/apiClient';
import { useAuth } from '../contexts/AuthContext';
import {
  Users, Store, Plus, Trash2, KeyRound, Edit3, Save, X,
  Shield, Wrench, Clock, CheckCircle, XCircle, Search, Lock, Menu, Eye, EyeOff,
  Bell, Mail, Send, Wifi, RefreshCw
} from 'lucide-react';
import { useMenuVisibility } from '../contexts/MenuVisibilityContext';
import { navGroups } from '../layout/sidebar';

// ─── Types ───
interface UserItem {
  id: number; username: string; fullName: string; role: string;
  isActive: boolean; createdAt: string; lastLoginAt: string | null;
  email: string | null;
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
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [resetId, setResetId] = useState<number | null>(null);
  const [form, setForm] = useState({ username: '', password: '', fullName: '', role: 'Teknisyen', email: '' });
  const [editForm, setEditForm] = useState({ fullName: '', role: '', isActive: true, email: '' });
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
      setForm({ username: '', password: '', fullName: '', role: 'Teknisyen', email: '' });
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
              <th className="text-left px-4 py-3">Kullanıcı</th>
              <th className="text-left px-4 py-3">Ad Soyad</th>
              <th className="text-left px-4 py-3">E-posta</th>
              <th className="text-left px-4 py-3">Rol</th>
              <th className="text-left px-4 py-3">Durum</th>
              <th className="text-left px-4 py-3">Son Giriş</th>
              <th className="text-right px-4 py-3">İşlem</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={7} className="text-center py-8 text-ms-text-muted">Yükleniyor...</td></tr>
            ) : users.map(u => (
              <tr key={u.id} className="border-b border-ms-border/50 hover:bg-ms-border/30 transition-colors">
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
                        <button onClick={() => { setEditId(u.id); setEditForm({ fullName: u.fullName, role: u.role, isActive: u.isActive, email: u.email || '' }); }}
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

// ════════════════════════════════════════
// MENU MANAGEMENT
// ════════════════════════════════════════
const MenuManagementPanel: React.FC = () => {
  const { hiddenMenus, setHiddenMenus } = useMenuVisibility();
  const [localHidden, setLocalHidden] = useState<Set<string>>(new Set(hiddenMenus));
  const [saving, setSaving] = useState(false);
  const [msg, setMsg] = useState('');

  useEffect(() => {
    setLocalHidden(new Set(hiddenMenus));
  }, [hiddenMenus]);

  const allPaths = navGroups.flatMap(g => g.items.map(i => i.to));
  // Ayarlar sayfasi her zaman gorunur olmali
  const configurablePaths = allPaths.filter(p => p !== '/settings' && p !== '/');

  const toggle = (path: string) => {
    setLocalHidden(prev => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      await setHiddenMenus(Array.from(localHidden));
      setMsg('Kaydedildi');
      setTimeout(() => setMsg(''), 3000);
    } catch (e: any) {
      setMsg(e.message || 'Hata olustu');
    } finally {
      setSaving(false);
    }
  };

  const hideAll = () => setLocalHidden(new Set(configurablePaths));
  const showAll = () => setLocalHidden(new Set());

  const hasChanges = (() => {
    const current = new Set(hiddenMenus);
    if (current.size !== localHidden.size) return true;
    for (const p of localHidden) if (!current.has(p)) return true;
    return false;
  })();

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-bold text-ms-text">Teknisyen Menü Yönetimi</h2>
          <p className="text-xs text-ms-text-muted mt-0.5">
            Teknisyenlerin sidebar'da gorebilecegi sayfalari ayarlayin. Admin her zaman tum menuleri gorur.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button onClick={showAll} className="btn-secondary text-xs">
            <Eye className="w-3.5 h-3.5" /> Tümünü Göster
          </button>
          <button onClick={hideAll} className="btn-secondary text-xs">
            <EyeOff className="w-3.5 h-3.5" /> Tümünü Gizle
          </button>
          <button onClick={handleSave} disabled={saving || !hasChanges}
            className="btn-primary text-xs disabled:opacity-40">
            <Save className="w-3.5 h-3.5" />
            {saving ? 'Kaydediliyor...' : 'Kaydet'}
          </button>
        </div>
      </div>

      {msg && (
        <div className="text-xs text-emerald-400 bg-emerald-500/10 border border-emerald-500/20 rounded-lg px-3 py-2">
          {msg}
        </div>
      )}

      <div className="space-y-3">
        {navGroups.map(group => {
          const items = group.items.filter(i => i.to !== '/settings' && i.to !== '/');
          if (items.length === 0) return null;

          const groupHiddenCount = items.filter(i => localHidden.has(i.to)).length;

          return (
            <div key={group.title} className="bg-ms-bg-soft border border-ms-border rounded-xl overflow-hidden">
              <div className="flex items-center justify-between px-4 py-2.5 border-b border-ms-border/50"
                style={{ background: 'rgba(255,255,255,0.01)' }}>
                <span className="text-sm font-semibold text-ms-text">{group.title}</span>
                <span className="text-[10px] text-ms-text-muted">
                  {items.length - groupHiddenCount}/{items.length} gorunur
                </span>
              </div>
              <div className="divide-y divide-ms-border/30">
                {items.map(item => {
                  const isHidden = localHidden.has(item.to);
                  return (
                    <button
                      key={item.to}
                      onClick={() => toggle(item.to)}
                      className={`w-full flex items-center gap-3 px-4 py-2.5 text-left transition-all hover:bg-white/[0.02] ${
                        isHidden ? 'opacity-40' : ''
                      }`}
                    >
                      <div className={`w-8 h-5 rounded-full relative transition-colors ${
                        isHidden ? 'bg-slate-700' : 'bg-violet-600'
                      }`}>
                        <div className={`absolute top-0.5 w-4 h-4 rounded-full bg-white shadow transition-transform ${
                          isHidden ? 'left-0.5' : 'left-3.5'
                        }`} />
                      </div>
                      <span className="text-ms-text-muted">{item.icon}</span>
                      <span className="text-sm text-ms-text font-medium flex-1">{item.label}</span>
                      <span className="text-[10px] font-mono text-ms-text-muted">{item.to}</span>
                      {isHidden ? (
                        <EyeOff className="w-3.5 h-3.5 text-slate-500" />
                      ) : (
                        <Eye className="w-3.5 h-3.5 text-violet-400" />
                      )}
                    </button>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>

      <div className="rounded-lg px-4 py-3 text-[11px]"
        style={{ background: 'rgba(139,92,246,0.05)', border: '1px solid rgba(139,92,246,0.15)', color: 'var(--ms-text-muted)' }}>
        <strong className="text-violet-400">Not:</strong> Kontrol Paneli ve Ayarlar sayfalari her zaman gorunurdur.
        Gizlenen sayfalar yalnizca teknisyen kullanicilarin sidebar'indan kaldirilir.
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

          <div className="flex gap-2">
            <button onClick={saveSmtp} disabled={saving} className="btn-primary text-sm">
              <Save className="w-3.5 h-3.5" /> {saving ? 'Kaydediliyor...' : 'Kaydet'}
            </button>
            <button onClick={testSmtp} disabled={testing || !smtp.host} className="btn-secondary text-sm">
              <Wifi className="w-3.5 h-3.5" /> {testing ? 'Test ediliyor...' : 'SMTP Testi'}
            </button>
            <button onClick={sendTestEmail} disabled={sendingTestEmail || !smtp.host} className="btn-secondary text-sm">
              <Send className="w-3.5 h-3.5" /> {sendingTestEmail ? 'Gonderiliyor...' : 'Test Maili Gonder'}
            </button>
          </div>
          <p className="text-[11px] text-ms-text-muted">
            Test maili, oturum acmis admin kullanicisinin e-posta adresine gonderilir.
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
