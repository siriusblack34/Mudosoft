import { navGroups } from '../layout/sidebar';

/** Her zaman görünür olan, profile bağlı olmayan path'ler. */
export const ALWAYS_ALLOWED = ['/', '/settings'];

/**
 * Sidebar'da karşılığı olmayan (orphan) rotaların hangi menü anahtarına bağlı sayılacağı.
 * Route guard çözümünde kullanılır.
 */
const ROUTE_MENU_OVERRIDES: Record<string, string> = {
  '/health-score': '/', // Dashboard benzeri özet sayfası — herkese açık say
};

export interface MenuCatalogItem {
  to: string;
  label: string;
  group: string;
  requiresAdmin: boolean;
}

/** Tüm sidebar menüleri (Kontrol Paneli '/' hariç) — etiket + grup + admin bilgisiyle. */
export function getMenuCatalog(): MenuCatalogItem[] {
  const out: MenuCatalogItem[] = [];
  for (const g of navGroups) {
    for (const it of g.items) {
      if (it.to === '/') continue; // Kontrol Paneli her zaman açık, yapılandırılamaz
      out.push({ to: it.to, label: it.label, group: g.title, requiresAdmin: !!it.requiresAdmin });
    }
  }
  return out;
}

/** Profillere atanabilir menüler: katalog eksi her-zaman-açık eksi admin'e özel menüler. */
export function getConfigurableCatalog(): MenuCatalogItem[] {
  return getMenuCatalog().filter(i => !ALWAYS_ALLOWED.includes(i.to) && !i.requiresAdmin);
}

/** /api/me/menus yanıtı — etkin erişimin ham bileşenleri. */
export interface MyMenusResponse {
  isAdmin: boolean;
  allowAllByDefault: boolean;
  profileName: string | null;
  profileAllowed: string[];
  profileHidden: string[];
  grants: string[];
  denials: string[];
}

/**
 * Etkin görünür menü path'lerini (sidebar `to` anahtarları) çözer.
 * Aynı mantık backend'deki MenuAccessService.CanAccess ile birebir.
 */
export function resolveAllowedPaths(my: MyMenusResponse | null): Set<string> {
  const result = new Set<string>(ALWAYS_ALLOWED);
  if (!my) return result;

  if (my.isAdmin) {
    for (const i of getMenuCatalog()) result.add(i.to); // admin her şeyi görür (admin-only dahil)
    return result;
  }

  const configurable = getConfigurableCatalog().map(i => i.to);
  const base = my.allowAllByDefault
    ? configurable.filter(p => !my.profileHidden.includes(p))
    : my.profileAllowed.filter(p => configurable.includes(p));

  for (const p of base) result.add(p);
  for (const p of my.grants ?? []) if (configurable.includes(p)) result.add(p); // override: aç
  for (const p of my.denials ?? []) result.delete(p);                          // override: kapat
  for (const p of ALWAYS_ALLOWED) result.add(p);                               // her zaman açık korunur
  return result;
}

/**
 * Router pathname'ini sahip olduğu menü anahtarına eşler (en uzun prefix).
 * null → hiçbir menüye bağlı değil (orphan) → guard fail-open davranır.
 */
export function resolveMenuKey(pathname: string): string | null {
  if (ROUTE_MENU_OVERRIDES[pathname]) return ROUTE_MENU_OVERRIDES[pathname];
  if (pathname === '/') return '/';

  const keys = getMenuCatalog().map(i => i.to).filter(k => k !== '/');
  let best: string | null = null;
  for (const key of keys) {
    if (pathname === key || pathname.startsWith(key + '/')) {
      if (!best || key.length > best.length) best = key;
    }
  }
  return best;
}
