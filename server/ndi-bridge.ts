/**
 * NDI Bridge Server — WebSocket ↔ NDI 6 Sender
 *
 * Receives raw RGBA pixel buffers over a local WebSocket and broadcasts
 * them as an NDI® video source.  Designed to be imported by the Vite dev
 * server plugin *and* runnable standalone via `npm run ndi:server`.
 *
 * The bridge sends JSON status messages back to the browser client:
 *   { type: "status",  state: "broadcasting" | "idle" }
 *   { type: "tally",   onProgram: bool, onPreview: bool }
 *   { type: "receivers", count: number }
 */

import grandi from 'grandi';
import { WebSocketServer, WebSocket } from 'ws';
import type { IncomingMessage } from 'node:http';
import type { Server as HttpServer } from 'node:http';

/* ------------------------------------------------------------------ */
/*  Types                                                              */
/* ------------------------------------------------------------------ */

export interface NdiBridgeOptions {
  /** Default NDI source name visible on the network. */
  defaultName?: string;
  /** WebSocket port when running standalone (ignored when attached to Vite). */
  port?: number;
}

interface ClientConfig {
  width: number;
  height: number;
  fps: number;
  name: string;
}

/* ------------------------------------------------------------------ */
/*  Core Bridge                                                        */
/* ------------------------------------------------------------------ */

export class NdiBridge {
  private wss: WebSocketServer | null = null;
  private sender: Awaited<ReturnType<typeof grandi.send>> | null = null;
  private currentConfig: ClientConfig = {
    width: 512,
    height: 512,
    fps: 30,
    name: 'LZX-V Synth Output',
  };
  private activeClient: WebSocket | null = null;
  private tallyInterval: ReturnType<typeof setInterval> | null = null;
  private destroyed = false;

  /* ---- lifecycle ------------------------------------------------- */

  /**
   * Attach to an existing HTTP server (used by the Vite plugin).
   */
  attachToServer(server: HttpServer, opts?: NdiBridgeOptions): void {
    this.wss = new WebSocketServer({ server, path: '/ndi-bridge' });
    this._init(opts);
    console.log('[NDI Bridge] Attached to Vite dev server on /ndi-bridge');
  }

  /**
   * Start a standalone WebSocket server.
   */
  startStandalone(opts?: NdiBridgeOptions): void {
    const port = opts?.port ?? 4455;
    this.wss = new WebSocketServer({ port, path: '/ndi-bridge' });
    this._init(opts);
    console.log(`[NDI Bridge] Standalone WebSocket server on ws://localhost:${port}/ndi-bridge`);
  }

  private _init(opts?: NdiBridgeOptions): void {
    if (opts?.defaultName) {
      this.currentConfig.name = opts.defaultName;
    }

    // Initialise NDI library
    if (!grandi.initialize()) {
      console.error('[NDI Bridge] NDI initialization failed — is your CPU supported?');
      return;
    }
    console.log(`[NDI Bridge] NDI SDK: ${grandi.version()}`);

    this.wss!.on('connection', (ws: WebSocket, _req: IncomingMessage) => {
      console.log('[NDI Bridge] Client connected');
      this.activeClient = ws;

      ws.on('message', (data: Buffer | string) => {
        // JSON control messages (string frames)
        if (typeof data === 'string') {
          this._handleControl(ws, data);
          return;
        }

        // Binary video frame
        this._handleVideoFrame(data as Buffer);
      });

      ws.on('close', () => {
        console.log('[NDI Bridge] Client disconnected');
        if (this.activeClient === ws) {
          this.activeClient = null;
        }
      });

      ws.on('error', (err) => {
        console.error('[NDI Bridge] WebSocket error:', err.message);
      });

      // Send initial status
      this._sendJson(ws, {
        type: 'status',
        state: this.sender ? 'broadcasting' : 'idle',
        config: this.currentConfig,
      });
    });
  }

  /* ---- control messages ------------------------------------------ */

