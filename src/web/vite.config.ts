import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    // The ECharts chunk (~620 kB) is loaded only when a chart first appears; it is expected to be the largest file.
    chunkSizeWarningLimit: 700,
    rolldownOptions: {
      output: {
        // Libraries change rarely: keeping them out of the app chunk lets browsers keep them cached across releases.
        manualChunks(id: string) {
          if (/node_modules[\/](react|react-dom|scheduler)[\/]/.test(id)) return 'react-vendor'
          return undefined
        },
      },
    },
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5058',
        changeOrigin: true,
      },
    },
  },
})
