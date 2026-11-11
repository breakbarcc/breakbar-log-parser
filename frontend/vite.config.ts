/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API (backend/src/Breakbar.Api) serves the built app from its wwwroot folder.
// During development Vite runs on :5173 and forwards API, report and asset requests to the API on :5080.
const api = 'http://localhost:5080'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      '/api': api,
      '/r': api,
      '/ei-assets': api,
    },
  },
  build: {
    outDir: '../backend/src/Breakbar.Api/wwwroot',
    emptyOutDir: true,
  },
  test: {
    environment: 'node',
  },
})
