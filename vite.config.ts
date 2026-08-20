import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import path from 'path';
import { defineConfig, type Plugin } from 'vite';

// Node's HTTP server (which Vite's dev server runs on) defaults to a 5-minute
// requestTimeout (Node 18+). Large video uploads to /api/content/probe and
// /api/content/upload routinely exceed that, so the socket gets reset mid-upload
// and the browser reports a generic "network error" even though nothing is
// actually wrong with the connection or the backend (Kestrel allows up to 10GB).
function disableDevServerRequestTimeout(): Plugin {
  return {
    name: 'disable-dev-server-request-timeout',
    configureServer(server) {
      server.httpServer?.once('listening', () => {
        // Vite's dev server is always plain HTTP/1.1 (never HTTP/2), so this cast is safe —
        // the Http2SecureServer variant in ViteDevServer's union type doesn't apply here.
        const httpServer = server.httpServer as import('http').Server;
        httpServer.requestTimeout = 0;
        httpServer.headersTimeout = 0;
      });
    },
  };
}

export default defineConfig(() => {
  return {
    plugins: [react(), tailwindcss(), disableDevServerRequestTimeout()],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, '.'),
      },
    },
    server: {
      port: 3000,
      hmr: process.env.DISABLE_HMR !== 'true',
      watch: process.env.DISABLE_HMR === 'true' ? null : {},
      proxy: {
        '/api': {
          target: 'http://localhost:57220',
          changeOrigin: true,
          secure: false,
          // No timeout on the proxy's outgoing connection either — large video
          // uploads/probes to the backend can legitimately take several minutes.
          timeout: 0,
          proxyTimeout: 0,
        },
        '/hubs': {
          target: 'http://localhost:57220',
          changeOrigin: true,
          secure: false,
          ws: true,  // SignalR WebSocket support
        },
      },
    },
  };
});
