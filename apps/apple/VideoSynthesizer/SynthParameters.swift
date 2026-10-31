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
    var audioToOsc: Float = 0.5
    var audioToFractal: Float = 0.5
    var audioToCA: Float = 0.2

    init() {}
    private enum CodingKeys: String, CodingKey {
        case schemaVersion, oscFreq, oscSpeed, fractalZoom, fractalDetail, fractalMorph
        case caFeed, caKill, feedbackMix, colorShift, audioToOsc, audioToFractal, audioToCA
    }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = try c.decode(Int.self, forKey: .schemaVersion)
        oscFreq = try c.decode(Float.self, forKey: .oscFreq)
        oscSpeed = try c.decode(Float.self, forKey: .oscSpeed)
        fractalZoom = try c.decode(Float.self, forKey: .fractalZoom)
        fractalDetail = try c.decode(Float.self, forKey: .fractalDetail)
        fractalMorph = try c.decode(Float.self, forKey: .fractalMorph)
        caFeed = try c.decode(Float.self, forKey: .caFeed)
        caKill = try c.decode(Float.self, forKey: .caKill)
        feedbackMix = try c.decode(Float.self, forKey: .feedbackMix)
        colorShift = try c.decode(Float.self, forKey: .colorShift)
        audioToOsc = c.contains(.audioToOsc) ? try c.decode(Float.self, forKey: .audioToOsc) : 0.5
        audioToFractal = c.contains(.audioToFractal) ? try c.decode(Float.self, forKey: .audioToFractal) : 0.5
        audioToCA = c.contains(.audioToCA) ? try c.decode(Float.self, forKey: .audioToCA) : 0.2
    }

    func validated() throws -> Self {
        guard schemaVersion == 1 else { throw PresetError.invalid("Unsupported preset version.") }
        let fields: [(Float, ClosedRange<Float>)] = [
            (oscFreq, 0...30), (oscSpeed, 0...5), (fractalZoom, 0.1...4),
            (fractalDetail, 1...30), (fractalMorph, -1...1), (caFeed, 0...0.1),
            (caKill, 0...0.1), (feedbackMix, 0...1), (colorShift, 0...1),
            (audioToOsc, 0...1), (audioToFractal, 0...1), (audioToCA, 0...1)
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
