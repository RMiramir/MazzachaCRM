import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// В разработке запросы /api уходят на бэкенд, а сборка кладётся прямо в wwwroot API,
// чтобы в рабочем режиме всё работало одним процессом.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': 'http://localhost:5080' },
  },
  build: {
    outDir: '../src/MazzachaCRM.Api/wwwroot',
    emptyOutDir: true,
    chunkSizeWarningLimit: 1500,
  },
});
