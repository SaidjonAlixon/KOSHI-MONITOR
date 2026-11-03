/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'

const testsDir = fileURLToPath(new URL('../../tests/koshi-admin-tests', import.meta.url))

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
    dedupe: ['react', 'react-dom', 'react-router', '@tanstack/react-query'],
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: process.env.KOSHI_API_URL ?? 'http://localhost:5044', changeOrigin: false },
      '/hubs': { target: process.env.KOSHI_API_URL ?? 'http://localhost:5044', changeOrigin: false, ws: true },
    },
  },
  build: { sourcemap: false, chunkSizeWarningLimit: 900 },
  test: {
    environment: 'jsdom',
    globals: true,
    root: fileURLToPath(new URL('.', import.meta.url)),
    include: [`${testsDir.replaceAll('\\', '/')}/**/*.test.{ts,tsx}`],
    setupFiles: [`${testsDir.replaceAll('\\', '/')}/setup.ts`],
  },
})
