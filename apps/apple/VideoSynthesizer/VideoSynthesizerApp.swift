import SwiftUI
import UniformTypeIdentifiers

@main
struct VideoSynthesizerApp: App {
    var body: some Scene {
        WindowGroup { SynthView() }
    }
}

struct PresetDocument: FileDocument {
    static var readableContentTypes: [UTType] { [.json] }
    var parameters: SynthParameters

    init(parameters: SynthParameters) { self.parameters = parameters }

    init(configuration: ReadConfiguration) throws {
        guard let data = configuration.file.regularFileContents else {
            throw SynthParameters.PresetError.invalid("The preset is empty.")
        }
        parameters = try JSONDecoder().decode(SynthParameters.self, from: data).validated()
    }

    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return FileWrapper(regularFileWithContents: try encoder.encode(parameters.validated()))
    }
}

struct SynthView: View {
    @Environment(\.scenePhase) private var scenePhase
    @StateObject private var audio = MicrophoneInput()
    @StateObject private var ndi = NdiOutput()
    @State private var parameters = SynthParameters()
    @State private var paused = false
    @State private var resetID = 0
    @State private var importing = false
    @State private var exporting = false
    @State private var errorMessage: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("LZX-V").font(.largeTitle.bold()).foregroundStyle(.mint)
            Text("MODULAR VIDEO SYNTHESIZER").font(.caption)
            HStack {
                Button(paused ? "Resume" : "Pause") { paused.toggle() }
                Button("Reset feedback") { resetID += 1 }
                Button(audio.active || audio.requesting ? "Disable microphone" : "Enable microphone") { audio.toggle() }
                Menu("Presets") {
                    Button("Open preset…") { importing = true }
                    Button("Save preset…") { exporting = true }
                }
            }
            #if os(macOS)
            HStack(alignment: .top, spacing: 24) {
                surface
                controls.frame(width: 280)
            }
            #else
            surface.frame(minHeight: 200, idealHeight: 300, maxHeight: 400)
            controls
            #endif
            Text(paused ? "Paused" : "Live · Metal preview")
                .font(.caption).foregroundStyle(.secondary)
            Text(audio.status).font(.caption).foregroundStyle(.secondary)
            if audio.active {
                Text("Bass \(audio.levels.bass, specifier: "%.2f") · Mid \(audio.levels.mid, specifier: "%.2f") · High \(audio.levels.high, specifier: "%.2f")")
                    .font(.caption.monospacedDigit())
            }
        }
        .padding(24)
        .background(Color(red: 0.06, green: 0.08, blue: 0.10))
        .preferredColorScheme(.dark)
        .onChange(of: scenePhase) { phase in audio.updateScene(active: phase == .active, background: phase == .background) }
        .onDisappear { audio.stop(); ndi.stop() }
        #if os(macOS)
        .frame(minWidth: 860, minHeight: 580)
        #endif
        .fileImporter(isPresented: $importing, allowedContentTypes: [.json]) { result in
            do {
                let url = try result.get()
                let access = url.startAccessingSecurityScopedResource()
                defer { if access { url.stopAccessingSecurityScopedResource() } }
                parameters = try JSONDecoder().decode(SynthParameters.self, from: Data(contentsOf: url)).validated()
                resetID += 1
            } catch { errorMessage = error.localizedDescription }
        }
        .fileExporter(isPresented: $exporting, document: PresetDocument(parameters: parameters),
                      contentType: .json, defaultFilename: "synth-preset") { result in
            if case .failure(let error) = result { errorMessage = error.localizedDescription }
        }
        .alert("Video Synthesizer", isPresented: Binding(
            get: { errorMessage != nil }, set: { if !$0 { errorMessage = nil } }
        )) { Button("OK") { errorMessage = nil } } message: { Text(errorMessage ?? "") }
    }

    private var surface: some View {
        MetalSurface(parameters: parameters, bands: audio.levels, ndi: ndi, paused: paused, resetID: resetID) { errorMessage = $0 }
            .aspectRatio(16.0 / 9.0, contentMode: .fit)
            .background(.black).clipShape(RoundedRectangle(cornerRadius: 8))
            .accessibilityLabel("Live synthesis preview")
    }

    private var controls: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                control("Oscillator frequency", \.oscFreq, 0...30)
                control("Oscillator speed", \.oscSpeed, 0...5)
                control("Fractal zoom", \.fractalZoom, 0.1...4)
                control("Fractal detail", \.fractalDetail, 1...30)
                control("Fractal morph", \.fractalMorph, -1...1)
                control("Cell feed", \.caFeed, 0...0.1)
                control("Cell kill", \.caKill, 0...0.1)
                control("Feedback mix", \.feedbackMix, 0...1)
                control("Color shift", \.colorShift, 0...1)
                control("Mid → oscillator", \.audioToOsc, 0...1)
                control("Bass → fractal", \.audioToFractal, 0...1)
                control("High → cell feed", \.audioToCA, 0...1)
                #if os(macOS)
                NdiControls(output:ndi)
                #endif
            }.padding(.trailing, 8)
        }
    }

    private func control(_ name: String, _ key: WritableKeyPath<SynthParameters, Float>,
                         _ range: ClosedRange<Double>) -> some View {
        VStack(alignment: .leading) {
            HStack {
                Text(name)
                Spacer()
                Text(parameters[keyPath: key], format: .number.precision(.fractionLength(3)))
                    .monospacedDigit().foregroundStyle(.secondary)
            }.font(.caption)
            Slider(value: Binding(get: { Double(parameters[keyPath: key]) },
                                  set: { parameters[keyPath: key] = Float($0) }), in: range)
                .accessibilityLabel(name)
        }
    }
}
