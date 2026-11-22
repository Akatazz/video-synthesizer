import {
  NdiEngine,
  NDI_RESOLUTION_PRESETS,
  NDI_FPS_OPTIONS,
  type ChannelMode,
  type AlphaMode,
} from './ndi-engine';

/**
 * Simple rotary knob implementation using an HTML <input type="range">.
 * The element is styled with CSS to look like a classic analog knob.
 * The function returns an object with the DOM element and a callback that
 * receives the current value (0‑1). The caller can bind this to shader
 * uniforms or other parameters.
 */
export function createKnob(
  label: string,
  min: number,
  max: number,
  step: number,
  init: number,
  onChange: (v: number) => void,
) {
  const container = document.createElement('div');
  container.className = 'knob';

  const input = document.createElement('input');
  input.type = 'range';
  input.min = String(min);
  input.max = String(max);
  input.step = String(step);
  input.value = String(init);
  input.className = 'rotary';
  // simple CSS rotation via background will be added in the main HTML file
  input.addEventListener('input', () => {
    const v = parseFloat(input.value);
    onChange(v);
  });

  const lbl = document.createElement('div');
  lbl.className = 'knob-label';
  lbl.textContent = label;

  container.appendChild(input);
  container.appendChild(lbl);

  return { element: container, input };
}

/**
 * Create a styled <select> dropdown.
 */
function createSelect(
  label: string,
  options: string[],
  initIndex: number,
  onChange: (index: number, value: string) => void,
) {
  const container = document.createElement('div');
  container.className = 'ndi-select-group';

  const lbl = document.createElement('label');
  lbl.className = 'ndi-label';
  lbl.textContent = label;

  const sel = document.createElement('select');
  sel.className = 'ndi-select';
  options.forEach((opt, i) => {
    const o = document.createElement('option');
    o.value = String(i);
    o.textContent = opt;
    if (i === initIndex) o.selected = true;
    sel.appendChild(o);
  });
  sel.addEventListener('change', () => {
    onChange(parseInt(sel.value), sel.options[sel.selectedIndex].textContent || '');
  });

  container.appendChild(lbl);
  container.appendChild(sel);
  return { element: container, select: sel };
}

/**
 * Build the hardware‑style rack UI. This function is called once after
 * the page loads and appends a set of knobs to the #synth-rack element.
 * It returns a plain object exposing the current parameter values.
 */
export function buildSynthRack(params: any, ndiEngine?: NdiEngine) {
  const rack = document.getElementById('synth-rack');
  if (!rack) return;
  rack.style.display = 'block';

  // Helper to create a knob and bind it directly to params
  const bind = (key: string, opts: any) => {
    const knob = createKnob(opts.label, opts.min, opts.max, opts.step, opts.init, (v: number) => {
      params[key] = v;
    });
    rack.appendChild(knob.element);
  };

  // Oscillator section
  const oscSection = document.createElement('div');
  oscSection.className = 'module';
  const oscTitle = document.createElement('div');
  oscTitle.className = 'module-title';
  oscTitle.textContent = 'Oscillator (LFO)';
  oscSection.appendChild(oscTitle);
  bind('oscFreq', { label: 'Freq', min: 0.1, max: 20, step: 0.1, init: params.oscFreq });
  bind('oscSpeed', { label: 'Rate', min: 0, max: 2, step: 0.01, init: params.oscSpeed });
  rack.appendChild(oscSection);

  // Fractal section
  const fracSection = document.createElement('div');
  fracSection.className = 'module';
  const fracTitle = document.createElement('div');
  fracTitle.className = 'module-title';
  fracTitle.textContent = 'Fractal Core';
  fracSection.appendChild(fracTitle);
  bind('fractalZoom', { label: 'Zoom', min: 0.1, max: 5, step: 0.01, init: params.fractalZoom });
  bind('fractalDetail', { label: 'Iter', min: 1, max: 30, step: 1, init: params.fractalDetail });
  bind('fractalMorph', { label: 'Morph', min: -1, max: 1, step: 0.01, init: params.fractalMorph });
  rack.appendChild(fracSection);

  // Cellular Automaton section
  const caSection = document.createElement('div');
  caSection.className = 'module';
  const caTitle = document.createElement('div');
  caTitle.className = 'module-title';
  caTitle.textContent = 'Cellular Automaton';
  caSection.appendChild(caTitle);
  bind('caFeed', { label: 'Feed', min: 0.01, max: 0.1, step: 0.001, init: params.caFeed });
  bind('caKill', { label: 'Kill', min: 0.01, max: 0.1, step: 0.001, init: params.caKill });
  bind('feedbackMix', { label: 'Mix', min: 0, max: 1, step: 0.01, init: params.feedbackMix });
  rack.appendChild(caSection);

  // Audio routing (CV) section
  const cvSection = document.createElement('div');
  cvSection.className = 'module';
  const cvTitle = document.createElement('div');
  cvTitle.className = 'module-title';
  cvTitle.textContent = 'Audio CV Patching';
  cvSection.appendChild(cvTitle);
  bind('audioToOsc', { label: 'Mic→Osc', min: 0, max: 1, step: 0.01, init: params.audioToOsc });
  bind('audioToFractal', {
    label: 'Mic→Fract',
    min: 0,
    max: 1,
    step: 0.01,
    init: params.audioToFractal,
  });
  bind('audioToCA', { label: 'Mic→CA', min: 0, max: 1, step: 0.01, init: params.audioToCA });
  rack.appendChild(cvSection);

  // Color section
  const colSection = document.createElement('div');
  colSection.className = 'module';
  const colTitle = document.createElement('div');
  colTitle.className = 'module-title';
  colTitle.textContent = 'Color';
  colSection.appendChild(colTitle);
  bind('colorShift', { label: 'Hue', min: 0, max: 1, step: 0.01, init: params.colorShift });
  rack.appendChild(colSection);

  // ============================================================
  //  NDI BROADCASTER MODULE
  // ============================================================
  if (ndiEngine) {
    buildNdiModule(rack, ndiEngine);
  }
}

