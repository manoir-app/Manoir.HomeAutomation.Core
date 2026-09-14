import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ command }) => ({
  // Relative at build time so the server-injected <base href> drives the deployed prefix; root during dev.
  base: command === 'build' ? './' : '/',
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:{{LOCAL_API_HTTPS_PORT}}',
        changeOrigin: true,
        secure: false,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
}));
