/**
 * NDI Engine — Browser-side manager for the WebGL → NDI pipeline.
 *
 * Responsibilities:
 *  1. Maintain a WebSocket connection to the local NDI bridge server
 *  2. Manage an offscreen RenderTarget at the selected NDI resolution
 *  3. Run a lighting pre-processor shader (black floor, gamma, Y-flip, channel/alpha modes)
 *  4. Throttle readPixels to the target FPS
 *  5. Dispatch telemetry events (status, receivers, tally)
 */

import * as THREE from 'three';

/* ------------------------------------------------------------------ */
/*  Types & Presets                                                    */
/* ------------------------------------------------------------------ */

export type NdiStatus = 'off' | 'connecting' | 'broadcasting' | 'connected' | 'tally-program';
export type ChannelMode = 'rgb' | 'luma' | 'red' | 'green' | 'blue';
export type AlphaMode = 'opaque' | 'luma-alpha' | 'luma-alpha-inv';

export interface NdiResolutionPreset {
  label: string;
  width: number;
  height: number;
}

export const NDI_RESOLUTION_PRESETS: NdiResolutionPreset[] = [
  { label: 'Matrix 512×512', width: 512, height: 512 },
  { label: 'Matrix 256×256', width: 256, height: 256 },
  { label: 'Matrix 128×128', width: 128, height: 128 },
  { label: 'Linear 512×64', width: 512, height: 64 },
  { label: 'Wide 1024×128', width: 1024, height: 128 },
  { label: 'Full HD 1920×1080', width: 1920, height: 1080 },
  { label: 'HD 1280×720', width: 1280, height: 720 },
];

export const NDI_FPS_OPTIONS = [15, 25, 30, 60] as const;

export interface NdiParams {
  enabled: boolean;
  streamName: string;
  presetIndex: number; // index into NDI_RESOLUTION_PRESETS, -1 = custom
  customWidth: number;
  customHeight: number;
  fps: number;
  blackCutoff: number; // 0.0 – 0.25
  gamma: number; // 0.5 – 2.5
  channelMode: ChannelMode;
  alphaMode: AlphaMode;
}

export function defaultNdiParams(): NdiParams {
  return {
    enabled: false,
    streamName: 'LZX-V Synth Output',
    presetIndex: 0,
    customWidth: 64,
    customHeight: 16,
    fps: 30,
    blackCutoff: 0.02,
    gamma: 1.0,
    channelMode: 'rgb',
    alphaMode: 'opaque',
  };
}

/* ------------------------------------------------------------------ */
/*  Status event callback                                              */
/* ------------------------------------------------------------------ */

export type NdiStatusCallback = (info: {
  status: NdiStatus;
  receivers: number;
  onProgram: boolean;
  onPreview: boolean;
  fps: number;
  resolution: { w: number; h: number };
}) => void;

/* ------------------------------------------------------------------ */
/*  Lighting Pre-Processor Shader                                      */
/* ------------------------------------------------------------------ */

const LIGHTING_VERTEX = /* glsl */ `
  varying vec2 vUv;
  void main() {
    vUv = uv;
    gl_Position = vec4(position, 1.0);
  }
`;

const LIGHTING_FRAGMENT = /* glsl */ `
  uniform sampler2D u_source;
  uniform float u_blackCutoff;
  uniform float u_gamma;
  uniform int u_channelMode;   // 0=rgb, 1=luma, 2=red, 3=green, 4=blue
  uniform int u_alphaMode;     // 0=opaque, 1=luma-alpha, 2=luma-alpha-inv
  varying vec2 vUv;

  void main() {
    // Flip Y: WebGL textures are bottom-up; NDI expects top-down
    vec2 uv = vec2(vUv.x, 1.0 - vUv.y);
    vec4 col = texture2D(u_source, uv);

    // --- Black Floor Cutoff (DMX zero gate) ---
    float luma = dot(col.rgb, vec3(0.2126, 0.7152, 0.0722));
    if (luma < u_blackCutoff) {
      col.rgb = vec3(0.0);
      luma = 0.0;
    }

    // --- Gamma correction ---
    col.rgb = pow(col.rgb, vec3(u_gamma));

    // --- Channel mode ---
    if (u_channelMode == 1) {       // Luma / monochrome
      float l = dot(col.rgb, vec3(0.2126, 0.7152, 0.0722));
      col.rgb = vec3(l);
    } else if (u_channelMode == 2) { // Red only
      col.rgb = vec3(col.r);
    } else if (u_channelMode == 3) { // Green only
      col.rgb = vec3(col.g);
    } else if (u_channelMode == 4) { // Blue only
      col.rgb = vec3(col.b);
    }

    // --- Alpha mode ---
    if (u_alphaMode == 0) {
      col.a = 1.0;
    } else if (u_alphaMode == 1) {   // Luma as alpha
      float l = dot(col.rgb, vec3(0.2126, 0.7152, 0.0722));
      col.a = l;
    } else if (u_alphaMode == 2) {   // Inverted luma as alpha
      float l = dot(col.rgb, vec3(0.2126, 0.7152, 0.0722));
      col.a = 1.0 - l;
    }

    gl_FragColor = clamp(col, 0.0, 1.0);
  }
`;

