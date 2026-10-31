import AVFoundation
import Combine
import Foundation

final class MicrophoneInput: ObservableObject {
    @Published private(set) var active = false
    @Published private(set) var requesting = false
    @Published private(set) var status = "Microphone off"
    @Published private(set) var levels = AudioBands()
    private var engine: AVAudioEngine?
    private var generation = 0
    private var foreground = true
    private var pendingStart = false
    private var observers: [NSObjectProtocol] = []

    init() {
        observers.append(NotificationCenter.default.addObserver(
            forName: .AVAudioEngineConfigurationChange, object: nil, queue: .main
        ) { [weak self] notification in
            guard let self, let current = self.engine, notification.object as? AVAudioEngine === current else { return }
            self.stop()
            self.status = "Audio device changed. Enable the microphone to reconnect."
        })
        #if os(iOS)
        observers.append(NotificationCenter.default.addObserver(
            forName: AVAudioSession.interruptionNotification, object: nil, queue: .main
        ) { [weak self] _ in self?.stop() })
        #endif
    }

    deinit {
        for observer in observers { NotificationCenter.default.removeObserver(observer) }
        engine?.inputNode.removeTap(onBus: 0)
        engine?.stop()
    }

    func toggle() {
        if active || requesting { stop(); return }
        generation += 1
        let request = generation
        requesting = true
        status = "Requesting microphone access…"
        AVCaptureDevice.requestAccess(for: .audio) { [weak self] granted in
            DispatchQueue.main.async {
                guard let self, self.generation == request else { return }
                self.requesting = false
                guard granted else { self.status = "Microphone access denied. Enable it in system settings to use audio."; return }
                guard self.foreground else {
                    self.pendingStart = true
                    self.requesting = true
                    self.status = "Microphone access granted. Waiting for the app to become active."
                    return
                }
                do { try self.start(request: request) }
                catch { self.stop(); self.status = "Microphone unavailable: \(error.localizedDescription)" }
            }
        }
    }

    func updateScene(active: Bool, background: Bool) {
        foreground = active
        if background || (!active && self.active) { stop(); return }
        // A system permission prompt can make the scene inactive. Continue the
        // explicit request once active, while a real background transition cancels it.
        if active && pendingStart {
            pendingStart = false
            requesting = false
            do { try start(request: generation) }
            catch { stop(); status = "Microphone unavailable: \(error.localizedDescription)" }
        }
    }

    private func start(request: Int) throws {
        #if os(iOS)
        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.record, mode: .measurement)
        try session.setActive(true)
        #endif
        let capture = AVAudioEngine()
        let input = capture.inputNode
        let format = input.outputFormat(forBus: 0)
        guard format.sampleRate >= 8000, format.sampleRate <= 192000, format.channelCount > 0 else {
            throw SynthParameters.PresetError.invalid("No supported audio input is available.")
        }
        let analysis = AudioAnalysis()
        // Retain this engine before installing its tap so failure always removes it.
        engine = capture
        input.installTap(onBus: 0, bufferSize: 1024, format: format) { [weak self] buffer, _ in
            guard let channels = buffer.floatChannelData, buffer.frameLength > 0 else { return }
            var latest = AudioBands()
            for frame in 0..<Int(buffer.frameLength) {
                var mono: Float = 0
                for channel in 0..<Int(buffer.format.channelCount) { mono += channels[channel][frame] }
                latest = analysis.push(mono / Float(buffer.format.channelCount), sampleRate: buffer.format.sampleRate)
            }
            let snapshot = latest
            DispatchQueue.main.async {
                guard let self, self.generation == request, self.active else { return }
                self.levels = snapshot
            }
        }
        capture.prepare()
        try capture.start()
        active = true
        status = "Microphone live"
    }

    func stop() {
        generation += 1 // Invalidate permission responses and queued audio callbacks.
        pendingStart = false
        if let capture = engine {
            capture.inputNode.removeTap(onBus: 0)
            capture.stop()
        }
        engine = nil
        active = false
        requesting = false
        levels = AudioBands()
        status = "Microphone off"
        #if os(iOS)
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
        #endif
    }
}