  private _handleControl(ws: WebSocket, raw: string): void {
    let msg: any;
    try {
      msg = JSON.parse(raw);
    } catch {
      return;
    }

    switch (msg.type) {
      case 'start': {
        const cfg = msg as Partial<ClientConfig> & { type: string };
        if (cfg.width) this.currentConfig.width = cfg.width;
        if (cfg.height) this.currentConfig.height = cfg.height;
        if (cfg.fps) this.currentConfig.fps = cfg.fps;
        if (cfg.name) this.currentConfig.name = cfg.name;
        this._createSender();
        this._sendJson(ws, { type: 'status', state: 'broadcasting', config: this.currentConfig });
        break;
      }

      case 'stop':
        this._destroySender();
        this._sendJson(ws, { type: 'status', state: 'idle' });
        break;

      case 'config': {
        // Live config update (resolution / fps / name change)
        const prev = { ...this.currentConfig };
        if (msg.width) this.currentConfig.width = msg.width;
        if (msg.height) this.currentConfig.height = msg.height;
        if (msg.fps) this.currentConfig.fps = msg.fps;
        if (msg.name) this.currentConfig.name = msg.name;

        // If name changed we need to recreate the sender (NDI name is immutable)
        if (this.sender && msg.name && msg.name !== prev.name) {
          this._destroySender();
          this._createSender();
        }

        this._sendJson(ws, {
          type: 'status',
          state: this.sender ? 'broadcasting' : 'idle',
          config: this.currentConfig,
        });
        break;
      }

      case 'ping':
        this._sendJson(ws, { type: 'pong' });
        break;
    }
  }

  /* ---- NDI sender management ------------------------------------- */

  private async _createSender(): Promise<void> {
    if (this.sender) {
      this._destroySender();
    }

    const sender = await grandi.send({
      name: this.currentConfig.name,
      clockVideo: true,
      clockAudio: false,
    });
    this.sender = sender;

    console.log(
      `[NDI Bridge] Sender created: "${sender.sourceName()}" @ ${this.currentConfig.width}×${this.currentConfig.height} ${this.currentConfig.fps}fps`,
    );

    // Start tally / receiver polling
    this.tallyInterval = setInterval(() => {
      if (!this.sender || !this.activeClient) return;

      const tally = this.sender.tally();
      const conns = this.sender.connections();

      this._sendJson(this.activeClient, {
        type: 'tally',
        onProgram: tally.onProgram,
        onPreview: tally.onPreview,
      });
      this._sendJson(this.activeClient, {
        type: 'receivers',
        count: conns,
      });
    }, 500);
  }

  private _destroySender(): void {
    if (this.tallyInterval) {
      clearInterval(this.tallyInterval);
      this.tallyInterval = null;
    }
    if (this.sender) {
      this.sender.destroy();
      this.sender = null;
      console.log('[NDI Bridge] Sender destroyed');
    }
  }

  /* ---- video frame handling -------------------------------------- */

  private _handleVideoFrame(data: Buffer): void {
    if (!this.sender) return;

    const { width, height, fps } = this.currentConfig;
    const expectedBytes = width * height * 4; // RGBA

    if (data.length !== expectedBytes) {
      // Silently drop mis-sized frames (can happen during resolution change)
      return;
    }

    // grandi.send().video() is synchronous-ish (it copies the buffer internally)
    this.sender.video({
      xres: width,
      yres: height,
      frameRateN: fps,
      frameRateD: 1,
      pictureAspectRatio: width / height,
      fourCC: 1094862674 as any, // FourCC.RGBA
      frameFormatType: 1 as any, // FrameType.Progressive
      lineStrideBytes: width * 4,
      data: data,
    });
  }

  /* ---- helpers --------------------------------------------------- */

  private _sendJson(ws: WebSocket, obj: any): void {
    if (ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify(obj));
    }
  }

  /**
   * Tear everything down.
   */
  destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this._destroySender();
    if (this.wss) {
      this.wss.close();
      this.wss = null;
    }
    grandi.destroy();
  }
}

/* ------------------------------------------------------------------ */
/*  Standalone entry point                                             */
/* ------------------------------------------------------------------ */

const isMain =
  process.argv[1] &&
  (process.argv[1].endsWith('ndi-bridge.ts') ||
    process.argv[1].endsWith('ndi-bridge.js') ||
    process.argv[1].endsWith('ndi-bridge.mjs'));

if (isMain) {
  const bridge = new NdiBridge();
  bridge.startStandalone({ port: 4455 });

  const shutdown = () => {
    console.log('\n[NDI Bridge] Shutting down…');
    bridge.destroy();
    process.exit(0);
  };
  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
}