/* ------------------------------------------------------------------ */
/*  NdiEngine class                                                    */
/* ------------------------------------------------------------------ */

export class NdiEngine {
  public params: NdiParams;

  private ws: WebSocket | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private statusCallback: NdiStatusCallback | null = null;

  // WebGL resources
  private ndiTarget: THREE.WebGLRenderTarget | null = null;
  private lightingMaterial: THREE.ShaderMaterial;
  private lightingScene: THREE.Scene;
  private lightingCamera: THREE.OrthographicCamera;
  private pixelBuffer: Uint8Array | null = null;

  // Throttle
  private lastFrameTime = 0;

  // Telemetry state
  private _status: NdiStatus = 'off';
  private _receivers = 0;
  private _onProgram = false;
  private _onPreview = false;

  constructor(params?: Partial<NdiParams>) {
    this.params = { ...defaultNdiParams(), ...params };

    // Build the lighting post-process scene
    this.lightingMaterial = new THREE.ShaderMaterial({
      uniforms: {
        u_source: { value: null },
        u_blackCutoff: { value: this.params.blackCutoff },
        u_gamma: { value: this.params.gamma },
        u_channelMode: { value: 0 },
        u_alphaMode: { value: 0 },
      },
      vertexShader: LIGHTING_VERTEX,
      fragmentShader: LIGHTING_FRAGMENT,
    });

    const quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), this.lightingMaterial);
    this.lightingScene = new THREE.Scene();
    this.lightingScene.add(quad);
    this.lightingCamera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  }

  /* ---- public API ------------------------------------------------ */

  onStatus(cb: NdiStatusCallback): void {
    this.statusCallback = cb;
  }

  /** Get the current active NDI resolution */
  getResolution(): { w: number; h: number } {
    if (this.params.presetIndex >= 0 && this.params.presetIndex < NDI_RESOLUTION_PRESETS.length) {
      const p = NDI_RESOLUTION_PRESETS[this.params.presetIndex];
      return { w: p.width, h: p.height };
    }
    return { w: this.params.customWidth, h: this.params.customHeight };
  }

  /** Toggle NDI output on/off */
  setEnabled(on: boolean): void {
    this.params.enabled = on;
    if (on) {
      this._connect();
    } else {
      this._sendControl({ type: 'stop' });
      this._setStatus('off');
      this._disconnect();
    }
  }

  /** Push current params to the bridge (call after UI changes) */
  pushConfig(): void {
    const res = this.getResolution();
    this._ensureTarget(res.w, res.h);
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this._sendControl({
        type: 'config',
        width: res.w,
        height: res.h,
        fps: this.params.fps,
        name: this.params.streamName,
      });
    }
  }

  /**
   * Called every frame from the main render loop.
   * Reads the current synth output texture, runs the lighting shader,
   * reads back pixels, and ships them to the bridge.
   */
  renderAndSend(renderer: THREE.WebGLRenderer, sourceTexture: THREE.Texture): void {
    if (!this.params.enabled) return;
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;

    // Throttle to target FPS
    const now = performance.now();
    const interval = 1000 / this.params.fps;
    if (now - this.lastFrameTime < interval) return;
    this.lastFrameTime = now;

    // Backpressure: skip if the socket is congested
    if (this.ws.bufferedAmount > 0) return;

    const res = this.getResolution();
    this._ensureTarget(res.w, res.h);

    // Update lighting uniforms
    this.lightingMaterial.uniforms.u_source.value = sourceTexture;
    this.lightingMaterial.uniforms.u_blackCutoff.value = this.params.blackCutoff;
    this.lightingMaterial.uniforms.u_gamma.value = this.params.gamma;
    this.lightingMaterial.uniforms.u_channelMode.value = this._channelModeInt();
    this.lightingMaterial.uniforms.u_alphaMode.value = this._alphaModeInt();

    // Render the lighting pass into the NDI-sized target
    renderer.setRenderTarget(this.ndiTarget!);
    renderer.render(this.lightingScene, this.lightingCamera);

    // Read pixels (RGBA, Uint8)
    renderer.readRenderTargetPixels(this.ndiTarget!, 0, 0, res.w, res.h, this.pixelBuffer!);

    // Ship the raw buffer over the WebSocket
    this.ws.send(this.pixelBuffer!);

    // Restore the render target (caller should set it themselves too)
    renderer.setRenderTarget(null);
  }

  /** Clean up all resources */
  destroy(): void {
    this._disconnect();
    if (this.ndiTarget) {
      this.ndiTarget.dispose();
      this.ndiTarget = null;
    }
    this.lightingMaterial.dispose();
  }

  /* ---- WebSocket management -------------------------------------- */

  private _connect(): void {
    if (this.ws) return;

    this._setStatus('connecting');

    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const url = `${protocol}//${window.location.host}/ndi-bridge`;

    try {
      this.ws = new WebSocket(url);
      this.ws.binaryType = 'arraybuffer';
    } catch {
      this._scheduleReconnect();
      return;
    }

    this.ws.onopen = () => {
      console.log('[NdiEngine] Connected to bridge');
      const res = this.getResolution();
      this._sendControl({
        type: 'start',
        width: res.w,
        height: res.h,
        fps: this.params.fps,
        name: this.params.streamName,
      });
    };

    this.ws.onmessage = (ev) => {
      if (typeof ev.data === 'string') {
        this._handleBridgeMessage(ev.data);
      }
    };

    this.ws.onclose = () => {
      console.log('[NdiEngine] Bridge connection closed');
      this.ws = null;
      if (this.params.enabled) {
        this._scheduleReconnect();
      }
    };

    this.ws.onerror = () => {
      this.ws?.close();
    };
  }

  private _disconnect(): void {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
    if (this.ws) {
      this.ws.onclose = null;
      this.ws.close();
      this.ws = null;
    }
  }

  private _scheduleReconnect(): void {
    if (this.reconnectTimer) return;
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      if (this.params.enabled) {
        this._connect();
      }
    }, 2000);
  }

  private _sendControl(msg: any): void {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg));
    }
  }

  private _handleBridgeMessage(raw: string): void {
    let msg: any;
    try {
      msg = JSON.parse(raw);
    } catch {
      return;
    }

    switch (msg.type) {
      case 'status':
        if (msg.state === 'broadcasting') {
          this._setStatus(this._receivers > 0 ? 'connected' : 'broadcasting');
        } else {
          this._setStatus('off');
        }
        break;

      case 'receivers':
        this._receivers = msg.count ?? 0;
        if (this._status === 'broadcasting' && this._receivers > 0) {
          this._setStatus('connected');
        } else if (this._status === 'connected' && this._receivers === 0) {
          this._setStatus('broadcasting');
        }
        this._emitStatus();
        break;

      case 'tally':
        this._onProgram = msg.onProgram ?? false;
        this._onPreview = msg.onPreview ?? false;
        if (this._onProgram && (this._status === 'connected' || this._status === 'broadcasting')) {
          this._setStatus('tally-program');
        } else if (!this._onProgram && this._status === 'tally-program') {
          this._setStatus(this._receivers > 0 ? 'connected' : 'broadcasting');
        }
        this._emitStatus();
        break;
    }
  }

  /* ---- render target management ---------------------------------- */

  private _ensureTarget(w: number, h: number): void {
    if (this.ndiTarget && this.ndiTarget.width === w && this.ndiTarget.height === h) {
      return;
    }

    if (this.ndiTarget) {
      this.ndiTarget.dispose();
    }

    this.ndiTarget = new THREE.WebGLRenderTarget(w, h, {
      minFilter: THREE.LinearFilter,
      magFilter: THREE.LinearFilter,
      format: THREE.RGBAFormat,
      type: THREE.UnsignedByteType, // readPixels needs Uint8
    });

    this.pixelBuffer = new Uint8Array(w * h * 4);
    console.log(`[NdiEngine] RenderTarget: ${w}×${h}`);
  }

  /* ---- helpers --------------------------------------------------- */

  private _channelModeInt(): number {
    switch (this.params.channelMode) {
      case 'rgb':
        return 0;
      case 'luma':
        return 1;
      case 'red':
        return 2;
      case 'green':
        return 3;
      case 'blue':
        return 4;
      default:
        return 0;
    }
  }

  private _alphaModeInt(): number {
    switch (this.params.alphaMode) {
      case 'opaque':
        return 0;
      case 'luma-alpha':
        return 1;
      case 'luma-alpha-inv':
        return 2;
      default:
        return 0;
    }
  }

  private _setStatus(s: NdiStatus): void {
    this._status = s;
    this._emitStatus();
  }

  private _emitStatus(): void {
    this.statusCallback?.({
      status: this._status,
      receivers: this._receivers,
      onProgram: this._onProgram,
      onPreview: this._onPreview,
      fps: this.params.fps,
      resolution: this.getResolution(),
    });
  }
}
