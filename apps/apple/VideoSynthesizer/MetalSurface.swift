import MetalKit
import SwiftUI

#if os(macOS)
struct MetalSurface: NSViewRepresentable {
    var parameters: SynthParameters
    var bands: AudioBands
    var paused: Bool
    var resetID: Int
    var onError: (String) -> Void
    func makeCoordinator() -> SynthRenderer { SynthRenderer(onError: onError) }
    func makeNSView(context: Context) -> MTKView { context.coordinator.makeView() }
    func updateNSView(_ view: MTKView, context: Context) {
        context.coordinator.update(parameters, bands: bands, paused: paused, resetID: resetID)
    }
    static func dismantleNSView(_ view: MTKView, coordinator: SynthRenderer) {
        view.isPaused = true
        view.delegate = nil
    }
}
#else
struct MetalSurface: UIViewRepresentable {
    var parameters: SynthParameters
    var bands: AudioBands
    var paused: Bool
    var resetID: Int
    var onError: (String) -> Void
    func makeCoordinator() -> SynthRenderer { SynthRenderer(onError: onError) }
    func makeUIView(context: Context) -> MTKView { context.coordinator.makeView() }
    func updateUIView(_ view: MTKView, context: Context) {
        context.coordinator.update(parameters, bands: bands, paused: paused, resetID: resetID)
    }
    static func dismantleUIView(_ view: MTKView, coordinator: SynthRenderer) {
        view.isPaused = true
        view.delegate = nil
    }
}
#endif

final class SynthRenderer: NSObject, MTKViewDelegate {
    // Eighteen scalar floats, in the same order as Uniforms in Synthesis.metal.
    private struct Uniforms {
        var time, width, height, oscFreq, oscSpeed, zoom, detail, morph: Float
        var feed, kill, feedback, colorShift: Float
        var audioToOsc, audioToFractal, audioToCA, bass, mid, high: Float
    }
    private var parameters = SynthParameters()
    private var bands = AudioBands()
    private var paused = false
    private var resetID = -1
    private var clearPending = true
    private var time: Float = 0
    private var previousTime = CACurrentMediaTime()
    private var queue: MTLCommandQueue?
    private var compute: MTLComputePipelineState?
    private var display: MTLRenderPipelineState?
    private var history: [MTLTexture] = []
    private var readIndex = 0
    private let onError: (String) -> Void
    private var reportedError = false

    init(onError: @escaping (String) -> Void) { self.onError = onError }

    func makeView() -> MTKView {
        let view = MTKView(frame: .zero, device: MTLCreateSystemDefaultDevice())
        view.colorPixelFormat = .bgra8Unorm
        view.preferredFramesPerSecond = 60
        view.delegate = self
        guard let device = view.device else { fail("Metal is unavailable on this device."); return view }
        do {
            guard let library = device.makeDefaultLibrary(),
                  let synth = library.makeFunction(name: "synthesize"),
                  let vertex = library.makeFunction(name: "screenVertex"),
                  let fragment = library.makeFunction(name: "screenFragment"),
                  let commandQueue = device.makeCommandQueue() else {
                throw SynthParameters.PresetError.invalid("Could not load the Metal shader library.")
            }
            queue = commandQueue
            compute = try device.makeComputePipelineState(function: synth)
            let descriptor = MTLRenderPipelineDescriptor()
            descriptor.vertexFunction = vertex
            descriptor.fragmentFunction = fragment
            descriptor.colorAttachments[0].pixelFormat = view.colorPixelFormat
            display = try device.makeRenderPipelineState(descriptor: descriptor)
        } catch { fail(error.localizedDescription) }
        return view
    }

    func update(_ parameters: SynthParameters, bands: AudioBands, paused: Bool, resetID: Int) {
        self.parameters = parameters
        self.bands = bands
        self.paused = paused
        if self.resetID != resetID { clearPending = true; self.resetID = resetID; time = 0 }
    }

    func mtkView(_ view: MTKView, drawableSizeWillChange size: CGSize) {
        // Allocate lazily in draw; the serial command queue preserves buffer ordering.
        clearPending = true
    }

