import * as THREE from 'three';
import { AudioEngine } from './audio';

/**
 * Simple rotary knob implementation using an HTML <input type="range">.
 * The element is styled with CSS to look like a classic analog knob.
 * The function returns an object with the DOM element and a callback that
 * receives the current value (0‑1). The caller can bind this to shader
 * uniforms or other parameters.
 */
export function createKnob(label: string, min: number, max: number, step: number, init: number, onChange: (v: number) => void) {
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
 * Build the hardware‑style rack UI. This function is called once after
 * the page loads and appends a set of knobs to the #synth-rack element.
 * It returns a plain object exposing the current parameter values.
 */
export function buildSynthRack(params: any) {
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
  bind('audioToFractal', { label: 'Mic→Fract', min: 0, max: 1, step: 0.01, init: params.audioToFractal });
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
}
