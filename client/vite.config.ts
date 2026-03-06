import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import istanbul from 'vite-plugin-istanbul';

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    ...(process.env.VITE_COVERAGE === 'true'
      ? [istanbul({ include: 'src/**/*', extension: ['.ts', '.tsx'] })]
      : []),
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5275',
      '/hubs': {
        target: 'http://localhost:5275',
        ws: true,
      },
    },
  },
});
