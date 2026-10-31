import Accelerate
import Foundation

struct AudioBands: Equatable {
    var bass: Float = 0
    var mid: Float = 0
    var high: Float = 0
}

/// Single-worker periodic Hann FFT. Levels use the native audio contract.
final class AudioAnalysis {
    private let size = 512
    private let setup: FFTSetup
    private var samples = [Float](repeating: 0, count: 512)
    private var real = [Float](repeating: 0, count: 512)
    private var imaginary = [Float](repeating: 0, count: 512)
    private var count = 0
    private var levels = AudioBands()

    init() {
        // A fixed-size complex FFT supports any valid input device sample rate.
        setup = vDSP_create_fftsetup(9, FFTRadix(kFFTRadix2))!
    }
    deinit { vDSP_destroy_fftsetup(setup) }

    func push(_ sample: Float, sampleRate: Double) -> AudioBands {
        guard sampleRate >= 8000 && sampleRate <= 192000 else { return AudioBands() }
        samples[count] = sample.isFinite ? min(1, max(-1, sample)) : 0
        count += 1
        guard count == size else { return levels }
        count = 0
        let mean = samples.reduce(0, +) / Float(size)
        for i in 0..<size {
            real[i] = (samples[i] - mean) * (0.5 - 0.5 * cos(2 * .pi * Float(i) / Float(size)))
            imaginary[i] = 0
        }
        real.withUnsafeMutableBufferPointer { realBuffer in
            imaginary.withUnsafeMutableBufferPointer { imaginaryBuffer in
                var split = DSPSplitComplex(realp: realBuffer.baseAddress!, imagp: imaginaryBuffer.baseAddress!)
                vDSP_fft_zip(setup, &split, 1, 9, FFTDirection(FFT_FORWARD))
            }
        }
        var power = [Float](repeating: 0, count: 3)
        for bin in 1...size/2 {
            let hz = Double(bin) * sampleRate / Double(size)
            if hz < 20 { continue }
            let band = hz < 250 ? 0 : hz < 2000 ? 1 : 2
            power[band] += (real[bin]*real[bin] + imaginary[bin]*imaginary[bin]) * 16 / (Float(size*size) * 1.5)
        }
        levels = AudioBands(bass: levels.bass*0.8 + min(1, sqrt(power[0]))*0.2,
                            mid: levels.mid*0.8 + min(1, sqrt(power[1]))*0.2,
                            high: levels.high*0.8 + min(1, sqrt(power[2]))*0.2)
        return levels
    }
}
