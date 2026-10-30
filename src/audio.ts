export class AudioEngine {
  private ctx: AudioContext | null = null;
  private analyser: AnalyserNode | null = null;
  private dataArray: Uint8Array | null = null;
  
  public async init(): Promise<void> {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
      
      const AudioContextClass = window.AudioContext || (window as any).webkitAudioContext;
      this.ctx = new AudioContextClass();
      
      const source = this.ctx.createMediaStreamSource(stream);
      this.analyser = this.ctx.createAnalyser();
      this.analyser.fftSize = 512;
      this.analyser.smoothingTimeConstant = 0.8;
      
      source.connect(this.analyser);
      
      this.dataArray = new Uint8Array(this.analyser.frequencyBinCount);
    } catch (err) {
      console.error('Failed to initialize AudioEngine:', err);
      throw err;
    }
  }

  public update(): { bass: number; mid: number; high: number } {
    if (!this.analyser || !this.dataArray || !this.ctx) {
      return { bass: 0, mid: 0, high: 0 };
    }

    this.analyser.getByteFrequencyData(this.dataArray as Uint8Array<ArrayBuffer>);
    
    const sampleRate = this.ctx.sampleRate;
    const nyquist = sampleRate / 2;
    const binCount = this.analyser.frequencyBinCount;
    const hzPerBin = nyquist / binCount;

    const getAverage = (startHz: number, endHz: number): number => {
      let startBin = Math.floor(startHz / hzPerBin);
      let endBin = Math.ceil(endHz / hzPerBin);
      
      startBin = Math.max(0, startBin);
      endBin = Math.min(binCount - 1, endBin);
      
      if (startBin > endBin) return 0;
      
      let sum = 0;
      let count = 0;
      for (let i = startBin; i <= endBin; i++) {
        sum += this.dataArray![i];
        count++;
      }
      
      return count > 0 ? (sum / count) / 255.0 : 0;
    };

    const bass = getAverage(20, 250);
    const mid = getAverage(250, 2000);
    const high = getAverage(2000, nyquist);

    return { bass, mid, high };
  }
}