/* ------------------------------------------------------------------ */
/*  NDI Broadcaster Module                                             */
/* ------------------------------------------------------------------ */

function buildNdiModule(rack: HTMLElement, engine: NdiEngine): void {
  const section = document.createElement('div');
  section.className = 'module ndi-module';

  // ---- Title row ----
  const titleRow = document.createElement('div');
  titleRow.className = 'ndi-title-row';

  const title = document.createElement('div');
  title.className = 'module-title ndi-module-title';
  title.textContent = 'NDI OUTPUT';

  const statusLed = document.createElement('span');
  statusLed.className = 'ndi-led';
  statusLed.title = 'Off';
  titleRow.appendChild(title);
  titleRow.appendChild(statusLed);
  section.appendChild(titleRow);

  // ---- Power toggle ----
  const toggleRow = document.createElement('div');
  toggleRow.className = 'ndi-toggle-row';

  const toggleLabel = document.createElement('label');
  toggleLabel.className = 'ndi-switch-label';
  toggleLabel.textContent = 'BROADCAST';

  const toggle = document.createElement('input');
  toggle.type = 'checkbox';
  toggle.className = 'ndi-toggle';
  toggle.checked = engine.params.enabled;
  toggle.addEventListener('change', () => {
    engine.setEnabled(toggle.checked);
  });

  toggleRow.appendChild(toggleLabel);
  toggleRow.appendChild(toggle);
  section.appendChild(toggleRow);

  // ---- Status line ----
  const statusLine = document.createElement('div');
  statusLine.className = 'ndi-status-line';
  statusLine.textContent = '⚪ Off';
  section.appendChild(statusLine);

  // ---- Stream name ----
  const nameRow = document.createElement('div');
  nameRow.className = 'ndi-row';
  const nameLabel = document.createElement('label');
  nameLabel.className = 'ndi-label';
  nameLabel.textContent = 'Stream Name';
  const nameInput = document.createElement('input');
  nameInput.type = 'text';
  nameInput.className = 'ndi-text-input';
  nameInput.value = engine.params.streamName;
  nameInput.addEventListener('change', () => {
    engine.params.streamName = nameInput.value || 'LZX-V Synth Output';
    engine.pushConfig();
  });
  nameRow.appendChild(nameLabel);
  nameRow.appendChild(nameInput);
  section.appendChild(nameRow);

  // ---- Resolution selector ----
  const resLabels = [...NDI_RESOLUTION_PRESETS.map((p) => p.label), 'Custom…'];
  const resSel = createSelect(
    'Resolution',
    resLabels,
    engine.params.presetIndex >= 0 ? engine.params.presetIndex : resLabels.length - 1,
    (idx) => {
      if (idx < NDI_RESOLUTION_PRESETS.length) {
        engine.params.presetIndex = idx;
        customResRow.style.display = 'none';
      } else {
        engine.params.presetIndex = -1;
        customResRow.style.display = 'flex';
      }
      engine.pushConfig();
    },
  );
  section.appendChild(resSel.element);

  // Custom resolution row (hidden by default)
  const customResRow = document.createElement('div');
  customResRow.className = 'ndi-custom-res-row';
  customResRow.style.display = engine.params.presetIndex === -1 ? 'flex' : 'none';

  const cw = document.createElement('input');
  cw.type = 'number';
  cw.className = 'ndi-num-input';
  cw.value = String(engine.params.customWidth);
  cw.min = '1';
  cw.max = '4096';
  cw.placeholder = 'W';
  cw.addEventListener('change', () => {
    engine.params.customWidth = Math.max(1, parseInt(cw.value) || 64);
    engine.pushConfig();
  });

  const sep = document.createElement('span');
  sep.textContent = '×';
  sep.className = 'ndi-res-sep';

  const ch = document.createElement('input');
  ch.type = 'number';
  ch.className = 'ndi-num-input';
  ch.value = String(engine.params.customHeight);
  ch.min = '1';
  ch.max = '4096';
  ch.placeholder = 'H';
  ch.addEventListener('change', () => {
    engine.params.customHeight = Math.max(1, parseInt(ch.value) || 16);
    engine.pushConfig();
  });

  customResRow.appendChild(cw);
  customResRow.appendChild(sep);
  customResRow.appendChild(ch);
  section.appendChild(customResRow);

  // ---- FPS selector ----
  const fpsSel = createSelect(
    'Frame Rate',
    NDI_FPS_OPTIONS.map((f) => `${f} fps`),
    NDI_FPS_OPTIONS.indexOf(engine.params.fps as any),
    (idx) => {
      engine.params.fps = NDI_FPS_OPTIONS[idx];
      engine.pushConfig();
    },
  );
  section.appendChild(fpsSel.element);

  // ---- Lighting knobs ----
  const lightingTitle = document.createElement('div');
  lightingTitle.className = 'ndi-sub-title';
  lightingTitle.textContent = 'Lighting Calibration';
  section.appendChild(lightingTitle);

  const cutoffKnob = createKnob('Blk Cut', 0, 0.25, 0.005, engine.params.blackCutoff, (v) => {
    engine.params.blackCutoff = v;
  });
  section.appendChild(cutoffKnob.element);

  const gammaKnob = createKnob('Gamma', 0.5, 2.5, 0.05, engine.params.gamma, (v) => {
    engine.params.gamma = v;
  });
  section.appendChild(gammaKnob.element);

  // ---- Channel mode ----
  const channelModes: ChannelMode[] = ['rgb', 'luma', 'red', 'green', 'blue'];
  const channelSel = createSelect(
    'Channel',
    ['Full Color (RGB)', 'Monochrome / Luma', 'Red Only', 'Green Only', 'Blue Only'],
    channelModes.indexOf(engine.params.channelMode),
    (idx) => {
      engine.params.channelMode = channelModes[idx];
    },
  );
  section.appendChild(channelSel.element);

  // ---- Alpha mode ----
  const alphaModes: AlphaMode[] = ['opaque', 'luma-alpha', 'luma-alpha-inv'];
  const alphaSel = createSelect(
    'Alpha',
    ['Opaque', 'Luma → Alpha', 'Inv Luma → Alpha'],
    alphaModes.indexOf(engine.params.alphaMode),
    (idx) => {
      engine.params.alphaMode = alphaModes[idx];
    },
  );
  section.appendChild(alphaSel.element);

  // ---- Console Setup Guide button ----
  const guideBtn = document.createElement('button');
  guideBtn.className = 'ndi-guide-btn';
  guideBtn.textContent = '📖 Console Setup Guide';
  guideBtn.addEventListener('click', () => {
    const modal = document.getElementById('ndi-setup-modal');
    if (modal) modal.style.display = 'flex';
  });
  section.appendChild(guideBtn);

  // ---- Register status callback ----
  engine.onStatus((info) => {
    // Update LED
    statusLed.className = 'ndi-led';
    switch (info.status) {
      case 'off':
        statusLed.classList.add('led-off');
        statusLed.title = 'Off';
        statusLine.textContent = '⚪ Off';
        break;
      case 'connecting':
        statusLed.classList.add('led-connecting');
        statusLed.title = 'Connecting…';
        statusLine.textContent = '🟡 Connecting…';
        break;
      case 'broadcasting':
        statusLed.classList.add('led-broadcasting');
        statusLed.title = 'Broadcasting (0 receivers)';
        statusLine.textContent = `🟢 Broadcasting — ${info.resolution.w}×${info.resolution.h} @ ${info.fps}fps`;
        break;
      case 'connected':
        statusLed.classList.add('led-connected');
        statusLed.title = `${info.receivers} receiver(s) connected`;
        statusLine.textContent = `🔵 Connected — Receivers: ${info.receivers}`;
        break;
      case 'tally-program':
        statusLed.classList.add('led-tally');
        statusLed.title = 'LIVE on Program!';
        statusLine.textContent = `🔴 LIVE — Program Output (${info.receivers} receiver${info.receivers !== 1 ? 's' : ''})`;
        break;
    }
  });

  rack.appendChild(section);
}
