import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const backend = 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    host: true,
    proxy: {
      '/api': backend,
      '/photos': backend,
      '/hubs': { target: backend, ws: true },
    },
  },
  build: { outDir: '../server/Firin.Api/wwwroot', emptyOutDir: true },
});
