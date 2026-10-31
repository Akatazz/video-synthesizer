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
        let app = NSApplication.shared
        app.setActivationPolicy(.regular)
        app.finishLaunching()
        let output = NdiOutput()
        output.runtimePath = CommandLine.arguments[2]
        output.sourceName = "LZX-V Background Metal Check"
        output.settings.width = 128; output.settings.height = 128
        output.start()
        require(output.active,output.status)
        var renderError: String?
        var stopEvents = 0
        let renderer = SynthRenderer(ndi:output,libraryURL:URL(fileURLWithPath:CommandLine.arguments[1]),
                                     onStop:{ stopEvents += 1 }) { renderError = $0 }
        renderer.update(SynthParameters(),bands:AudioBands(),paused:false,resetID:0)
        let view = renderer.makeView()
        view.drawableSize = CGSize(width:64,height:36)
        defer { renderer.stop(); view.delegate = nil; view.isPaused = true }
        require(view.window == nil,"The check must run without a window or drawable.")
        withExtendedLifetime((renderer,view)) {
            var fingerprints = Set<UInt64>()
            let deadline = Date().addingTimeInterval(10)
            while output.framesSent < 4 && Date() < deadline && renderError == nil && output.active {
                pumpEvents(for:0.02)
                if output.framesSent > 0 { fingerprints.insert(output.lastFrameChecksum) }
            }
            require(renderError == nil,renderError ?? "")
            require(output.framesSent >= 4,"Background Metal output did not send frames: \(output.status)")
            require(fingerprints.count >= 2,"GPU readback did not contain changing synthesis pixels.")
            renderer.mtkView(view,drawableSizeWillChange:CGSize(width:CGFloat.infinity,height:36))
            renderer.mtkView(view,drawableSizeWillChange:CGSize(width:64,height:CGFloat.nan))
            renderer.mtkView(view,drawableSizeWillChange:.zero)
            waitForFrames(output,additional:2,context:"invalid/zero drawable-size transitions")
            renderer.update(SynthParameters(),bands:AudioBands(),paused:true,resetID:0)
            // Drain an already queued live frame before checking held output.
            let drain = output.framesSent+2
            while output.framesSent < drain && Date() < deadline && output.active {
                pumpEvents(for:0.02)
            }
            let held = output.lastFrameChecksum
            let pausedTarget = output.framesSent+2
            while output.framesSent < pausedTarget && Date() < deadline && output.active {
                pumpEvents(for:0.02)
                require(output.lastFrameChecksum == held,"Paused background output changed its pixels.")
            }
            require(output.framesSent >= pausedTarget,"Pausing synthesis stopped background NDI output.")
            report("Background Metal synthesis/readback/NDI checks passed without a drawable, including paused output.")

            renderer.update(SynthParameters(),bands:AudioBands(),paused:false,resetID:0)
            let window = NSWindow(contentRect:NSRect(x:20,y:20,width:320,height:180),
                                  styleMask:[.titled,.miniaturizable],backing:.buffered,defer:false)
            window.isReleasedWhenClosed = false
            view.frame = NSRect(x:0,y:0,width:320,height:180)
            window.contentView = view
            defer { window.close() }
            report("Checking visible window output.")
            window.orderFront(nil)
            waitForFrames(output,additional:3,context:"visible window")
            window.miniaturize(nil)
            report("Checking window minimization.")
            let minimizeDeadline = Date().addingTimeInterval(5)
            while !window.isMiniaturized && Date() < minimizeDeadline {
                pumpEvents(for:0.02)
            }
            require(window.isMiniaturized,"The test window did not minimize.")
            waitForFrames(output,additional:3,context:"minimized window")
            report("Checking restored window output.")
            window.deminiaturize(nil)
            window.orderFront(nil)
            waitForFrames(output,additional:3,context:"restored window")
            window.close()
            let stoppedCount = output.framesSent
            pumpEvents(for:0.15)
            require(!output.active && output.framesSent == stoppedCount,"Renderer teardown left output running.")
            require(stopEvents == 1,"Window close did not release the renderer/input lifecycle exactly once.")
            report("Metal NDI output continued through window minimize/restore and stopped on window close.")
        }
    }

    private static func waitForFrames(_ output: NdiOutput, additional: Int, context: String) {
        let target = output.framesSent+additional
        let deadline = Date().addingTimeInterval(5)
        while output.framesSent < target && output.active && Date() < deadline {
            pumpEvents(for:0.02)
        }
        require(output.framesSent >= target,"NDI output stopped during \(context): \(output.status)")
    }

    private static func pumpEvents(for seconds: TimeInterval) {
        let deadline = Date().addingTimeInterval(seconds)
        repeat {
            // A bare CF run loop does not dispatch WindowServer events. Exercise
            // AppKit's event path too, so occlusion and miniaturization update.
            if let event = NSApp.nextEvent(matching:.any,until:Date(),inMode:.default,dequeue:true) {
                NSApp.sendEvent(event)
            }
            NSApp.updateWindows()
            RunLoop.current.run(until:Date().addingTimeInterval(0.002))
        } while Date() < deadline
    }

    private static func report(_ message: String) {
        FileHandle.standardError.write(Data((message+"\n").utf8))
    }

    private static func require(_ condition: @autoclosure () -> Bool, _ message: @autoclosure () -> String) {
        guard condition() else { report("FAIL: "+message()); exit(1) }
    }
}
