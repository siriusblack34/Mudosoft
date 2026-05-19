import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ChevronRight, ChevronDown, Folder, FolderOpen, Monitor, User as UserIcon, Users, RefreshCw, Download, AlertCircle, CheckCircle2, XCircle, Circle, AlertTriangle, Loader2, Search, Clock, X, RotateCcw } from 'lucide-react';
import { apiClient } from '../lib/apiClient';

interface InstallStep {
  name: string;
  state: 'pending' | 'running' | 'done' | 'error' | 'warn';
  detail: string;
}
interface InstallStatus {
  id: string;
  ipAddress: string;
  storeCode: string;
  phase: 'pending' | 'running' | 'done' | 'warn' | 'error';
  error?: string;
  steps: InstallStep[];
  startedAt: string;
  completedAt?: string;
}
interface InstallProgress {
  hostname: string;
  ip?: string;
  ok: boolean;
  error?: string;
  status?: InstallStatus;
  polling?: boolean;
}

const StepIcon: React.FC<{ state: string }> = ({ state }) => {
  switch (state) {
    case 'running': return <Loader2 className="w-4 h-4 text-blue-400 animate-spin" />;
    case 'done':    return <CheckCircle2 className="w-4 h-4 text-emerald-400" />;
    case 'warn':    return <AlertTriangle className="w-4 h-4 text-amber-400" />;
    case 'error':   return <XCircle className="w-4 h-4 text-red-400" />;
    default:        return <Circle className="w-4 h-4 text-gray-600" />;
  }
};

interface AdOu { dn: string; name: string; parentDn: string | null; computerCount: number; }
interface AdGroup { dn: string; name: string; description: string | null; memberCount: number; }
interface AdUser {
  dn: string;
  samAccountName: string;
  displayName: string | null;
  userPrincipalName: string | null;
  email: string | null;
  description: string | null;
  lastLogon: string | null;
  enabled: boolean;
  ouDn: string | null;
}
type PendingStatus = 'Waiting' | 'Matched' | 'Installing' | 'Done' | 'Failed' | 'Expired' | 'Cancelled';
interface PendingUserInstall {
  id: number;
  samAccountName: string;
  displayName: string | null;
  requestedBy: string | null;
  requestedAt: string;
  expiresAt: string;
  status: PendingStatus;
  matchedComputer: string | null;
  matchedIp: string | null;
  matchedAt: string | null;
  installId: string | null;
  lastError: string | null;
  updatedAt: string | null;
}
interface AdComputer {
  dn: string;
  name: string;
  dnsHostName: string | null;
  operatingSystem: string | null;
  operatingSystemVersion: string | null;
  description: string | null;
  lastLogon: string | null;
  enabled: boolean;
  ouDn: string | null;
}

interface OuTreeNode extends AdOu { children: OuTreeNode[]; }

function buildOuTree(flat: AdOu[]): OuTreeNode[] {
  const byDn = new Map<string, OuTreeNode>();
  flat.forEach(o => byDn.set(o.dn, { ...o, children: [] }));
  const roots: OuTreeNode[] = [];
  byDn.forEach(node => {
    if (node.parentDn && byDn.has(node.parentDn)) {
      byDn.get(node.parentDn)!.children.push(node);
    } else {
      roots.push(node);
    }
  });
  const sortRec = (nodes: OuTreeNode[]) => {
    nodes.sort((a, b) => a.name.localeCompare(b.name, 'tr'));
    nodes.forEach(n => sortRec(n.children));
  };
  sortRec(roots);
  return roots;
}

function relativeDate(iso: string | null): string {
  if (!iso) return '—';
  const d = new Date(iso);
  const days = Math.floor((Date.now() - d.getTime()) / 86400000);
  if (days < 1) return 'bugün';
  if (days < 30) return `${days} gün önce`;
  if (days < 365) return `${Math.floor(days / 30)} ay önce`;
  return `${Math.floor(days / 365)} yıl önce`;
}

