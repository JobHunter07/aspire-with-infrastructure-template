import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Proxy API calls to the app service
      '/api': {
        // Default to the local API HTTP launch profile. Allow overriding via env vars.
        target: process.env.SERVER_HTTP || process.env.SERVER_HTTPS || 'http://localhost:5327',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
        secure: false
      }
    }
  }
})
