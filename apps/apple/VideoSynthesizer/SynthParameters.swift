import Foundation

struct SynthParameters: Codable, Equatable {
    var schemaVersion = 1
    var oscFreq: Float = 2
    var oscSpeed: Float = 0.5
    var fractalZoom: Float = 1.5
    var fractalDetail: Float = 10
    var fractalMorph: Float = 0.2
    var caFeed: Float = 0.045
    var caKill: Float = 0.06
    var feedbackMix: Float = 0.95
    var colorShift: Float = 0

    func validated() throws -> Self {
        guard schemaVersion == 1 else { throw PresetError.invalid("Unsupported preset version.") }
        let fields: [(Float, ClosedRange<Float>)] = [
            (oscFreq, 0...30), (oscSpeed, 0...5), (fractalZoom, 0.1...4),
            (fractalDetail, 1...30), (fractalMorph, -1...1), (caFeed, 0...0.1),
            (caKill, 0...0.1), (feedbackMix, 0...1), (colorShift, 0...1)
        ]
        guard fields.allSatisfy({ $0.0.isFinite && $0.1.contains($0.0) }) else {
            throw PresetError.invalid("Preset contains values outside the supported ranges.")
        }
        return self
    }

    enum PresetError: LocalizedError {
        case invalid(String)
        var errorDescription: String? {
            switch self { case .invalid(let message): return message }
        }
    }
}
