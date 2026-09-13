import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'node:path';

export default defineConfig({
  base: './',
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:7284',
        changeOrigin: true,
        secure: false,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
  resolve: {
    alias: {
      '@manoir-app/core-admin-ui-kit/avatar': resolve(__dirname, '../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Kit/src/components/Avatar.tsx'),
      '@manoir-app/core-admin-ui-kit/button': resolve(__dirname, '../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Kit/src/components/Button.tsx'),
      '@manoir-app/core-admin-ui-kit/card': resolve(__dirname, '../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Kit/src/components/Card.tsx'),
      '@manoir-app/core-admin-ui-kit/default-admin-shell': resolve(__dirname, '../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Kit/src/components/DefaultAdminShell.tsx'),
      '@manoir-app/core-admin-ui-kit/page-header': resolve(__dirname, '../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Kit/src/components/PageHeader.tsx'),
      '@manoir-app/core-admin-ui-kit/sidebar-nav': resolve(__dirname, '../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Kit/src/components/SidebarNav.tsx'),
      '@manoir-app/core-admin-ui-kit/shell-header': resolve(__dirname, '../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Kit/src/components/ShellHeader.tsx'),
      '@manoir-app/core-admin-ui-kit/status-dot': resolve(__dirname, '../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Kit/src/components/StatusDot.tsx'),
      '@manoir-app/core-admin-ui-kit/toggle-switch': resolve(__dirname, '../../../MaNoir.Platform/ui/MaNoir.Core.AdminUi.Kit/src/components/ToggleSwitch.tsx'),
    },
  },
});