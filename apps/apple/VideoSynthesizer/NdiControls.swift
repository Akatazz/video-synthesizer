#if os(macOS)
import AppKit
import SwiftUI

struct NdiControls: View {
    @ObservedObject var output: NdiOutput
    private let sizes = [(128,128),(256,256),(512,512),(512,64),(1024,128),(1280,720),(1920,1080)]
    var body: some View {
        VStack(alignment:.leading,spacing:12) {
            Text("NDI OUTPUT").font(.headline)
            Link("NDI® technology · ndi.video",destination:URL(string:"https://ndi.video")!)
            TextField("NDI source name",text:$output.sourceName).disabled(output.active)
            TextField("NDI runtime path (optional)",text:$output.runtimePath).disabled(output.active)
            Button("NDI runtime…") {
                let panel = NSOpenPanel()
                panel.title = "Choose the installed NDI runtime library"
                panel.canChooseDirectories = false
                panel.allowsMultipleSelection = false
                if panel.runModal() == .OK, let url = panel.url { output.runtimePath = url.path }
            }.disabled(output.active)
            Picker("Resolution",selection:Binding(
                get: { sizes.firstIndex { $0.0 == output.settings.width && $0.1 == output.settings.height } ?? 2 },
                set: { output.settings.width = sizes[$0].0; output.settings.height = sizes[$0].1 }
            )) {
                ForEach(sizes.indices,id:\.self) { index in Text("\(sizes[index].0) × \(sizes[index].1)").tag(index) }
            }.disabled(output.active)
            Picker("Frame rate",selection:$output.settings.fps) {
                ForEach([15,25,30],id:\.self) { fps in Text("\(fps) fps").tag(fps) }
            }.disabled(output.active)
            Text("Black cutoff \(output.settings.cutoff,specifier:"%.3f")").font(.caption)
            Slider(value:$output.settings.cutoff,in:0...0.25).accessibilityLabel("NDI black cutoff")
            Text("Gamma \(output.settings.gamma,specifier:"%.3f")").font(.caption)
            Slider(value:$output.settings.gamma,in:0.5...2.5).accessibilityLabel("NDI gamma")
            Picker("Channels",selection:$output.settings.channel) {
                Text("RGB").tag(0); Text("Luma").tag(1); Text("Red").tag(2); Text("Green").tag(3); Text("Blue").tag(4)
            }
            Picker("Alpha",selection:$output.settings.alpha) {
                Text("Opaque").tag(0); Text("Luma alpha").tag(1); Text("Inverted luma alpha").tag(2)
            }
            Button(output.active ? "Stop NDI output" : "Start NDI output") { if output.active { output.stop() } else { output.start() } }
            Text(output.status).font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
        }.padding(.top,16)
    }
}
#endif
