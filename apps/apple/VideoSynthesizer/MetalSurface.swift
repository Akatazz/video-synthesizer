import MetalKit
import SwiftUI

#if os(macOS)
private final class NativeMetalView: MTKView {
    var onWindowClose: (() -> Void)?
    private var closeObserver: NSObjectProtocol?

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if let closeObserver { NotificationCenter.default.removeObserver(closeObserver) }
        closeObserver = nil
        if let window {
            closeObserver = NotificationCenter.default.addObserver(
                forName:NSWindow.willCloseNotification,object:window,queue:.main
            ) { [weak self] _ in self?.onWindowClose?() }
        }
    }
    deinit { if let closeObserver { NotificationCenter.default.removeObserver(closeObserver) } }
}

struct MetalSurface: NSViewRepresentable {
    var parameters: SynthParameters
    var bands: AudioBands
    var ndi: NdiOutput
    var paused: Bool
    var resetID: Int
    var onStop: () -> Void
    var onError: (String) -> Void
    func makeCoordinator() -> SynthRenderer { SynthRenderer(ndi:ndi,onStop:onStop,onError: onError) }
    func makeNSView(context: Context) -> MTKView { context.coordinator.makeView() }
    func updateNSView(_ view: MTKView, context: Context) {
        context.coordinator.update(parameters, bands: bands, paused: paused, resetID: resetID)
    }
    static func dismantleNSView(_ view: MTKView, coordinator: SynthRenderer) {
        coordinator.stop()
        view.isPaused = true
        view.delegate = nil
    }
}
#else
struct MetalSurface: UIViewRepresentable {
    var parameters: SynthParameters
    var bands: AudioBands
    var ndi: NdiOutput
    var paused: Bool
    var resetID: Int
    var onStop: () -> Void
    var onError: (String) -> Void
    func makeCoordinator() -> SynthRenderer { SynthRenderer(ndi:ndi,onStop:onStop,onError: onError) }
    func makeUIView(context: Context) -> MTKView { context.coordinator.makeView() }
    func updateUIView(_ view: MTKView, context: Context) {
        context.coordinator.update(parameters, bands: bands, paused: paused, resetID: resetID)
    }
    static func dismantleUIView(_ view: MTKView, coordinator: SynthRenderer) {
        coordinator.stop()
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
    private let ndi: NdiOutput
    private let libraryURL: URL?
    private var renderSize = CGSize(width:640,height:360)
    private var backgroundTimer: DispatchSourceTimer?
    private var stopped = false
    private let requestGate = NSLock()
    private var mainDrawPending = false
    private let onStop: () -> Void

    init(ndi:NdiOutput,libraryURL:URL? = nil,onStop:@escaping () -> Void = {},onError: @escaping (String) -> Void) {
        self.ndi = ndi; self.libraryURL = libraryURL; self.onStop = onStop; self.onError = onError
    }
    deinit { backgroundTimer?.cancel() }

    func stop() {
        guard !stopped else { return }
        stopped = true
        backgroundTimer?.setEventHandler {}
        backgroundTimer?.cancel()
        backgroundTimer = nil
        ndi.stop()
        onStop()
    }

    func makeView() -> MTKView {
        #if os(macOS)
        let view = NativeMetalView(frame:.zero,device:MTLCreateSystemDefaultDevice())
        view.onWindowClose = { [weak self] in self?.stop() }
        #else
        let view = MTKView(frame: .zero, device: MTLCreateSystemDefaultDevice())
        #endif
        view.colorPixelFormat = .bgra8Unorm
        view.preferredFramesPerSecond = 60
        view.delegate = self
        guard let device = view.device else { fail("Metal is unavailable on this device."); return view }
        do {
            let loadedLibrary: MTLLibrary?
            if let libraryURL { loadedLibrary = try device.makeLibrary(URL:libraryURL) }
            else { loadedLibrary = device.makeDefaultLibrary() }
            guard let library = loadedLibrary,
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
        #if os(macOS)
        if !reportedError {
            let timer = DispatchSource.makeTimerSource(queue:.main)
            timer.schedule(deadline:.now(),repeating:.nanoseconds(16_666_667),leeway:.milliseconds(1))
            timer.setEventHandler { [weak self, weak view] in
                guard let self, let view, !self.stopped, self.ndi.active, !self.canPresent(view) else { return }
                self.render(in:view,present:false)
            }
            backgroundTimer = timer
            timer.resume()
        }
        #endif
        return view
    }

    func update(_ parameters: SynthParameters, bands: AudioBands, paused: Bool, resetID: Int) {
        self.parameters = parameters
        self.bands = bands
        self.paused = paused
        if self.resetID != resetID { clearPending = true; self.resetID = resetID; time = 0 }
    }

    func mtkView(_ view: MTKView, drawableSizeWillChange size: CGSize) {
        // Keep the last nonzero output size while a window is hidden/minimized.
        // Texture resizing below clears history only when the dimensions change.
        if Thread.isMainThread {
            if size.width > 0 && size.height > 0 { renderSize = size }
        } else {
            DispatchQueue.main.async { [weak self] in
                if size.width > 0 && size.height > 0 { self?.renderSize = size }
            }
        }
    }

    func draw(in view: MTKView) {
        if Thread.isMainThread { drawVisible(in:view); return }
        // Serialize view access, parameter updates and encoding on the main
        // queue, without accumulating display-link callbacks during a stall.
        requestGate.lock()
        if mainDrawPending { requestGate.unlock(); return }
        mainDrawPending = true
        requestGate.unlock()
        DispatchQueue.main.async { [weak self, weak view] in
            guard let self else { return }
            self.requestGate.lock(); self.mainDrawPending = false; self.requestGate.unlock()
            if let view { self.drawVisible(in:view) }
        }
    }

    private func canPresent(_ view: MTKView) -> Bool {
        #if os(macOS)
        guard !view.isHidden, let window = view.window else { return false }
        return window.isVisible && !window.isMiniaturized && window.occlusionState.contains(.visible)
        #else
        return !view.isHidden && view.window != nil
        #endif
    }

    private func drawVisible(in view: MTKView) {
        guard !stopped, canPresent(view) else { return }
        render(in:view,present:true)
    }

    private func render(in view: MTKView, present: Bool) {
        let now = CACurrentMediaTime()
        guard !stopped, !reportedError, let device = view.device, let queue, let compute, let display else { return }
        let drawable = present ? view.currentDrawable : nil
        let pass = drawable != nil ? view.currentRenderPassDescriptor : nil
        guard (drawable != nil && pass != nil) || ndi.active else { return }
        guard let command = queue.makeCommandBuffer() else { fail("Could not create a Metal command buffer."); return }
        let delta = Float(min(now - previousTime, 0.1))
        previousTime = now
        if view.drawableSize.width > 0 && view.drawableSize.height > 0 { renderSize = view.drawableSize }

        // Cap the internal preview to 1280x720 while keeping the drawable's aspect ratio.
        let scale = min(1, min(1280 / renderSize.width, 720 / renderSize.height))
        let width = max(1, Int(renderSize.width * scale))
        let height = max(1, Int(renderSize.height * scale))
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
        if let pass, let drawable {
            guard let encoder = command.makeRenderCommandEncoder(descriptor:pass) else { fail("Could not present preview."); return }
            encoder.setRenderPipelineState(display)
            encoder.setFragmentTexture(history[readIndex],index:0)
            encoder.drawPrimitives(type:.triangle,vertexStart:0,vertexCount:3)
            encoder.endEncoding()
            command.present(drawable)
        }
        if let ticket = ndi.reserveFrame(time:now) {
            encodeNdiCapture(command,device:device,display:display,width:width,height:height,ticket:ticket)
        }
        command.addCompletedHandler { [weak self] buffer in
            if let error = buffer.error { DispatchQueue.main.async { self?.fail(error.localizedDescription) } }
        }
        command.commit()
    }

    private func fail(_ message: String) {
        guard !reportedError else { return }
        reportedError = true
        stop()
        DispatchQueue.main.async { self.onError(message) }
    }

    private func encodeNdiCapture(_ command: MTLCommandBuffer, device: MTLDevice, display: MTLRenderPipelineState,
                                  width: Int, height: Int, ticket: NdiFrameTicket) {
        let stride = (width*4+255) & ~255
        let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat:.bgra8Unorm,width:width,height:height,mipmapped:false)
        descriptor.usage = [.renderTarget]
        descriptor.storageMode = .private
        guard let texture = device.makeTexture(descriptor:descriptor),
              let buffer = device.makeBuffer(length:stride*height,options:.storageModeShared) else {
            ndi.cancel(ticket,message:"Could not allocate the NDI readback buffer."); return
        }
        let pass = MTLRenderPassDescriptor()
        pass.colorAttachments[0].texture = texture
        pass.colorAttachments[0].loadAction = .dontCare
        pass.colorAttachments[0].storeAction = .store
        guard let encoder = command.makeRenderCommandEncoder(descriptor:pass) else { ndi.cancel(ticket,message:"Could not render NDI output."); return }
        encoder.setRenderPipelineState(display)
        encoder.setFragmentTexture(history[readIndex],index:0)
        encoder.drawPrimitives(type:.triangle,vertexStart:0,vertexCount:3)
        encoder.endEncoding()
        guard let blit = command.makeBlitCommandEncoder() else { ndi.cancel(ticket,message:"Could not read NDI output."); return }
        blit.copy(from:texture,sourceSlice:0,sourceLevel:0,sourceOrigin:MTLOrigin(x:0,y:0,z:0),
                  sourceSize:MTLSize(width:width,height:height,depth:1),to:buffer,destinationOffset:0,
                  destinationBytesPerRow:stride,destinationBytesPerImage:stride*height)
        blit.endEncoding()
        command.addCompletedHandler { [ndi] completed in
            if let error = completed.error { ndi.cancel(ticket,message:error.localizedDescription) }
            else { ndi.submit(buffer,width:width,height:height,stride:stride,ticket:ticket) }
        }
    }
}
