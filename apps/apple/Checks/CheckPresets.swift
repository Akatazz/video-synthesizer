import Foundation

private struct LightingFixture: Decodable {
    var name: String
    var sourceWidth, sourceHeight, width, height, channel, alpha: Int
    var cutoff, gamma: Float
    var sourceBGRA, expectedBGRA: [UInt8]
}

@main
struct CheckPresets {
    static func main() throws {
        let path = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "contracts/presets/classic-feedback.json"
        let data = try Data(contentsOf: URL(fileURLWithPath: path))
        let decoder = JSONDecoder()
        let preset = try decoder.decode(SynthParameters.self, from: data).validated()
        precondition(preset == SynthParameters(), "Canonical preset differs from defaults")
        let roundTrip = try decoder.decode(SynthParameters.self, from: JSONEncoder().encode(preset)).validated()
        precondition(roundTrip == preset, "Preset round trip changed values")
        var routed = preset
        routed.audioToOsc = 0.15; routed.audioToFractal = 0.75; routed.audioToCA = 1
        let routeRoundTrip = try decoder.decode(SynthParameters.self, from: JSONEncoder().encode(routed)).validated()
        precondition(routeRoundTrip == routed, "Audio routes must survive preset save/load")
        let object = try JSONSerialization.jsonObject(with: data) as! [String: Any]
        for key in object.keys {
            var missing = object
            missing.removeValue(forKey: key)
            expectRejection(try JSONSerialization.data(withJSONObject: missing))
        }
        for (key, value) in [("schemaVersion", 2.0), ("oscFreq", -1.0), ("fractalDetail", 0.0)] {
            var invalid = object
            invalid[key] = value
            expectRejection(try JSONSerialization.data(withJSONObject: invalid))
        }
        for invalid in ["null", "{}", "{malformed}"] { expectRejection(Data(invalid.utf8)) }
        print("Swift preset contract checks passed.")
        for bin in [2, 10, 64] {
            let analysis = AudioAnalysis()
            var levels = AudioBands()
            for sample in 0..<512*40 {
                levels = analysis.push(0.8 * sin(2 * .pi * Float(bin*sample) / 512), sampleRate: 32000)
            }
            let values = [levels.bass, levels.mid, levels.high]
            let expected = bin == 2 ? 0 : bin == 10 ? 1 : 2
            precondition(values[expected] > 0.75 && values[expected] < 0.85, "FFT tone amplitude or band is wrong")
            for index in 0..<3 where index != expected { precondition(values[index] < 0.01, "FFT leaks into another band") }
        }
        let dc = AudioAnalysis()
        var dcLevels = AudioBands()
        for _ in 0..<1024 { dcLevels = dc.push(0.5, sampleRate: 44100) }
        precondition(dcLevels == AudioBands(), "DC input must not modulate synthesis")
        for _ in 0..<1024 { dcLevels = dc.push(.nan, sampleRate: 48000) }
        precondition(dcLevels == AudioBands(), "Non-finite input must not modulate synthesis")
        print("Swift native FFT checks passed.")
        let image: [UInt8] = [0,0,255,255,0,255,0,255,255,0,0,255,10,10,10,255]
        var lighting = NdiSettings()
        lighting.width = 2; lighting.height = 2; lighting.cutoff = 0
        let identity = try LightingOutput.process(image,width:2,height:2,stride:8,settings:lighting)
        precondition(identity == image,"Lighting identity changed BGRA channels or orientation")
        lighting.cutoff = 0.05
        let cutoff = try LightingOutput.process(image,width:2,height:2,stride:8,settings:lighting)
        precondition(Array(cutoff[12..<16]) == [0,0,0,255],"Black cutoff failed")
        lighting.cutoff = 0; lighting.channel = 2; lighting.alpha = 1
        let red = try LightingOutput.process(image,width:2,height:2,stride:8,settings:lighting)
        precondition(Array(red[0..<4]) == [255,255,255,255] && Array(red[4..<8]) == [0,0,0,0],"Red routing or alpha failed")
        lighting.alpha = 2
        let inverted = try LightingOutput.process(image,width:2,height:2,stride:8,settings:lighting)
        precondition(inverted[3] == 0 && inverted[7] == 255,"Inverted alpha failed")
        let unavailable = NdiOutput()
        unavailable.runtimePath = "/video-synth-missing-runtime-4a137d6f.dylib"
        unavailable.start()
        precondition(!unavailable.active && unavailable.status.hasPrefix("NDI unavailable:"),"Missing runtime must leave output off and an error visible")
        if CommandLine.arguments.count > 2 {
            var error = [CChar](repeating:0,count:512)
            let success = CommandLine.arguments[2].withCString { vs_ndi_test_roundtrip($0,&error,512) }
            precondition(success == 1,String(cString:error))
            print("Real macOS NDI sender/receiver round trip passed.")
        }
        print("Swift lighting checks passed.")
        let lightingPath = URL(fileURLWithPath:path).deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("lighting-fixtures.json")
        let cases = try JSONDecoder().decode([LightingFixture].self,from:Data(contentsOf:lightingPath))
        for fixture in cases {
            var p = NdiSettings()
            p.width = fixture.width; p.height = fixture.height; p.cutoff = fixture.cutoff
            p.gamma = fixture.gamma; p.channel = fixture.channel; p.alpha = fixture.alpha
            let actual = try LightingOutput.process(fixture.sourceBGRA,width:fixture.sourceWidth,height:fixture.sourceHeight,stride:fixture.sourceWidth*4,settings:p)
            precondition(actual.count == fixture.expectedBGRA.count && zip(actual,fixture.expectedBGRA).allSatisfy { abs(Int($0.0)-Int($0.1)) <= 1 },"Lighting fixture failed: \(fixture.name)")
        }
    }

    static func expectRejection(_ data: Data) {
        do {
            _ = try JSONDecoder().decode(SynthParameters.self, from: data).validated()
            preconditionFailure("Invalid preset was accepted")
        } catch { /* Expected: invalid input must not enter live state. */ }
    }
}