const ActiveDirectoryPage: React.FC = () => {
  const [tab, setTab] = useState<'ou' | 'group' | 'user'>('ou');
  const [ous, setOus] = useState<AdOu[]>([]);
  const [groups, setGroups] = useState<AdGroup[]>([]);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [selectedNode, setSelectedNode] = useState<{ type: 'ou' | 'group'; dn: string; name: string } | null>(null);
  const [computers, setComputers] = useState<AdComputer[]>([]);
  const [users, setUsers] = useState<AdUser[]>([]);
  const [userChecked, setUserChecked] = useState<Set<string>>(new Set());
  const [pendingInstalls, setPendingInstalls] = useState<PendingUserInstall[]>([]);
  const [schedulingUser, setSchedulingUser] = useState(false);
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [search, setSearch] = useState('');
  const [loadingTree, setLoadingTree] = useState(false);
  const [loadingComputers, setLoadingComputers] = useState(false);
  const [installing, setInstalling] = useState(false);
  const [installProgress, setInstallProgress] = useState<InstallProgress[] | null>(null);
  const pollTimersRef = useRef<Map<string, number>>(new Map());
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    return () => {
      pollTimersRef.current.forEach(id => window.clearTimeout(id));
      pollTimersRef.current.clear();
    };
  }, []);

  const ouTree = useMemo(() => buildOuTree(ous), [ous]);

  useEffect(() => { loadTree(); }, []);

  async function loadTree() {
    setLoadingTree(true);
    setError(null);
    try {
      const [ouRes, groupRes] = await Promise.all([
        apiClient.get<{ ous: AdOu[] }>('/api/ad/ous', 30000),
        apiClient.get<{ groups: AdGroup[] }>('/api/ad/groups', 30000),
      ]);
      setOus(ouRes.ous);
      setGroups(groupRes.groups);
    } catch (e: any) {
      setError(e?.message || 'AD bilgileri alınamadı');
    } finally {
      setLoadingTree(false);
    }
  }

  async function selectOu(node: OuTreeNode) {
    setSelectedNode({ type: 'ou', dn: node.dn, name: node.name });
    setChecked(new Set());
    setUserChecked(new Set());
    setLoadingComputers(true);
    setError(null);
    try {
      if (tab === 'user') {
        const url = `/api/ad/users?ou=${encodeURIComponent(node.dn)}&recursive=false`;
        const res = await apiClient.get<{ users: AdUser[] }>(url, 30000);
        setUsers(res.users);
        setComputers([]);
      } else {
        const url = `/api/ad/computers?ou=${encodeURIComponent(node.dn)}&recursive=false`;
        const res = await apiClient.get<{ computers: AdComputer[] }>(url, 30000);
        setComputers(res.computers);
        setUsers([]);
      }
    } catch (e: any) {
      setError(e?.message || 'Veri alınamadı');
      setComputers([]); setUsers([]);
    } finally {
      setLoadingComputers(false);
    }
  }

  async function loadPendingInstalls() {
    try {
      const res = await apiClient.get<{ items: PendingUserInstall[] }>('/api/user-install', 15000);
      setPendingInstalls(res.items);
    } catch { /* silent */ }
  }

  useEffect(() => {
    loadPendingInstalls();
    const id = window.setInterval(loadPendingInstalls, 10000);
    return () => window.clearInterval(id);
  }, []);

  async function scheduleUserInstall() {
    if (userChecked.size === 0) return;
    setSchedulingUser(true);
    setError(null);
    try {
      const selected = users.filter(u => userChecked.has(u.samAccountName));
      await apiClient.post('/api/user-install', {
        users: selected.map(u => ({ samAccountName: u.samAccountName, displayName: u.displayName }))
      }, 30000);
      setUserChecked(new Set());
      await loadPendingInstalls();
    } catch (e: any) {
      setError(e?.message || 'Planlama başarısız');
    } finally {
      setSchedulingUser(false);
    }
  }

  async function cancelPending(id: number) {
    try {
      await apiClient.delete(`/api/user-install/${id}`);
      await loadPendingInstalls();
    } catch (e: any) { setError(e?.message || 'İptal başarısız'); }
  }

  async function retryPending(id: number) {
    try {
      await apiClient.post(`/api/user-install/${id}/retry`, {});
      await loadPendingInstalls();
    } catch (e: any) { setError(e?.message || 'Yeniden başlatma başarısız'); }
  }

  async function selectGroup(g: AdGroup) {
    setSelectedNode({ type: 'group', dn: g.dn, name: g.name });
    setChecked(new Set());
    setLoadingComputers(true);
    setError(null);
    try {
      const url = `/api/ad/computers?group=${encodeURIComponent(g.dn)}`;
      const res = await apiClient.get<{ computers: AdComputer[] }>(url, 30000);
      setComputers(res.computers);
    } catch (e: any) {
      setError(e?.message || 'Bilgisayarlar alınamadı');
      setComputers([]);
    } finally {
      setLoadingComputers(false);
    }
  }

  function toggleExpand(dn: string) {
    setExpanded(prev => {
      const next = new Set(prev);
      next.has(dn) ? next.delete(dn) : next.add(dn);
      return next;
    });
  }

  function toggleCheck(name: string) {
    setChecked(prev => {
      const next = new Set(prev);
      next.has(name) ? next.delete(name) : next.add(name);
      return next;
    });
  }

  function toggleAll() {
    if (checked.size === filteredComputers.length) {
      setChecked(new Set());
    } else {
      setChecked(new Set(filteredComputers.filter(c => c.enabled).map(c => c.dnsHostName || c.name)));
    }
  }

  const filteredComputers = useMemo(() => {
    if (!search.trim()) return computers;
    const q = search.toLowerCase();
    return computers.filter(c =>
      c.name.toLowerCase().includes(q) ||
      (c.dnsHostName || '').toLowerCase().includes(q) ||
      (c.description || '').toLowerCase().includes(q) ||
      (c.operatingSystem || '').toLowerCase().includes(q)
    );
  }, [computers, search]);

  const filteredUsers = useMemo(() => {
    if (!search.trim()) return users;
    const q = search.toLowerCase();
    return users.filter(u =>
      u.samAccountName.toLowerCase().includes(q) ||
      (u.displayName || '').toLowerCase().includes(q) ||
      (u.userPrincipalName || '').toLowerCase().includes(q) ||
      (u.email || '').toLowerCase().includes(q)
    );
  }, [users, search]);

  const pendingActiveByUser = useMemo(() => {
    const map = new Map<string, PendingUserInstall>();
    for (const p of pendingInstalls) {
      if (p.status === 'Waiting' || p.status === 'Matched' || p.status === 'Installing') {
        map.set(p.samAccountName.toLowerCase(), p);
      }
    }
    return map;
  }, [pendingInstalls]);

  const pollStatus = useCallback((hostname: string, ip: string) => {
    const tick = async () => {
      try {
        const status = await apiClient.get<InstallStatus>(
          `/api/agent/remote-install/status?ip=${encodeURIComponent(ip)}`
        );
        setInstallProgress(prev => prev?.map(p =>
          p.hostname === hostname ? { ...p, status, polling: status.phase === 'running' } : p
        ) || null);
        if (status.phase === 'running') {
          const id = window.setTimeout(tick, 1500);
          pollTimersRef.current.set(hostname, id);
        } else {
          pollTimersRef.current.delete(hostname);
        }
      } catch {
        const id = window.setTimeout(tick, 3000);
        pollTimersRef.current.set(hostname, id);
      }
    };
    tick();
  }, []);

  async function installSelected() {
    if (checked.size === 0) return;
    setInstalling(true);
    setInstallProgress(null);
    pollTimersRef.current.forEach(id => window.clearTimeout(id));
    pollTimersRef.current.clear();
    setError(null);
    try {
      const hostnames = Array.from(checked);

      // 1) Pre-flight probe: DNS + ping. Stale DNS / offline laptop'ları install denemeden ele.
      const probe = await apiClient.post<{ results: { hostname: string; ip?: string; alive: boolean; error?: string }[] }>(
        '/api/ad/probe',
        { hostnames },
        30000
      );
      const probeMap = new Map(probe.results.map(r => [r.hostname.toLowerCase(), r]));

      const alive = hostnames.filter(h => probeMap.get(h.toLowerCase())?.alive);
      const dead = hostnames.filter(h => !probeMap.get(h.toLowerCase())?.alive);

      const deadProgress: InstallProgress[] = dead.map(h => {
        const p = probeMap.get(h.toLowerCase());
        return {
          hostname: h,
          ip: p?.ip,
          ok: false,
          error: p?.error || 'Makine erişilemiyor (ping başarısız)',
        };
      });

      if (alive.length === 0) {
        setInstallProgress(deadProgress);
        return;
      }

      // 2) Sadece canlı hostlara install gönder
      const res = await apiClient.post<{ results: { hostname: string; ip?: string; ok: boolean; error?: string }[] }>(
        '/api/agent/remote-install/by-hostname',
        { hostnames: alive },
        60000
      );
      const liveProgress: InstallProgress[] = res.results.map(r => ({
        hostname: r.hostname,
        ip: r.ip,
        ok: r.ok,
        error: r.error,
        polling: r.ok && !!r.ip,
      }));
      setInstallProgress([...liveProgress, ...deadProgress]);
      for (const r of res.results) {
        if (r.ok && r.ip) pollStatus(r.hostname, r.ip);
      }
    } catch (e: any) {
      setError(e?.message || 'Kurulum başlatılamadı');
    } finally {
      setInstalling(false);
    }
  }

  function renderOuNode(node: OuTreeNode, depth = 0): React.ReactNode {
    const isExpanded = expanded.has(node.dn);
    const isSelected = selectedNode?.type === 'ou' && selectedNode.dn === node.dn;
    const hasChildren = node.children.length > 0;
    return (
      <div key={node.dn}>
        <div
          className={`flex items-center gap-1.5 py-1 pr-2 rounded cursor-pointer text-sm hover:bg-ms-bg-soft ${isSelected ? 'bg-violet-500/10 text-violet-300' : 'text-ms-text'}`}
          style={{ paddingLeft: 4 + depth * 14 }}
          onClick={() => selectOu(node)}
        >
          <span
            className="w-4 h-4 flex-shrink-0 inline-flex items-center justify-center"
            onClick={(e) => { e.stopPropagation(); if (hasChildren) toggleExpand(node.dn); }}
          >
            {hasChildren ? (isExpanded ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronRight className="w-3.5 h-3.5" />) : null}
          </span>
          {isExpanded && hasChildren ? <FolderOpen className="w-4 h-4 text-amber-400" /> : <Folder className="w-4 h-4 text-amber-500" />}
          <span className="truncate flex-1">{node.name}</span>
          {node.computerCount > 0 && (
            <span className="text-[10px] text-ms-text-muted px-1.5 py-0.5 rounded bg-ms-bg">{node.computerCount}</span>
          )}
        </div>
        {isExpanded && hasChildren && node.children.map(c => renderOuNode(c, depth + 1))}
      </div>
    );
  }

  const selectableCount = filteredComputers.filter(c => c.enabled).length;
  const allChecked = selectableCount > 0 && filteredComputers.filter(c => c.enabled).every(c => checked.has(c.dnsHostName || c.name));

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h1 className="text-2xl font-bold text-ms-text">Active Directory</h1>
          <p className="text-sm text-ms-text-muted">AD üzerinden bilgisayar keşfi ve agent kurulumu</p>
        </div>
        <button onClick={loadTree} className="btn-secondary text-sm" disabled={loadingTree}>
          <RefreshCw className={`w-4 h-4 ${loadingTree ? 'animate-spin' : ''}`} />
          Yenile
        </button>
      </div>

      {error && (
        <div className="mb-3 flex items-center gap-2 bg-red-500/10 border border-red-500/30 text-red-300 text-sm rounded-lg px-3 py-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          {error}
        </div>
      )}

      <div className="flex gap-4 flex-1 min-h-0">
        {/* Sol panel: OU/Grup */}
        <div className="w-72 flex flex-col bg-ms-bg-soft border border-ms-border rounded-lg overflow-hidden">
          <div className="flex border-b border-ms-border">
            <button
              className={`flex-1 px-3 py-2 text-sm font-medium transition ${tab === 'ou' ? 'bg-violet-500/15 text-violet-300' : 'text-ms-text-muted hover:bg-ms-bg'}`}
              onClick={() => setTab('ou')}
            >
              <Folder className="w-4 h-4 inline mr-1.5" />OU Ağacı
            </button>
            <button
              className={`flex-1 px-3 py-2 text-sm font-medium transition ${tab === 'group' ? 'bg-violet-500/15 text-violet-300' : 'text-ms-text-muted hover:bg-ms-bg'}`}
              onClick={() => setTab('group')}
            >
              <Users className="w-4 h-4 inline mr-1.5" />Gruplar
            </button>
            <button
              className={`flex-1 px-3 py-2 text-sm font-medium transition ${tab === 'user' ? 'bg-violet-500/15 text-violet-300' : 'text-ms-text-muted hover:bg-ms-bg'}`}
              onClick={() => { setTab('user'); setComputers([]); setUsers([]); setSelectedNode(null); }}
              title="Kullanıcı bazlı kurulum — kullanıcı seçince, ilk login olduğu makineye otomatik kurulur"
            >
              <UserIcon className="w-4 h-4 inline mr-1.5" />Kullanıcılar
            </button>
          </div>
          <div className="flex-1 overflow-y-auto p-2">
            {loadingTree ? (
              <div className="text-sm text-ms-text-muted p-2">Yükleniyor…</div>
            ) : tab === 'ou' || tab === 'user' ? (
              ouTree.length === 0 ? <div className="text-sm text-ms-text-muted p-2">OU bulunamadı</div>
                : ouTree.map(n => renderOuNode(n))
            ) : (
              groups.length === 0 ? <div className="text-sm text-ms-text-muted p-2">Grup bulunamadı</div>
                : groups.map(g => (
                  <div
                    key={g.dn}
                    onClick={() => selectGroup(g)}
                    className={`flex items-center gap-2 py-1.5 px-2 rounded cursor-pointer text-sm hover:bg-ms-bg ${selectedNode?.type === 'group' && selectedNode.dn === g.dn ? 'bg-violet-500/10 text-violet-300' : 'text-ms-text'}`}
                    title={g.description || g.dn}
                  >
                    <Users className="w-4 h-4 text-sky-400 shrink-0" />
                    <span className="truncate flex-1">{g.name}</span>
                    <span className="text-[10px] text-ms-text-muted">{g.memberCount}</span>
                  </div>
                ))
            )}
          </div>
        </div>

        {/* Sağ panel: bilgisayar listesi */}
        <div className="flex-1 flex flex-col bg-ms-bg-soft border border-ms-border rounded-lg overflow-hidden">
          <div className="flex items-center justify-between gap-3 px-3 py-2 border-b border-ms-border">
            <div className="flex-1 min-w-0">
              <div className="text-sm font-medium text-ms-text truncate">
                {selectedNode ? selectedNode.name : 'Bir OU veya grup seçin'}
              </div>
              {selectedNode && (
                <div className="text-[11px] text-ms-text-muted truncate" title={selectedNode.dn}>{selectedNode.dn}</div>
              )}
            </div>
            <div className="relative">
              <Search className="w-4 h-4 absolute left-2 top-1/2 -translate-y-1/2 text-ms-text-muted" />
              <input
                value={search}
                onChange={e => setSearch(e.target.value)}
                placeholder="Filtrele"
                className="pl-8 pr-2 py-1 text-sm bg-ms-bg border border-ms-border rounded w-48"
              />
            </div>
            {tab === 'user' ? (
              <button
                onClick={scheduleUserInstall}
                disabled={schedulingUser || userChecked.size === 0}
                className="btn-primary text-sm bg-emerald-600 hover:bg-emerald-500 disabled:opacity-40 text-white px-3 py-1.5 rounded-lg flex items-center gap-1.5"
                title="Seçili kullanıcılar bir makineye login olunca otomatik agent kurulur (10 gün TTL)"
              >
                <Clock className="w-4 h-4" />
                Login'de Kur ({userChecked.size})
              </button>
            ) : (
              <button
                onClick={installSelected}
                disabled={installing || checked.size === 0}
                className="btn-primary text-sm"
              >
                <Download className="w-4 h-4" />
                Agent Kur ({checked.size})
              </button>
            )}
          </div>

          <div className="flex-1 overflow-auto">
            {loadingComputers ? (
              <div className="p-4 text-sm text-ms-text-muted">Yükleniyor…</div>
            ) : !selectedNode ? (
              <div className="p-4 text-sm text-ms-text-muted">Sol panelden bir OU{tab === 'group' ? ' veya grup' : ''} seçin.</div>
            ) : tab === 'user' ? (
              filteredUsers.length === 0 ? (
                <div className="p-4 text-sm text-ms-text-muted">Bu OU'da kullanıcı bulunamadı.</div>
              ) : (
                <table className="w-full text-sm">
                  <thead className="sticky top-0 bg-ms-bg-soft border-b border-ms-border text-ms-text-muted text-xs uppercase">
                    <tr>
                      <th className="px-3 py-2 text-left w-8">
                        <input
                          type="checkbox"
                          checked={filteredUsers.filter(u => u.enabled).length > 0
                            && filteredUsers.filter(u => u.enabled).every(u => userChecked.has(u.samAccountName))}
                          onChange={() => {
                            if (filteredUsers.filter(u => u.enabled).every(u => userChecked.has(u.samAccountName))) {
                              setUserChecked(new Set());
                            } else {
                              setUserChecked(new Set(filteredUsers.filter(u => u.enabled).map(u => u.samAccountName)));
                            }
                          }}
                          className="rounded accent-emerald-500"
                        />
                      </th>
                      <th className="px-2 py-2 text-left">Kullanıcı</th>
                      <th className="px-2 py-2 text-left">SAM</th>
                      <th className="px-2 py-2 text-left">UPN / Email</th>
                      <th className="px-2 py-2 text-left">Son Logon</th>
                      <th className="px-2 py-2 text-left">Durum</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredUsers.map(u => {
                      const isChecked = userChecked.has(u.samAccountName);
                      const active = pendingActiveByUser.get(u.samAccountName.toLowerCase());
                      return (
                        <tr key={u.dn} className={`border-b border-ms-border/40 ${!u.enabled ? 'opacity-50' : 'hover:bg-ms-bg'}`}>
                          <td className="px-3 py-1.5">
                            <input
                              type="checkbox"
                              checked={isChecked}
                              disabled={!u.enabled || !!active}
                              onChange={() => {
                                setUserChecked(prev => {
                                  const next = new Set(prev);
                                  next.has(u.samAccountName) ? next.delete(u.samAccountName) : next.add(u.samAccountName);
                                  return next;
                                });
                              }}
                              className="rounded accent-emerald-500"
                            />
                          </td>
                          <td className="px-2 py-1.5">
                            <div className="flex items-center gap-2">
                              <UserIcon className="w-4 h-4 text-ms-text-muted" />
                              <span className="font-medium">{u.displayName || u.samAccountName}</span>
                              {!u.enabled && <span className="text-[10px] px-1 py-0.5 bg-red-500/15 text-red-400 rounded">disabled</span>}
                            </div>
                          </td>
                          <td className="px-2 py-1.5 font-mono text-ms-text-muted">{u.samAccountName}</td>
                          <td className="px-2 py-1.5 text-ms-text-muted truncate max-w-xs">{u.userPrincipalName || u.email || '—'}</td>
                          <td className="px-2 py-1.5 text-ms-text-muted">{relativeDate(u.lastLogon)}</td>
                          <td className="px-2 py-1.5">
                            {active ? (
                              <span className="text-[10px] px-1.5 py-0.5 rounded bg-amber-500/15 text-amber-400 border border-amber-500/20">
                                {active.status === 'Installing' ? 'Kuruluyor' : active.status === 'Matched' ? 'Bulundu' : 'Bekliyor'}
                              </span>
                            ) : (
                              <span className="text-[10px] text-ms-text-muted">—</span>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              )
            ) : filteredComputers.length === 0 ? (
              <div className="p-4 text-sm text-ms-text-muted">Bilgisayar bulunamadı.</div>
            ) : (
              <table className="w-full text-sm">
                <thead className="sticky top-0 bg-ms-bg-soft border-b border-ms-border text-ms-text-muted text-xs uppercase">
                  <tr>
                    <th className="px-3 py-2 text-left w-8">
                      <input type="checkbox" checked={allChecked} onChange={toggleAll} className="rounded accent-violet-500" />
                    </th>
                    <th className="px-2 py-2 text-left">Bilgisayar</th>
                    <th className="px-2 py-2 text-left">DNS</th>
                    <th className="px-2 py-2 text-left">OS</th>
                    <th className="px-2 py-2 text-left">Son Logon</th>
                    <th className="px-2 py-2 text-left">Açıklama</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredComputers.map(c => {
                    const key = c.dnsHostName || c.name;
                    const isChecked = checked.has(key);
                    return (
                      <tr key={c.dn} className={`border-b border-ms-border/40 ${!c.enabled ? 'opacity-50' : 'hover:bg-ms-bg'}`}>
                        <td className="px-3 py-1.5">
                          <input
                            type="checkbox"
                            checked={isChecked}
                            disabled={!c.enabled}
                            onChange={() => toggleCheck(key)}
                            className="rounded accent-violet-500"
                          />
                        </td>
                        <td className="px-2 py-1.5">
                          <div className="flex items-center gap-2">
                            <Monitor className="w-4 h-4 text-ms-text-muted" />
                            <span className="font-medium">{c.name}</span>
                            {!c.enabled && <span className="text-[10px] px-1 py-0.5 bg-red-500/15 text-red-400 rounded">disabled</span>}
                          </div>
                        </td>
                        <td className="px-2 py-1.5 text-ms-text-muted">{c.dnsHostName || '—'}</td>
                        <td className="px-2 py-1.5 text-ms-text-muted">{c.operatingSystem || '—'}</td>
                        <td className="px-2 py-1.5 text-ms-text-muted">{relativeDate(c.lastLogon)}</td>
                        <td className="px-2 py-1.5 text-ms-text-muted truncate max-w-xs" title={c.description || ''}>{c.description || '—'}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
          </div>
        </div>
      </div>

      {installProgress && (
        <div className="fixed bottom-4 right-4 w-[480px] max-w-[calc(100vw-2rem)] bg-ms-bg-soft border border-ms-border rounded-lg p-3 shadow-2xl z-50">
          <div className="flex items-center justify-between mb-2">
            <div className="text-sm font-medium text-ms-text">Kurulum İlerlemesi</div>
            <button
              onClick={() => {
                pollTimersRef.current.forEach(id => window.clearTimeout(id));
                pollTimersRef.current.clear();
                setInstallProgress(null);
              }}
              className="text-[11px] text-ms-text-muted hover:text-ms-text"
            >
              Temizle
            </button>
          </div>
          <div className="space-y-2 max-h-[70vh] overflow-auto pr-1">
            {installProgress.map(p => {
              const phase = p.status?.phase;
              return (
                <div key={p.hostname} className="border border-ms-border/60 rounded-lg">
                  <div className="flex items-center gap-2 px-3 py-2">
                    {!p.ok
                      ? <XCircle className="w-4 h-4 text-red-400 shrink-0" />
                      : phase === 'done'  ? <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
                      : phase === 'warn'  ? <AlertTriangle className="w-4 h-4 text-amber-400 shrink-0" />
                      : phase === 'error' ? <XCircle className="w-4 h-4 text-red-400 shrink-0" />
                      : <Loader2 className="w-4 h-4 text-blue-400 animate-spin shrink-0" />}
                    <span className="font-mono text-xs text-ms-text">{p.hostname}</span>
                    {p.ip && <span className="text-[11px] text-ms-text-muted">→ {p.ip}</span>}
                    {p.status?.storeCode && (
                      <span className="text-[10px] text-ms-text-muted">M:{p.status.storeCode}</span>
                    )}
                    {!p.ok && p.error && (
                      <span className="text-xs text-red-400 ml-auto">{p.error}</span>
                    )}
                  </div>
                  {p.ok && p.status && (
                    <div className={`mx-2 mb-2 p-3 rounded-lg border ${
                      phase === 'done'  ? 'bg-emerald-500/5 border-emerald-500/20' :
                      phase === 'warn'  ? 'bg-amber-500/5 border-amber-500/20' :
                      phase === 'error' ? 'bg-red-500/5 border-red-500/20' :
                      'bg-blue-500/5 border-blue-500/20'
                    }`}>
                      <div className="space-y-1.5">
                        {p.status.steps.map((s, i) => (
                          <div key={i} className="flex items-center gap-2">
                            <StepIcon state={s.state} />
                            <span className={`text-xs ${
                              s.state === 'done'    ? 'text-emerald-400' :
                              s.state === 'error'   ? 'text-red-400' :
                              s.state === 'warn'    ? 'text-amber-400' :
                              s.state === 'running' ? 'text-blue-400' :
                              'text-gray-500'
                            }`}>{s.name}</span>
                            {s.detail && (
                              <span className="text-[10px] text-ms-text-muted ml-1">— {s.detail}</span>
                            )}
                          </div>
                        ))}
                      </div>
                      {p.status.error && (
                        <p className="text-xs text-red-400 mt-2">{p.status.error}</p>
                      )}
                    </div>
                  )}
                  {p.ok && !p.status && (
                    <div className="px-3 pb-2 text-[11px] text-ms-text-muted flex items-center gap-2">
                      <Loader2 className="w-3 h-3 animate-spin" /> Durum bekleniyor…
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}

      {tab === 'user' && pendingInstalls.length > 0 && (
        <div className="fixed bottom-4 left-4 w-[520px] max-w-[calc(100vw-2rem)] bg-ms-bg-soft border border-ms-border rounded-lg p-3 shadow-2xl z-50">
          <div className="flex items-center justify-between mb-2">
            <div className="text-sm font-medium text-ms-text flex items-center gap-2">
              <Clock className="w-4 h-4 text-emerald-400" />
              Bekleyen Kullanıcı Kurulumları
              <span className="text-[10px] px-1.5 py-0.5 rounded bg-ms-bg text-ms-text-muted">{pendingInstalls.length}</span>
            </div>
            <button onClick={loadPendingInstalls} className="text-[11px] text-ms-text-muted hover:text-ms-text">
              Yenile
            </button>
          </div>
          <div className="space-y-1 max-h-[60vh] overflow-auto pr-1">
            {pendingInstalls.map(p => {
              const isActive = p.status === 'Waiting' || p.status === 'Matched' || p.status === 'Installing';
              const isRetryable = p.status === 'Failed' || p.status === 'Expired' || p.status === 'Cancelled';
              const statusColor =
                p.status === 'Done' ? 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30' :
                p.status === 'Failed' ? 'bg-red-500/15 text-red-400 border-red-500/30' :
                p.status === 'Installing' ? 'bg-blue-500/15 text-blue-400 border-blue-500/30' :
                p.status === 'Matched' ? 'bg-violet-500/15 text-violet-300 border-violet-500/30' :
                p.status === 'Waiting' ? 'bg-amber-500/15 text-amber-400 border-amber-500/30' :
                'bg-gray-500/15 text-gray-400 border-gray-500/30';
              return (
                <div key={p.id} className="flex items-center gap-2 px-2 py-1.5 border border-ms-border/40 rounded text-xs">
                  <UserIcon className="w-3.5 h-3.5 text-ms-text-muted shrink-0" />
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-ms-text truncate">{p.displayName || p.samAccountName}</span>
                      <span className="font-mono text-[10px] text-ms-text-muted">{p.samAccountName}</span>
                    </div>
                    {p.matchedComputer && (
                      <div className="text-[10px] text-ms-text-muted truncate">
                        → {p.matchedComputer}{p.matchedIp ? ` (${p.matchedIp})` : ''}
                      </div>
                    )}
                    {p.lastError && (
                      <div className="text-[10px] text-red-400 truncate" title={p.lastError}>{p.lastError}</div>
                    )}
                  </div>
                  <span className={`text-[10px] px-1.5 py-0.5 rounded border ${statusColor}`}>{p.status}</span>
                  {isActive && (
                    <button onClick={() => cancelPending(p.id)} className="p-1 text-gray-500 hover:text-red-400 rounded" title="İptal">
                      <X className="w-3.5 h-3.5" />
                    </button>
                  )}
                  {isRetryable && (
                    <button onClick={() => retryPending(p.id)} className="p-1 text-gray-500 hover:text-emerald-400 rounded" title="Yeniden başlat">
                      <RotateCcw className="w-3.5 h-3.5" />
                    </button>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
};

export default ActiveDirectoryPage;