    func draw(in view: MTKView) {
        let now = CACurrentMediaTime()
        let delta = Float(min(now - previousTime, 0.1))
        previousTime = now
        guard !reportedError, let device = view.device, let queue, let compute, let display,
              view.drawableSize.width > 0, view.drawableSize.height > 0,
              let drawable = view.currentDrawable, let pass = view.currentRenderPassDescriptor,
              let command = queue.makeCommandBuffer() else { return }

        // Cap the internal preview to 1280x720 while keeping the drawable's aspect ratio.
        let scale = min(1, min(1280 / view.drawableSize.width, 720 / view.drawableSize.height))
        let width = max(1, Int(view.drawableSize.width * scale))
        let height = max(1, Int(view.drawableSize.height * scale))
        if history.first?.width != width || history.first?.height != height {
            let descriptor = MTLTextureDescriptor.texture2DDescriptor(
                pixelFormat: .rgba16Float, width: width, height: height, mipmapped: false)
            descriptor.usage = [.shaderRead, .shaderWrite, .renderTarget]
            descriptor.storageMode = .private
            guard let a = device.makeTexture(descriptor: descriptor),
                  let b = device.makeTexture(descriptor: descriptor) else {
                fail("Could not allocate feedback textures."); return
            }
            history = [a, b]
            readIndex = 0
            clearPending = true
        }
        let reset = clearPending
        if reset {
            for texture in history {
                let clear = MTLRenderPassDescriptor()
                clear.colorAttachments[0].texture = texture
                clear.colorAttachments[0].loadAction = .clear
                clear.colorAttachments[0].storeAction = .store
                clear.colorAttachments[0].clearColor = MTLClearColorMake(0, 0, 0, 1)
                guard let encoder = command.makeRenderCommandEncoder(descriptor: clear) else { fail("Could not clear feedback."); return }
                encoder.endEncoding()
            }
            clearPending = false
        }
        if !paused || reset {
            if !paused && !reset { time += delta }
            let p = parameters
            var uniforms = Uniforms(time: time, width: Float(width), height: Float(height),
                oscFreq: p.oscFreq, oscSpeed: p.oscSpeed, zoom: p.fractalZoom, detail: p.fractalDetail,
                morph: p.fractalMorph, feed: p.caFeed, kill: p.caKill, feedback: p.feedbackMix, colorShift: p.colorShift,
                audioToOsc: p.audioToOsc, audioToFractal: p.audioToFractal, audioToCA: p.audioToCA,
                bass: bands.bass, mid: bands.mid, high: bands.high)
            guard let encoder = command.makeComputeCommandEncoder() else { fail("Could not start synthesis."); return }
            encoder.setComputePipelineState(compute)
            encoder.setTexture(history[readIndex], index: 0)
            encoder.setTexture(history[1 - readIndex], index: 1)
            encoder.setBytes(&uniforms, length: MemoryLayout<Uniforms>.stride, index: 0)
            let groupWidth = compute.threadExecutionWidth
            let group = MTLSize(width: groupWidth, height: min(8, compute.maxTotalThreadsPerThreadgroup / groupWidth), depth: 1)
            encoder.dispatchThreads(MTLSize(width: width, height: height, depth: 1), threadsPerThreadgroup: group)
            encoder.endEncoding()
            readIndex = 1 - readIndex
        }
        guard let encoder = command.makeRenderCommandEncoder(descriptor: pass) else { fail("Could not present preview."); return }
        encoder.setRenderPipelineState(display)
        encoder.setFragmentTexture(history[readIndex], index: 0)
        encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
        encoder.endEncoding()
        command.addCompletedHandler { [weak self] buffer in
            if let error = buffer.error { DispatchQueue.main.async { self?.fail(error.localizedDescription) } }
        }
        command.present(drawable)
        command.commit()
    }

    private func fail(_ message: String) {
        guard !reportedError else { return }
        reportedError = true
        DispatchQueue.main.async { self.onError(message) }
    }
}
