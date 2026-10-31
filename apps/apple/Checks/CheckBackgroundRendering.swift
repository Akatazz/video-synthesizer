import AppKit
import MetalKit

@main
struct CheckBackgroundRendering {
    static func main() throws {
        guard CommandLine.arguments.count == 3 else { fatalError("Provide the compiled Metal library and NDI runtime paths.") }
        guard MTLCreateSystemDefaultDevice() != nil else {
            print("SKIP: No Metal device is available. Background GPU/readback still requires hardware validation.")
            return
        }
        _ = NSApplication.shared
        let output = NdiOutput()
        output.runtimePath = CommandLine.arguments[2]
        output.sourceName = "LZX-V Background Metal Check"
        output.settings.width = 128; output.settings.height = 128
        output.start()
        precondition(output.active,output.status)
        var renderError: String?
        let renderer = SynthRenderer(ndi:output,libraryURL:URL(fileURLWithPath:CommandLine.arguments[1])) { renderError = $0 }
        renderer.update(SynthParameters(),bands:AudioBands(),paused:false,resetID:0)
        let view = renderer.makeView()
        view.drawableSize = CGSize(width:64,height:36)
        defer { renderer.stop(); view.delegate = nil; view.isPaused = true }
        precondition(view.window == nil,"The check must run without a window or drawable.")
        withExtendedLifetime((renderer,view)) {
            var fingerprints = Set<UInt64>()
            let deadline = Date().addingTimeInterval(10)
            while output.framesSent < 4 && Date() < deadline && renderError == nil && output.active {
                RunLoop.current.run(until:Date().addingTimeInterval(0.02))
                if output.framesSent > 0 { fingerprints.insert(output.lastFrameChecksum) }
            }
            precondition(renderError == nil,renderError ?? "")
            precondition(output.framesSent >= 4,"Background Metal output did not send frames: \(output.status)")
            precondition(fingerprints.count >= 2,"GPU readback did not contain changing synthesis pixels.")
            renderer.update(SynthParameters(),bands:AudioBands(),paused:true,resetID:0)
            // Drain an already queued live frame before checking held output.
            let drain = output.framesSent+2
            while output.framesSent < drain && Date() < deadline && output.active {
                RunLoop.current.run(until:Date().addingTimeInterval(0.02))
            }
            let held = output.lastFrameChecksum
            let pausedTarget = output.framesSent+2
            while output.framesSent < pausedTarget && Date() < deadline && output.active {
                RunLoop.current.run(until:Date().addingTimeInterval(0.02))
                precondition(output.lastFrameChecksum == held,"Paused background output changed its pixels.")
            }
            precondition(output.framesSent >= pausedTarget,"Pausing synthesis stopped background NDI output.")
            print("Background Metal synthesis/readback/NDI checks passed without a drawable, including paused output.")

            renderer.update(SynthParameters(),bands:AudioBands(),paused:false,resetID:0)
            let window = NSWindow(contentRect:NSRect(x:20,y:20,width:320,height:180),
                                  styleMask:[.titled,.miniaturizable],backing:.buffered,defer:false)
            window.isReleasedWhenClosed = false
            window.contentView = view
            defer { window.close() }
            window.orderFront(nil)
            waitForFrames(output,additional:3,context:"visible window")
            window.miniaturize(nil)
            let minimizeDeadline = Date().addingTimeInterval(5)
            while !window.isMiniaturized && Date() < minimizeDeadline {
                RunLoop.current.run(until:Date().addingTimeInterval(0.02))
            }
            precondition(window.isMiniaturized,"The test window did not minimize.")
            waitForFrames(output,additional:3,context:"minimized window")
            window.deminiaturize(nil)
            window.orderFront(nil)
            waitForFrames(output,additional:3,context:"restored window")
            renderer.stop()
            let stoppedCount = output.framesSent
            RunLoop.current.run(until:Date().addingTimeInterval(0.15))
            precondition(!output.active && output.framesSent == stoppedCount,"Renderer teardown left output running.")
            print("Metal NDI output continued through window minimize/restore and stopped on teardown.")
        }
    }

    private static func waitForFrames(_ output: NdiOutput, additional: Int, context: String) {
        let target = output.framesSent+additional
        let deadline = Date().addingTimeInterval(5)
        while output.framesSent < target && output.active && Date() < deadline {
            RunLoop.current.run(until:Date().addingTimeInterval(0.02))
        }
        precondition(output.framesSent >= target,"NDI output stopped during \(context): \(output.status)")
    }
}
