import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react()],
  server: { port: 4200, strictPort: true },
  preview: { port: 4200, strictPort: true },
  test: { environment: 'node' },
});
