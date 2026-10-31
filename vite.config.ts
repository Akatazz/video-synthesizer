/**
 * Vite plugin that embeds the NDI WebSocket bridge into the dev server.
 * Running `npm run dev` automatically starts NDI broadcasting support.
 */

import { defineConfig } from 'vite';
import { NdiBridge } from './server/ndi-bridge.ts';

const ndiBridge = new NdiBridge();

export default defineConfig({
  server: {
    port: 5173,
  },
  plugins: [
    {
      name: 'ndi-bridge-plugin',
      configureServer(server) {
        // Attach the NDI bridge WebSocket to the Vite HTTP server
        server.httpServer?.on('listening', () => {
          ndiBridge.attachToServer(server.httpServer!);
        });
      },
    },
  ],
  // grandi is a native Node addon — never bundle it into the browser build
  optimizeDeps: {
    exclude: ['grandi', 'ws'],
  },
  build: {
    rollupOptions: {
      external: ['grandi', 'ws'],
    },
  },
});
