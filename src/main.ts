import * as THREE from 'three';
import { AudioEngine } from './audio';
import { buildSynthRack } from './ui';
import { NdiEngine } from './ndi-engine';

const init = () => {
  const container = document.getElementById('app');
  if (!container) return;

  // Parameter object that will be mutated by the UI knobs
  const synthParams: any = {
    oscFreq: 2.0,
    oscSpeed: 0.5,
    fractalZoom: 1.5,
    fractalDetail: 10,
    fractalMorph: 0.2,
    caFeed: 0.045,
    caKill: 0.060,
    feedbackMix: 0.95,
    audioToOsc: 0.5,
    audioToFractal: 0.5,
    audioToCA: 0.2,
    colorShift: 0.0,
    preset: 'Classic Feedback'
  };

  // NDI Engine — manages the WebSocket bridge and GPU readback pipeline
  const ndiEngine = new NdiEngine();

  // Audio Engine
  const audio = new AudioEngine();
  const audioBtn = document.getElementById('btn-audio');
  if (audioBtn) {
    audioBtn.addEventListener('click', async () => {
      await audio.init();
      audioBtn.style.display = 'none';
      // Build the hardware‑style rack once audio is active (pass NDI engine)
      buildSynthRack(synthParams, ndiEngine);
    });
  }

  const renderer = new THREE.WebGLRenderer({ antialias: false, powerPreference: "high-performance" });
  renderer.setSize(window.innerWidth, window.innerHeight);
  renderer.setPixelRatio(window.devicePixelRatio);
  container.appendChild(renderer.domElement);

  const camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);

  const options = { minFilter: THREE.LinearFilter, magFilter: THREE.LinearFilter, format: THREE.RGBAFormat, type: THREE.FloatType };
  let targetA = new THREE.WebGLRenderTarget(window.innerWidth, window.innerHeight, options);
  let targetB = new THREE.WebGLRenderTarget(window.innerWidth, window.innerHeight, options);

  // ---- SHADER CODE (unchanged) ----
  const feedbackShader = `
    uniform float u_time;
    uniform vec2 u_resolution;
    uniform sampler2D u_feedbackTex;
    uniform float u_audioBass;
    uniform float u_audioMid;
    uniform float u_audioHigh;

    // Synth Params uniforms (filled from synthParams)
    uniform float p_oscFreq;
    uniform float p_oscSpeed;
    uniform float p_fractalZoom;
    uniform float p_fractalDetail;
    uniform float p_fractalMorph;
    uniform float p_caFeed;
    uniform float p_caKill;
    uniform float p_feedbackMix;
    uniform float p_audioToOsc;
    uniform float p_audioToFractal;
    uniform float p_audioToCA;
    uniform float p_colorShift;

    varying vec2 vUv;
    vec2 cxSqr(vec2 a) { return vec2(a.x*a.x - a.y*a.y, 2.0*a.x*a.y); }
    vec3 generateFractal(vec2 uv, vec2 c) {
        vec2 z = (uv - 0.5) * p_fractalZoom * 5.0;
        float iter = 0.0;
        for(int i=0; i<30; i++) {
            if(float(i) >= p_fractalDetail) break;
            z = cxSqr(z) + c;
            if(dot(z,z) > 4.0) break;
            iter += 1.0;
        }
        float smoothIter = iter - log2(max(1.0, log2(dot(z,z))));
        float val = smoothIter / p_fractalDetail;
        return vec3(val, val * 0.5, sin(val * 3.14)*0.5 + 0.5);
    }
    vec3 palette( in float t, in vec3 a, in vec3 b, in vec3 c, in vec3 d ) {
        return a + b*cos( 6.28318*(c*t+d) );
    }
    void main() {
      vec2 st = gl_FragCoord.xy / u_resolution.xy;
      vec2 texel = 1.0 / u_resolution.xy;

      // Audio‑CV modulations
      float dynSpeed = p_oscSpeed + (u_audioMid * p_audioToOsc * 5.0);
      float dynMorph = p_fractalMorph + (u_audioBass * p_audioToFractal * 0.5);
      float dynFeed = p_caFeed + (u_audioHigh * p_audioToCA * 0.05);

      // Spatial warping (oscillator)
      vec2 oscWarp = vec2(
          sin(st.y * p_oscFreq + u_time * dynSpeed),
          cos(st.x * p_oscFreq + u_time * dynSpeed)
      ) * 0.01;

      // Fractal seed (Julia set)
      vec2 juliaC = vec2(dynMorph, 0.156 + sin(u_time*0.1)*0.1);
      vec3 fractalShape = generateFractal(st + oscWarp, juliaC);

      // Reaction‑Diffusion cellular automaton (Gray‑Scott)
      vec4 sum = vec4(0.0);
      sum += texture2D(u_feedbackTex, st + vec2(-1, -1) * texel) * 0.05;
      sum += texture2D(u_feedbackTex, st + vec2( 0, -1) * texel) * 0.2;
      sum += texture2D(u_feedbackTex, st + vec2( 1, -1) * texel) * 0.05;
      sum += texture2D(u_feedbackTex, st + vec2(-1,  0) * texel) * 0.2;
      sum += texture2D(u_feedbackTex, st) * -1.0;
      sum += texture2D(u_feedbackTex, st + vec2( 1,  0) * texel) * 0.2;
      sum += texture2D(u_feedbackTex, st + vec2(-1,  1) * texel) * 0.05;
      sum += texture2D(u_feedbackTex, st + vec2( 0,  1) * texel) * 0.2;
      sum += texture2D(u_feedbackTex, st + vec2( 1,  1) * texel) * 0.05;

      vec4 lastFrame = texture2D(u_feedbackTex, st + (oscWarp*0.5));
      vec4 reaction = lastFrame + (sum * 1.0) - (lastFrame * lastFrame * lastFrame) + dynFeed * (1.0 - lastFrame);
      reaction -= p_caKill * lastFrame;

      vec3 finalColor = palette(reaction.r + p_colorShift, vec3(0.5), vec3(0.5), vec3(1.0), vec3(0.0, 0.33, 0.67));

      // Mix fractal seed and reaction‑diffusion
      vec3 composite = mix(finalColor * reaction.rgb, fractalShape, 0.05);

      // Dry/Wet mix
      gl_FragColor = mix(vec4(composite,1.0), vec4(fractalShape,1.0), 1.0 - p_feedbackMix);
      gl_FragColor = clamp(gl_FragColor,0.0,1.0);
    }
  `;

  const feedbackMaterial = new THREE.ShaderMaterial({
    uniforms: {
      u_time: { value: 0 },
      u_resolution: { value: new THREE.Vector2(window.innerWidth, window.innerHeight) },
      u_feedbackTex: { value: null },
      u_audioBass: { value: 0.0 },
      u_audioMid: { value: 0.0 },
      u_audioHigh: { value: 0.0 },
      // Params bound later each frame
      p_oscFreq: { value: synthParams.oscFreq },
      p_oscSpeed: { value: synthParams.oscSpeed },
      p_fractalZoom: { value: synthParams.fractalZoom },
      p_fractalDetail: { value: synthParams.fractalDetail },
      p_fractalMorph: { value: synthParams.fractalMorph },
      p_caFeed: { value: synthParams.caFeed },
      p_caKill: { value: synthParams.caKill },
      p_feedbackMix: { value: synthParams.feedbackMix },
      p_audioToOsc: { value: synthParams.audioToOsc },
      p_audioToFractal: { value: synthParams.audioToFractal },
      p_audioToCA: { value: synthParams.audioToCA },
      p_colorShift: { value: synthParams.colorShift }
    },
    vertexShader: `
      varying vec2 vUv;
      void main(){ vUv = uv; gl_Position = vec4(position,1.0); }
    `,
    fragmentShader: feedbackShader
  });

  const feedbackPlane = new THREE.Mesh(new THREE.PlaneGeometry(2,2), feedbackMaterial);
  const feedbackScene = new THREE.Scene();
  feedbackScene.add(feedbackPlane);

  const screenScene = new THREE.Scene();
  const screenMaterial = new THREE.ShaderMaterial({
    uniforms: { u_texture: { value: null } },
    vertexShader: `
      varying vec2 vUv;
      void main(){ vUv = uv; gl_Position = vec4(position,1.0); }
    `,
    fragmentShader: `
      uniform sampler2D u_texture;
      varying vec2 vUv;
      void main(){ gl_FragColor = texture2D(u_texture, vUv); }
    `
  });
  const screenPlane = new THREE.Mesh(new THREE.PlaneGeometry(2,2), screenMaterial);
  screenScene.add(screenPlane);

  const animate = (time: number) => {
    feedbackMaterial.uniforms.u_time.value = time * 0.001;
    // Update param uniforms from UI knobs (they are mutated in synthParams)
    feedbackMaterial.uniforms.p_oscFreq.value = synthParams.oscFreq;
    feedbackMaterial.uniforms.p_oscSpeed.value = synthParams.oscSpeed;
    feedbackMaterial.uniforms.p_fractalZoom.value = synthParams.fractalZoom;
    feedbackMaterial.uniforms.p_fractalDetail.value = synthParams.fractalDetail;
    feedbackMaterial.uniforms.p_fractalMorph.value = synthParams.fractalMorph;
    feedbackMaterial.uniforms.p_caFeed.value = synthParams.caFeed;
    feedbackMaterial.uniforms.p_caKill.value = synthParams.caKill;
    feedbackMaterial.uniforms.p_feedbackMix.value = synthParams.feedbackMix;
    feedbackMaterial.uniforms.p_audioToOsc.value = synthParams.audioToOsc;
    feedbackMaterial.uniforms.p_audioToFractal.value = synthParams.audioToFractal;
    feedbackMaterial.uniforms.p_audioToCA.value = synthParams.audioToCA;
    feedbackMaterial.uniforms.p_colorShift.value = synthParams.colorShift;

    const audioData = audio.update();
    if (audioData) {
      feedbackMaterial.uniforms.u_audioBass.value = audioData.bass;
      feedbackMaterial.uniforms.u_audioMid.value = audioData.mid;
      feedbackMaterial.uniforms.u_audioHigh.value = audioData.high;
    }

    feedbackMaterial.uniforms.u_feedbackTex.value = targetA.texture;
    renderer.setRenderTarget(targetB);
    renderer.render(feedbackScene, camera);

    // ---- NDI: capture the synth output and send to the bridge ----
    ndiEngine.renderAndSend(renderer, targetB.texture);

    screenMaterial.uniforms.u_texture.value = targetB.texture;
    renderer.setRenderTarget(null);
    renderer.render(screenScene, camera);

    const tmp = targetA; targetA = targetB; targetB = tmp;
    requestAnimationFrame(animate);
  };

  requestAnimationFrame(animate);

  window.addEventListener('resize', () => {
    const w = window.innerWidth, h = window.innerHeight;
    renderer.setSize(w, h);
    targetA.setSize(w, h);
    targetB.setSize(w, h);
    feedbackMaterial.uniforms.u_resolution.value.set(w, h);
  });

  // Wire up the setup-guide modal close button
  const modalClose = document.getElementById('ndi-modal-close');
  if (modalClose) {
    modalClose.addEventListener('click', () => {
      const modal = document.getElementById('ndi-setup-modal');
      if (modal) modal.style.display = 'none';
    });
  }
};

init();
