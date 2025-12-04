import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173
  },
  // DÜZELTME: Bu ayar, iç içe Router hatasına neden olan
  // önbelleğe alınmış bağımlılıkları sıfırdan yeniden yüklemeye zorlar.
  optimizeDeps: {
    force: true,
  },
});