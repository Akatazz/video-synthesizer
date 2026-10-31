import Foundation

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
    }

    static func expectRejection(_ data: Data) {
        do {
            _ = try JSONDecoder().decode(SynthParameters.self, from: data).validated()
            preconditionFailure("Invalid preset was accepted")
        } catch { /* Expected: invalid input must not enter live state. */ }
    }
}
