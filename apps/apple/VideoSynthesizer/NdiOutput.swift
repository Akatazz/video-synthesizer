import Combine
import Foundation
import Metal

struct NdiSettings {
    var width = 512, height = 512, fps = 30
    var cutoff: Float = 0.02
    var gamma: Float = 1
    var channel = 0, alpha = 0
    func validated() throws -> Self {
        guard width >= 2, width <= 1920, width % 2 == 0, height >= 1, height <= 1080,
              [15,25,30].contains(fps), cutoff.isFinite, (0...0.25).contains(cutoff),
              gamma.isFinite, (0.5...2.5).contains(gamma), (0...4).contains(channel), (0...2).contains(alpha) else {
            throw SynthParameters.PresetError.invalid("Invalid NDI output settings.")
        }
        return self
    }
}

enum LightingOutput {
    static func process(_ source: [UInt8], width: Int, height: Int, stride: Int, settings: NdiSettings) throws -> [UInt8] {
        let p = try settings.validated()
        guard width > 0, height > 0, width <= 4096, height <= 4096, stride >= width*4,
              stride <= 65536, source.count >= stride*height else {
            throw SynthParameters.PresetError.invalid("Invalid NDI source image.")
        }
        var output = [UInt8](repeating: 0, count: p.width*p.height*4)
        func mix(_ a: Float, _ b: Float, _ amount: Float) -> Float { a+(b-a)*amount }
        func encode(_ value: Float) -> UInt8 { UInt8((min(1,max(0,value))*255).rounded()) }
        for y in 0..<p.height {
            let sy = min(Float(height-1),max(0,(Float(y)+0.5)*Float(height)/Float(p.height)-0.5))
            let y0 = Int(sy), y1 = min(y0+1,height-1)
            for x in 0..<p.width {
                let sx = min(Float(width-1),max(0,(Float(x)+0.5)*Float(width)/Float(p.width)-0.5))
                let x0 = Int(sx), x1 = min(x0+1,width-1)
                func read(_ channel: Int) -> Float {
                    let top = mix(Float(source[y0*stride+x0*4+channel]),Float(source[y0*stride+x1*4+channel]),sx-Float(x0))
                    let bottom = mix(Float(source[y1*stride+x0*4+channel]),Float(source[y1*stride+x1*4+channel]),sx-Float(x0))
                    return mix(top,bottom,sy-Float(y0))/255
                }
                var b = read(0), g = read(1), r = read(2)
                if r*0.2126+g*0.7152+b*0.0722 < p.cutoff { r = 0; g = 0; b = 0 }
                r = pow(r,p.gamma); g = pow(g,p.gamma); b = pow(b,p.gamma)
                if p.channel != 0 {
                    let mono = p.channel == 1 ? r*0.2126+g*0.7152+b*0.0722 : p.channel == 2 ? r : p.channel == 3 ? g : b
                    r = mono; g = mono; b = mono
                }
                let luma = r*0.2126+g*0.7152+b*0.0722
                let alpha: Float = p.alpha == 0 ? 1 : p.alpha == 1 ? luma : 1-luma
                let pixel = (y*p.width+x)*4
                output[pixel] = encode(b); output[pixel+1] = encode(g)
                output[pixel+2] = encode(r); output[pixel+3] = encode(alpha)
            }
        }
        return output
    }
}

struct NdiFrameTicket { let generation: Int; let settings: NdiSettings }

final class NdiOutput: ObservableObject {
    @Published private(set) var active = false
    @Published private(set) var status = "NDI off"
    @Published var settings = NdiSettings()
    @Published var sourceName = "LZX-V Synth Output (macOS)"
    @Published var runtimePath = ""
    private let worker = DispatchQueue(label: "video-synth.ndi-output", qos: .userInitiated)
    private let gate = NSLock()
    private var sender: OpaquePointer?
    private var running = false, busy = false
    private var generation = 0
    private var previousCapture: Double = 0

    deinit { if let sender { vs_ndi_close(sender) } }

    func start() {
        stop()
        do {
            _ = try settings.validated()
            guard !sourceName.contains("\0") else { throw SynthParameters.PresetError.invalid("Invalid NDI source name.") }
            var error = [CChar](repeating: 0,count: 512)
            let opened = runtimePath.withCString { path in
                sourceName.withCString { name in vs_ndi_open(path,name,&error,512) }
            }
            guard let opened else { throw SynthParameters.PresetError.invalid(String(cString:error)) }
            worker.sync { sender = opened }
            gate.lock(); running = true; previousCapture = 0; gate.unlock()
            active = true
            status = "NDI sending · waiting for receivers"
        } catch { status = "NDI unavailable: \(error.localizedDescription)" }
    }

    func stop() {
        gate.lock(); running = false; busy = false; generation += 1; gate.unlock()
        worker.sync { if let sender { vs_ndi_close(sender) }; sender = nil }
        active = false
        status = "NDI off"
    }

    func reserveFrame(time: Double) -> NdiFrameTicket? {
        gate.lock(); defer { gate.unlock() }
        guard running, !busy, time-previousCapture >= 1/Double(settings.fps) else { return nil }
        busy = true; previousCapture = time
        return NdiFrameTicket(generation:generation,settings:settings)
    }

    private func isCurrent(_ ticket: NdiFrameTicket) -> Bool {
        gate.lock(); defer { gate.unlock() }
        return running && generation == ticket.generation
    }

    func cancel(_ ticket: NdiFrameTicket, message: String) {
        DispatchQueue.main.async { [weak self] in
            guard let self, self.isCurrent(ticket) else { return }
            self.stop(); self.status = "NDI error: \(message)"
        }
    }

    func submit(_ buffer: MTLBuffer, width: Int, height: Int, stride: Int, ticket: NdiFrameTicket) {
        worker.async { [weak self] in
            guard let self, self.isCurrent(ticket), let sender = self.sender else { return }
            defer {
                self.gate.lock()
                if self.generation == ticket.generation { self.busy = false }
                self.gate.unlock()
            }
            do {
                let source = Array(UnsafeBufferPointer(start:buffer.contents().assumingMemoryBound(to:UInt8.self),count:stride*height))
                let pixels = try LightingOutput.process(source,width:width,height:height,stride:stride,settings:ticket.settings)
                var receivers: Int32 = 0, program: Int32 = 0, preview: Int32 = 0
                pixels.withUnsafeBufferPointer { bytes in
                    vs_ndi_send(sender,bytes.baseAddress,Int32(ticket.settings.width),Int32(ticket.settings.height),
                                Int32(ticket.settings.fps),&receivers,&program,&preview)
                }
                let text = "NDI sending · \(ticket.settings.width) × \(ticket.settings.height) · \(receivers) receiver(s) · \(program != 0 ? "PROGRAM" : preview != 0 ? "PREVIEW" : "No tally")"
                DispatchQueue.main.async { [weak self] in if let self, self.isCurrent(ticket) { self.status = text } }
            } catch { self.cancel(ticket,message:error.localizedDescription) }
        }
    }
}
