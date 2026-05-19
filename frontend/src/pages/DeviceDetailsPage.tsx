import React, { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  Activity, AlertTriangle, ArrowLeft, Cpu, Download, FolderOpen, HardDrive,
  MemoryStick, Monitor, Package, Power, Radar, Settings,
  Terminal, Trash2, Wifi, WifiOff, X, Clock, User, Server,
  Gauge, Zap, ThermometerSun, Eye, Wrench,
} from "lucide-react";
import { useAuth } from "../contexts/AuthContext";
import MetricChart from "../components/ui/MetricChart";
import { apiClient } from "../lib/apiClient";
import type { Device, DeviceMetric, OsInfo } from "../types";

/* ------------------------------------------------------------------ */
/*  Helpers                                                            */
/* ------------------------------------------------------------------ */
const fmt = (s?: string | null) => {
  if (!s) return "-";
  const d = new Date(s.endsWith("Z") ? s : `${s}Z`);
  return Number.isNaN(d.getTime())
    ? "-"
    : d.toLocaleString("tr-TR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
};

const fmtUptime = (s?: string | null) => {
  if (!s) return "-";
  const boot = new Date(s.endsWith("Z") ? s : `${s}Z`);
  if (Number.isNaN(boot.getTime())) return "-";
  const ms = Date.now() - boot.getTime();
  const d = Math.floor(ms / 86400000);
  const h = Math.floor((ms % 86400000) / 3600000);
  const m = Math.floor((ms % 3600000) / 60000);
  if (d > 0) return `${d}g ${h}s ${m}dk`;
  if (h > 0) return `${h}s ${m}dk`;
  return `${m}dk`;
};

const fmtOs = (o?: OsInfo) => {
  if (!o?.name) return "-";
  const n = o.name.trim();
  if (n.includes("NT 6.1")) return "Windows 7";
  if (n.includes("NT 10.0")) return "Windows 10/11";
  return n;
};

const fmtRam = (mb?: number) => (mb ? `${Math.round(mb / 1024)} GB` : "-");
const clamp = (v = 0) => Math.max(0, Math.min(100, Math.round(v)));

/* ------------------------------------------------------------------ */
/*  Page                                                               */
/* ------------------------------------------------------------------ */
const DeviceDetailsPage: React.FC = () => {
  const { deviceId } = useParams<{ deviceId: string }>();
  const navigate = useNavigate();
  const { isAdmin } = useAuth();
  const [dev, setDev] = useState<Device | null>(null);
  const [metrics, setMetrics] = useState<DeviceMetric[]>([]);
  const [loading, setLoading] = useState(true);
  const [restarting, setRestarting] = useState(false);
  const [installingVnc, setInstallingVnc] = useState(false);
  const [vncResult, setVncResult] = useState<{ ok: boolean; title: string; output: string } | null>(null);

  // Uninstall modal
  const [uninstallOpen, setUninstallOpen] = useState(false);
  const [uninstallConfirmText, setUninstallConfirmText] = useState("");
  const [uninstalling, setUninstalling] = useState(false);
  const [uninstallError, setUninstallError] = useState<string | null>(null);

  // Cleanup
  const [cleanPaths, setCleanPaths] = useState<string[]>([]);
  const [customPath, setCustomPath] = useState("");
  const [cleaning, setCleaning] = useState(false);
  const [cleanResult, setCleanResult] = useState<string | null>(null);

  // Scanner
  const scanRef = useRef<HTMLElement>(null);
  const [scanOpen, setScanOpen] = useState(false);
  const [scanning, setScanning] = useState(false);
  const [scanResults, setScanResults] = useState<any[]>([]);
  const [scanInfo, setScanInfo] = useState<{ subnet: string; range: string; total: number } | null>(null);
  const [scanError, setScanError] = useState<string | null>(null);

  /* ---- data fetch ---- */
  useEffect(() => {
    if (!deviceId) { setLoading(false); return; }
    let off = false;
    const load = async () => {
      try { const d = await apiClient.getDevice(deviceId); if (!off) setDev(d); }
      catch { if (!off) setDev(null); }
      finally { if (!off) setLoading(false); }
    };
    load();
    const iv = setInterval(load, 10000);
    return () => { off = true; clearInterval(iv); };
  }, [deviceId]);

  useEffect(() => {
    if (!deviceId) { setMetrics([]); return; }
    let off = false;
    const loadMetrics = async () => {
      try {
        const data = await apiClient.getDeviceMetrics(deviceId);
        if (!off) setMetrics(data);
      } catch {
        if (!off) setMetrics([]);
      }
    };
    void loadMetrics();
    return () => { off = true; };
  }, [deviceId]);

  /* ---- actions ---- */
  const doVnc = async () => {
    if (!deviceId) return;
    setInstallingVnc(true);
    setVncResult(null);
    try {
      const resp = await apiClient.post<{ commandId: string }>(`/api/agent/vnc-install/${deviceId}`);
      const commandId = resp?.commandId;
      const isRepair = !!dev?.vncInstalled;

      // Sonucu polla — agent komutu yaklasik 20-30sn'de tamamliyor
      const deadline = Date.now() + 90_000;
      let result: { success: boolean; output: string } | null = null;
      while (Date.now() < deadline) {
        await new Promise(r => setTimeout(r, 3000));
        try {
          const r = await apiClient.get<{ success: boolean; output: string }>(`/api/agent/command-results/${commandId}`);
          if (r && (r as any).output) { result = r; break; }
        } catch { /* 404 = hala bekliyor */ }
      }

      if (result) {
        setVncResult({
          ok: result.success,
          title: result.success
            ? (isRepair ? "VNC Onar tamamlandı" : "VNC Kurulum tamamlandı")
            : (isRepair ? "VNC Onar başarısız" : "VNC Kurulum başarısız"),
          output: result.output,
        });
      } else {
        setVncResult({
          ok: false,
          title: "Sonuç gelmedi",
          output: "Komut agent'a iletildi ancak 90sn içinde sonuç dönmedi. Cihaz offline olabilir ya da komut hala işleniyor olabilir.",
        });
      }
    }
    catch {
      setVncResult({ ok: false, title: "Komut gönderilemedi", output: "İstek backend'e ulaşmadı." });
    }
    finally { setInstallingVnc(false); }
  };

  const doRestart = async () => {
    if (!deviceId || !window.confirm("Bu cihazı yeniden başlatmak istediğinize emin misiniz?")) return;
    setRestarting(true);
    try { await apiClient.runScript(deviceId, "shutdown /r /t 0"); alert("Yeniden başlatma komutu gönderildi."); }
    catch { alert("Komut gönderilemedi."); }
    finally { setRestarting(false); }
  };

  const doUninstall = async () => {
    if (!deviceId || !dev) return;
    setUninstalling(true);
    setUninstallError(null);
    try {
      await apiClient.uninstallAgent(deviceId);
      setUninstallOpen(false);
      setUninstallConfirmText("");
      alert(
        `Uninstall komutu kuyruga eklendi.\n\nAgent ~30sn icinde:\n- TightVNC'yi kaldiracak\n- Servisi durdurup silecek\n- Kurulum klasoru ve loglari silecek\n\nCihaz birazdan offline olacak ve envanterden silinebilir.`
      );
    } catch (e) {
      setUninstallError(e instanceof Error ? e.message : "Uninstall komutu gonderilemedi.");
    } finally {
      setUninstalling(false);
    }
  };

  const doScan = async () => {
    if (!dev?.ipAddress) return;
    const parts = dev.ipAddress.split(".");
    if (parts.length < 4) return;
    const subnet = parts.slice(0, 3).join(".");
    setScanOpen(true); setScanning(true); setScanResults([]); setScanError(null);
    setScanInfo({ subnet, range: `${subnet}.1 - ${subnet}.254`, total: 0 });
    // Scan bölümüne kaydır
    setTimeout(() => {
      scanRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }, 100);
    try {
      const body: any = { subnet }; if (deviceId) body.deviceId = deviceId;
      const res = await apiClient.post<any>("/api/agent/remote-install/scan", body, 360000);
      setScanResults(res.devices || []);
      setScanInfo({ subnet: res.subnet, range: res.scannedRange, total: res.total });
      setScanError(res.error || null);
    } catch (err: any) { setScanError(err?.message || String(err)); }
    finally { setScanning(false); }
  };

  const cleanupPaths = [
    { label: "Windows Temp", path: "C:\\Windows\\Temp" },
    { label: "Kullanıcı Temp", path: "%USERTEMP%" },
    { label: "Prefetch", path: "C:\\Windows\\Prefetch" },
  ];

  const doClean = async () => {
    if (!deviceId) return;
    const all = [...cleanPaths, ...(customPath.trim() ? [customPath.trim()] : [])];
    if (!all.length) { alert("En az bir klasör seçin."); return; }
    if (!window.confirm(`${all.length} klasör temizlenecek. Onaylıyor musunuz?`)) return;
    setCleaning(true); setCleanResult(null);
    try {
      const r: string[] = [];
      for (const p of all) { const { commandId } = await apiClient.folderCleanup(deviceId, p); r.push(`${p} (${commandId.slice(0, 8)}...)`); }
      setCleanResult(r.join("\n")); setCleanPaths([]); setCustomPath("");
    } catch { setCleanResult("Temizlik komutu gönderilemedi!"); }
    finally { setCleaning(false); }
  };

  /* ---- chart data ---- */
  const cpuData = useMemo(() => metrics.map(m => ({ name: fmt(m.timestampUtc), value: m.cpuUsagePercent })), [metrics]);
  const ramData = useMemo(() => metrics.map(m => ({ name: fmt(m.timestampUtc), value: m.ramUsagePercent })), [metrics]);
  const diskData = useMemo(() => metrics.map(m => ({ name: fmt(m.timestampUtc), value: m.diskUsagePercent })), [metrics]);

  /* ---- loading / error ---- */
  if (loading) return (
    <div className="flex items-center justify-center min-h-[500px]">
      <div className="relative">
        <div className="w-12 h-12 rounded-full border-2 border-violet-500/20 border-t-violet-500 animate-spin" />
        <div className="absolute inset-0 w-12 h-12 rounded-full border-2 border-transparent border-b-sky-500/40 animate-spin" style={{ animationDirection: "reverse", animationDuration: "1.5s" }} />
      </div>
    </div>
  );

  if (!dev) return (
    <div className="flex flex-col items-center justify-center min-h-[500px] animate-fade-in">
      <Monitor className="w-16 h-16 mb-4 text-slate-600" />
      <p className="text-slate-400 mb-4">Cihaz bilgileri yüklenemedi</p>
      <button onClick={() => navigate("/devices")} className="btn-secondary">Cihazlara Dön</button>
    </div>
  );

  const osText = dev.os?.version && dev.os.version !== "-" ? dev.os.version : fmtOs(dev.os);
  const isKasa = dev.hostname?.startsWith("KSTR");
  const cpu = clamp(dev.cpuUsage);
  const ram = clamp(dev.ramUsage);
  const disk = clamp(dev.diskUsage);

  return (
    <div className="space-y-6 animate-fade-in">

      {/* ═══════════ HEADER ═══════════ */}
      <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <button onClick={() => navigate("/devices")} className="p-2.5 rounded-xl bg-ms-panel border border-ms-border hover:bg-ms-hover-bg transition-all hover:-translate-x-0.5">
            <ArrowLeft className="w-4 h-4 text-ms-text-muted" />
          </button>
          <div>
            <div className="flex items-center gap-3">
              <h1 className="text-xl font-bold text-ms-text">{dev.storeName || dev.hostname}</h1>
              <StatusPill online={dev.online} />
              <span className={`px-2 py-0.5 rounded text-[10px] font-semibold tracking-wide uppercase ${isKasa ? "bg-amber-500/10 text-amber-500 dark:text-amber-400" : "bg-sky-500/10 text-sky-600 dark:text-sky-400"}`}>
                {isKasa ? "Kasa" : "Mağaza PC"}
              </span>
            </div>
            <p className="text-xs text-ms-text-muted mt-0.5 font-mono">{dev.ipAddress} · M:{dev.storeCode} · {dev.hostname}</p>
          </div>
        </div>

        {/* Action Buttons */}
        <div className="flex flex-wrap gap-1.5">
          <Pill icon={Activity} label="Sağlık" color="emerald" onClick={() => navigate(`/devices/${deviceId}/health`)} />
          <Pill icon={Monitor} label="Uzak Masaüstü" color="blue" onClick={() => navigate(`/devices/${deviceId}/rdp`)} disabled={!dev.online || !dev.vncInstalled} />
          <Pill
            icon={dev.vncInstalled ? Wrench : Download}
            label={installingVnc ? (dev.vncInstalled ? "Onarılıyor..." : "Kuruluyor...") : (dev.vncInstalled ? "VNC Onar" : "VNC Kur")}
            color={dev.vncInstalled ? "amber" : "cyan"}
            onClick={doVnc}
            disabled={installingVnc || !dev.online}
          />
          <Pill icon={Settings} label="Servisler" color="indigo" onClick={() => navigate(`/devices/${deviceId}/services`)} />
          <Pill icon={FolderOpen} label="Dosyalar" color="violet" onClick={() => navigate(`/devices/${deviceId}/files`)} />
          <Pill icon={Package} label="Yazılımlar" color="fuchsia" onClick={() => navigate(`/devices/${deviceId}/software`)} />
          <Pill icon={Terminal} label="Betik" color="amber" onClick={() => navigate(`/devices/${deviceId}/script`)} />
          <Pill icon={Radar} label={scanning ? "Taranıyor..." : "IP Tarama"} color="teal" onClick={doScan} disabled={scanning} />
          <Pill icon={Power} label={restarting ? "Gönderiliyor..." : "Yeniden Başlat"} color="red" onClick={doRestart} disabled={restarting || !dev.online} />
          {isAdmin && (
            <Pill
              icon={Trash2}
              label="Agent'ı Kaldır"
              color="red"
              onClick={() => { setUninstallError(null); setUninstallConfirmText(""); setUninstallOpen(true); }}
              disabled={!dev.online}
            />
          )}
        </div>
      </div>

      {/* ═══════════ LIVE GAUGES ═══════════ */}
      <div className="grid grid-cols-3 gap-4">
        <GaugeCard label="İşlemci" value={cpu} icon={Cpu} color="rose" />
        <GaugeCard label="Bellek" value={ram} icon={MemoryStick} color="blue" />
        <GaugeCard label="Disk" value={disk} icon={HardDrive} color="emerald"
          extra={dev.totalDiskGB ? `${Math.round((dev.totalDiskGB * disk) / 100)} / ${dev.totalDiskGB} GB` : undefined} />
      </div>

      {/* ═══════════ DEVICE INFO TABLE ═══════════ */}
      <section className="rounded-2xl bg-ms-bg-soft border border-ms-border p-6 relative overflow-hidden">
        {/* subtle glow */}
        <div className="absolute -top-20 -right-20 w-60 h-60 bg-violet-500/[0.04] rounded-full blur-3xl pointer-events-none" />
        <h2 className="text-sm font-semibold text-ms-text mb-5 flex items-center gap-2 relative z-10">
          <span className="p-1.5 rounded-lg bg-violet-500/10"><Monitor className="w-4 h-4 text-violet-500 dark:text-violet-400" /></span>
          Cihaz Bilgileri
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-x-8 gap-y-1 relative z-10">
          <InfoRow label="PC Adı" value={dev.hostname} icon={Server} />
          <InfoRow label="IP Adresi" value={dev.ipAddress} mono />
          <InfoRow label="İşletim Sistemi" value={osText} />
          <InfoRow label="Mağaza Kodu" value={String(dev.storeCode || "-")} />
          <InfoRow label="Agent Sürümü" value={dev.agentVersion} badge />
          <InfoRow label="Çalışma Süresi" value={fmtUptime(dev.systemBootTime)} icon={Clock} accent />
          <InfoRow label="Oturum Açan" value={dev.lastLoggedInUser} icon={User} />
          <InfoRow label="Son Görülme" value={fmt(dev.lastSeen)} />
          <InfoRow label="SQL Sürümü" value={dev.sqlVersion} />
          <InfoRow label="Kurulum Tarihi" value={fmt(dev.firstSeen)} accent />
          <InfoRow label="İşlemci" value={dev.cpuModel} icon={Cpu} />
          <InfoRow label="Bellek" value={fmtRam(dev.totalRamMB)} icon={MemoryStick} />
          <InfoRow label="Seri No" value={dev.serialNumber} mono />
        </div>
      </section>

      {/* ═══════════ PERFORMANCE CHARTS ═══════════ */}
      <section>
        <div className="flex items-center gap-2 mb-3">
          <span className="p-1.5 rounded-lg bg-emerald-500/10"><Activity className="w-4 h-4 text-emerald-500 dark:text-emerald-400" /></span>
          <h2 className="text-sm font-semibold text-ms-text">Performans Metrikleri</h2>
          <span className="text-[11px] text-ms-text-muted ml-1">Son 24 Saat</span>
        </div>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <ChartCard title="İşlemci Kullanımı" data={cpuData} value={cpu} color="#f87171" />
          <ChartCard title="Bellek Kullanımı" data={ramData} value={ram} color="#60a5fa" />
          <ChartCard title="Disk Kullanımı" data={diskData} value={disk} color="#34d399" />
        </div>
      </section>

      {/* ═══════════ QUICK CLEANUP ═══════════ */}
      <section className="rounded-2xl bg-ms-bg-soft border border-ms-border p-5">
        <div className="flex items-center gap-2 mb-3">
          <span className="p-1.5 rounded-lg bg-rose-500/10"><Trash2 className="w-4 h-4 text-rose-500 dark:text-rose-400" /></span>
          <h2 className="text-sm font-semibold text-ms-text">Hızlı Temizlik</h2>
        </div>
        <div className="flex items-center gap-2 flex-wrap">
          {cleanupPaths.map(({ label, path }) => (
            <button key={path} onClick={() => setCleanPaths(p => p.includes(path) ? p.filter(x => x !== path) : [...p, path])}
              className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${cleanPaths.includes(path)
                ? "bg-rose-500 text-white shadow-lg shadow-rose-500/20"
                : "bg-ms-panel text-ms-text-muted hover:text-ms-text border border-ms-border hover:border-rose-500/30"
              }`}>
              {cleanPaths.includes(path) ? "✓ " : ""}{label}
            </button>
          ))}
          <input type="text" value={customPath} onChange={e => setCustomPath(e.target.value)}
            placeholder="Özel klasör yolu..."
            className="flex-1 min-w-[200px] !py-1.5 !px-3 !text-xs" />
          <button onClick={doClean} disabled={cleaning || !dev.online || (!cleanPaths.length && !customPath.trim())}
            className="px-4 py-1.5 bg-rose-600 hover:bg-rose-500 text-white rounded-lg text-xs font-medium disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-1.5 transition-colors shadow-lg shadow-rose-600/10">
            <Trash2 className="w-3.5 h-3.5" />
            {cleaning ? "Temizleniyor..." : "Temizle"}
          </button>
        </div>
        {cleanResult && <pre className="mt-3 p-3 rounded-lg bg-ms-panel border border-ms-border text-xs text-ms-text-muted whitespace-pre-line">{cleanResult}</pre>}
      </section>

      {/* ═══════════ IP SCANNER ═══════════ */}
      {scanOpen && (
        <section ref={scanRef} className="rounded-2xl border border-teal-500/20 bg-ms-bg-soft overflow-hidden animate-fade-in">
          <div className="flex items-center justify-between px-5 py-3 border-b border-teal-500/10">
            <div className="flex items-center gap-2">
              <span className="p-1.5 rounded-lg bg-teal-500/10"><Radar className="w-4 h-4 text-teal-500 dark:text-teal-400" /></span>
              <h2 className="text-sm font-semibold text-teal-600 dark:text-teal-400">Ağ Taraması</h2>
              {scanInfo && <span className="text-[11px] font-mono text-ms-text-muted ml-2">{scanInfo.range} · {scanInfo.total} cihaz</span>}
            </div>
            <div className="flex items-center gap-2">
              {!scanning && <button onClick={doScan} className="text-[11px] font-medium text-teal-600 dark:text-teal-400 hover:underline">Tekrar Tara</button>}
              <button onClick={() => setScanOpen(false)} className="p-1 text-ms-text-muted hover:text-ms-text"><X className="w-4 h-4" /></button>
            </div>
          </div>
          {scanning ? (
            <div className="flex items-center justify-center py-16 gap-3">
              <div className="w-5 h-5 border-2 border-teal-500 border-t-transparent rounded-full animate-spin" />
              <span className="text-sm text-ms-text-muted">Ağ taranıyor...</span>
            </div>
          ) : scanResults.length === 0 ? (
            <div className="flex flex-col items-center py-16 text-ms-text-muted">
              <Wifi className="w-6 h-6 mb-2 opacity-30" />
              <span className="text-sm">{scanError || "Erişilebilir cihaz bulunamadı"}</span>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>IP Adresi</th><th>Cihaz Adı</th><th>MAC Adresi</th><th>Üretici</th><th>Ping</th><th>Durum</th>
                  </tr>
                </thead>
                <tbody>
                  {scanResults.map((r: any) => {
                    const isSelf = r.ipAddress === dev.ipAddress;
                    const rawName = (r.hostname || "").trim();
                    const looksLikeIp = !rawName || /^\d{1,3}(\.\d{1,3}){3}$/.test(rawName) || rawName === r.ipAddress;
                    if (looksLikeIp && r.vendor) r.hostname = r.vendor;
                    return (
                      <tr key={r.ipAddress} className={isSelf ? "!bg-teal-500/5" : ""}>
                        <td className="font-mono font-medium">{r.ipAddress}{isSelf && <span className="ml-1.5 text-[9px] text-teal-500 dark:text-teal-400">(bu cihaz)</span>}</td>
                        <td>{r.hostname || "—"}</td>
                        <td className="font-mono text-[11px] text-ms-text-muted">{r.macAddress || "—"}</td>
                        <td>{r.vendor ? <span className="text-[11px] px-1.5 py-0.5 rounded bg-ms-panel text-ms-text-muted">{r.vendor}</span> : "—"}</td>
                        <td className="font-mono"><span className={r.pingMs < 5 ? "text-emerald-500" : r.pingMs < 50 ? "text-amber-500" : "text-red-500"}>{r.pingMs}ms</span></td>
                        <td>
                          {r.hasAgent ? (
                            <span className={`inline-flex items-center gap-1 text-[11px] font-medium ${r.online ? "text-emerald-500" : "text-red-500"}`}>
                              <span className={`w-1.5 h-1.5 rounded-full ${r.online ? "bg-emerald-500" : "bg-red-500"}`} />
                              {r.online ? "Agent Online" : "Agent Offline"}
                            </span>
                          ) : <span className="text-[11px] text-ms-text-muted">Ağ cihazı</span>}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {/* ═══════════ VNC RESULT MODAL ═══════════ */}
      {vncResult && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4" onClick={() => setVncResult(null)}>
          <div className={`w-full max-w-xl rounded-2xl border bg-ms-bg-soft shadow-2xl ${vncResult.ok ? 'border-emerald-500/40' : 'border-amber-500/40'}`} onClick={(e) => e.stopPropagation()}>
            <div className={`flex items-start gap-3 border-b px-5 py-4 ${vncResult.ok ? 'border-emerald-500/20' : 'border-amber-500/20'}`}>
              <span className={`p-2 rounded-xl ${vncResult.ok ? 'bg-emerald-500/10' : 'bg-amber-500/10'}`}>
                <Wrench className={`w-5 h-5 ${vncResult.ok ? 'text-emerald-500' : 'text-amber-500'}`} />
              </span>
              <div className="flex-1">
                <h3 className="text-base font-semibold text-ms-text">{vncResult.title}</h3>
                <p className="text-xs text-ms-text-muted mt-0.5">{dev?.storeName || dev?.hostname}</p>
              </div>
              <button onClick={() => setVncResult(null)} className="p-1 text-ms-text-muted hover:text-ms-text">
                <X className="w-4 h-4" />
              </button>
            </div>
            <div className="px-5 py-4">
              <pre className="text-xs font-mono whitespace-pre-wrap bg-ms-bg rounded-lg p-3 border border-ms-border max-h-[60vh] overflow-auto text-ms-text">
                {vncResult.output}
              </pre>
            </div>
            <div className="flex justify-end gap-2 px-5 py-3 border-t border-ms-border">
              <button onClick={() => setVncResult(null)} className="px-3 py-1.5 text-sm rounded-lg bg-ms-bg hover:bg-ms-hover-bg border border-ms-border text-ms-text">
                Kapat
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ═══════════ UNINSTALL CONFIRMATION MODAL ═══════════ */}
      {uninstallOpen && dev && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4" onClick={() => !uninstalling && setUninstallOpen(false)}>
          <div className="w-full max-w-lg rounded-2xl border border-red-500/40 bg-ms-bg-soft shadow-2xl" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-start gap-3 border-b border-red-500/20 px-5 py-4">
              <span className="p-2 rounded-xl bg-red-500/10">
                <AlertTriangle className="w-5 h-5 text-red-500" />
              </span>
              <div className="flex-1">
                <h3 className="text-base font-semibold text-ms-text">Agent'ı Tamamen Kaldır</h3>
                <p className="text-xs text-ms-text-muted mt-0.5">{dev.storeName || dev.hostname} · {dev.ipAddress}</p>
              </div>
              <button
                onClick={() => !uninstalling && setUninstallOpen(false)}
                disabled={uninstalling}
                className="p-1 text-ms-text-muted hover:text-ms-text disabled:opacity-40"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <div className="px-5 py-4 space-y-3">
              <p className="text-sm text-ms-text">
                Bu islem cihazdaki <span className="font-semibold text-red-500">Orchestra agent'ini, TightVNC'yi, kurulum klasorunu ve loglari</span> tamamen kaldirir. Geri donus icin agent yeniden kurulmali.
              </p>
              <ul className="text-xs text-ms-text-muted space-y-1 list-disc pl-5">
                <li><span className="font-mono">MudosoftAgentService</span> servisi durdurulup silinir</li>
                <li>Tray uygulamasi (<span className="font-mono">MudoSoft.Tray.exe</span>) kapatilir</li>
                <li>TightVNC msiexec ile sessizce kaldirilir, registry temizlenir</li>
                <li><span className="font-mono">C:\Program Files\MudoSoft</span> ve update/log dosyalari silinir</li>
                <li><span className="font-mono">MudoSoftRDHelper</span> scheduled task'i silinir</li>
              </ul>

              <div className="rounded-lg border border-red-500/30 bg-red-500/5 p-3">
                <p className="text-xs text-red-500 dark:text-red-400 mb-2">
                  Onaylamak icin hostname'i yazin: <span className="font-mono font-semibold">{dev.hostname}</span>
                </p>
                <input
                  type="text"
                  value={uninstallConfirmText}
                  onChange={(e) => setUninstallConfirmText(e.target.value)}
                  placeholder={dev.hostname || ""}
                  disabled={uninstalling}
                  autoFocus
                  className="w-full px-3 py-2 bg-ms-panel border border-ms-border rounded-lg text-sm font-mono text-ms-text placeholder-ms-text-muted focus:outline-none focus:border-red-500/50 disabled:opacity-50"
                />
              </div>

              {uninstallError && (
                <div className="rounded-lg border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs text-red-500 dark:text-red-400">
                  {uninstallError}
                </div>
              )}
            </div>

            <div className="flex items-center justify-end gap-2 border-t border-ms-border px-5 py-3">
              <button
                onClick={() => setUninstallOpen(false)}
                disabled={uninstalling}
                className="px-4 py-2 rounded-lg text-sm font-medium text-ms-text-muted hover:text-ms-text disabled:opacity-50"
              >
                Vazgec
              </button>
              <button
                onClick={doUninstall}
                disabled={uninstalling || uninstallConfirmText.trim() !== (dev.hostname || "")}
                className="px-4 py-2 rounded-lg text-sm font-medium bg-red-600 hover:bg-red-500 text-white shadow-lg shadow-red-600/20 disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-2"
              >
                <Trash2 className="w-4 h-4" />
                {uninstalling ? "Gonderiliyor..." : "Kaldir"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

/* ================================================================== */
/*  Sub-components                                                     */
/* ================================================================== */

/* ---- Online / Offline badge ---- */
const StatusPill: React.FC<{ online: boolean }> = ({ online }) => (
  <span className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold ${online
    ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20"
    : "bg-red-500/10 text-red-600 dark:text-red-400 border border-red-500/20"
  }`}>
    <span className={`w-1.5 h-1.5 rounded-full ${online ? "bg-emerald-500 animate-pulse" : "bg-red-500"}`} />
    {online ? "Aktif" : "Kapalı"}
  </span>
);

/* ---- Action pill button ---- */
const pillColors: Record<string, string> = {
  emerald: "bg-emerald-600 hover:bg-emerald-500 shadow-emerald-600/20",
  blue: "bg-blue-600 hover:bg-blue-500 shadow-blue-600/20",
  cyan: "bg-cyan-600 hover:bg-cyan-500 shadow-cyan-600/20",
  indigo: "bg-indigo-600 hover:bg-indigo-500 shadow-indigo-600/20",
  violet: "bg-violet-600 hover:bg-violet-500 shadow-violet-600/20",
  fuchsia: "bg-fuchsia-600 hover:bg-fuchsia-500 shadow-fuchsia-600/20",
  amber: "bg-amber-600 hover:bg-amber-500 shadow-amber-600/20",
  red: "bg-red-600 hover:bg-red-500 shadow-red-600/20",
  teal: "bg-teal-600 hover:bg-teal-500 shadow-teal-600/20",
};

const Pill: React.FC<{ icon: React.FC<{ className?: string }>; label: string; color: string; onClick: () => void; disabled?: boolean }> = ({ icon: Icon, label, color, onClick, disabled }) => (
  <button onClick={onClick} disabled={disabled}
    className={`${pillColors[color]} text-white px-3 py-1.5 rounded-lg flex items-center gap-1.5 text-xs font-medium shadow-lg disabled:opacity-40 disabled:cursor-not-allowed transition-all hover:-translate-y-0.5 hover:shadow-xl active:translate-y-0`}>
    <Icon className="w-3.5 h-3.5" />{label}
  </button>
);

/* ---- Radial gauge card ---- */
const gaugeTheme: Record<string, { stroke: string; text: string; glow: string; bg: string }> = {
  rose:    { stroke: "stroke-rose-500",    text: "text-rose-400",    glow: "shadow-rose-500/20",    bg: "bg-rose-500/5" },
  blue:    { stroke: "stroke-blue-500",    text: "text-blue-400",    glow: "shadow-blue-500/20",    bg: "bg-blue-500/5" },
  emerald: { stroke: "stroke-emerald-500", text: "text-emerald-400", glow: "shadow-emerald-500/20", bg: "bg-emerald-500/5" },
  amber:   { stroke: "stroke-amber-500",   text: "text-amber-400",  glow: "shadow-amber-500/20",   bg: "bg-amber-500/5" },
};

const GaugeCard: React.FC<{ label: string; value: number; icon: React.FC<{ className?: string }>; color: string; extra?: string }> = ({ label, value, icon: Icon, color, extra }) => {
  const t = gaugeTheme[color] || gaugeTheme.blue;
  // warn color override
  const effectiveColor = value >= 90 ? "rose" : value >= 75 ? "amber" : color;
  const et = gaugeTheme[effectiveColor] || t;
  const r = 42; const c = 2 * Math.PI * r;
  const offset = c - (c * value) / 100;

  return (
    <div className={`rounded-2xl bg-ms-bg-soft border border-ms-border p-5 flex items-center gap-5 transition-all hover:shadow-lg ${et.glow} group`}>
      {/* SVG ring */}
      <div className="relative w-20 h-20 flex-shrink-0">
        <svg className="w-20 h-20 -rotate-90" viewBox="0 0 100 100">
          <circle cx="50" cy="50" r={r} fill="none" className="stroke-ms-border" strokeWidth="6" />
          <circle cx="50" cy="50" r={r} fill="none" className={`${et.stroke} transition-all duration-700 ease-out`}
            strokeWidth="6" strokeLinecap="round" strokeDasharray={c} strokeDashoffset={offset} />
        </svg>
        <div className="absolute inset-0 flex items-center justify-center">
          <span className={`text-lg font-bold ${et.text}`}>{value}%</span>
        </div>
      </div>
      {/* Label */}
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2 text-ms-text-muted mb-1">
          <Icon className="w-4 h-4" />
          <span className="text-sm font-medium">{label}</span>
        </div>
        {extra && <p className="text-xs text-ms-text-muted truncate">{extra}</p>}
        {/* mini bar */}
        <div className="h-1.5 bg-ms-border/50 rounded-full overflow-hidden mt-2">
          <div className={`h-full rounded-full bg-gradient-to-r ${et.stroke.replace("stroke-", "from-")} to-transparent transition-all duration-700`} style={{ width: `${value}%` }} />
        </div>
      </div>
    </div>
  );
};

/* ---- Info Row ---- */
const InfoRow: React.FC<{ label: string; value?: string | null; icon?: React.FC<{ className?: string }>; mono?: boolean; badge?: boolean; accent?: boolean }> = ({ label, value, icon: Icon, mono, badge, accent }) => (
  <div className="flex items-center justify-between py-2.5 border-b border-ms-border/50 last:border-0 group/row hover:bg-ms-hover-bg -mx-2 px-2 rounded transition-colors">
    <span className="text-[13px] text-ms-text-muted flex items-center gap-1.5">
      {Icon && <Icon className="w-3.5 h-3.5 opacity-50" />}
      {label}
    </span>
    {badge ? (
      <span className="px-2 py-0.5 bg-emerald-500/10 rounded text-emerald-600 dark:text-emerald-400 text-[13px] font-semibold">{value || "-"}</span>
    ) : (
      <span className={`text-[13px] font-medium ${accent ? "text-violet-600 dark:text-violet-400" : "text-ms-text"} ${mono ? "font-mono" : ""} transition-colors`}>{value || "-"}</span>
    )}
  </div>
);

/* ---- Chart Card ---- */
const ChartCard: React.FC<{ title: string; data: { name: string; value: number }[]; value: number; color: string }> = ({ title, data, value, color }) => (
  <div className="p-4 rounded-2xl bg-ms-bg-soft border border-ms-border hover:shadow-md transition-all">
    <MetricChart title={title} data={data} value={value} color={color} height={140} />
  </div>
);

export default DeviceDetailsPage;
