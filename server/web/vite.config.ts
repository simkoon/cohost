import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The dev proxy targets the developer's local server (self-signed certificate). Development only.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': { target: 'https://localhost:8443', secure: false },
    },
  },
})
