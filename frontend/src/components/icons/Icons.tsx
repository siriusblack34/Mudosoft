// src/components/icons/Icons.tsx (Düzeltildi)

import {
  Home,
  Server,
  Database,
  Clock,
  Settings,
  // DÜZELTME: Close ikonunu ekleyin
  X, // 'X' lucide-react kütüphanesinde genellikle çarpı/kapatma ikonudur
} from "lucide-react";

export const HomeIcon = Home;
export const ServerIcon = Server;
export const DatabaseIcon = Database;
export const ClockIcon = Clock;
export const SettingsIcon = Settings;
// DÜZELTME: X ikonunu CloseIcon adı altında dışa aktarın
export const CloseIcon = X;