package com.akatazz.videosynthesizer

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class PresetTest {
    private fun fixture() = javaClass.classLoader!!.getResource("classic-feedback.json")!!.readText()

    @Test fun canonicalRoundTrip() {
        val preset = SynthParameters.fromJson(fixture())
        assertEquals(SynthParameters(), preset)
        assertEquals(preset, SynthParameters.fromJson(preset.toJson()))
    }

    @Test fun rejectsMissingAndInvalidFields() {
        val source = JSONObject(fixture())
        source.keys().asSequence().toList().forEach { key ->
            val missing = JSONObject(fixture()).apply { remove(key) }
            assertThrows(Exception::class.java) { SynthParameters.fromJson(missing.toString()) }
        }
        listOf("schemaVersion" to 2, "oscFreq" to -1, "fractalDetail" to 0).forEach { (key, value) ->
            val invalid = JSONObject(fixture()).put(key, value)
            assertThrows(Exception::class.java) { SynthParameters.fromJson(invalid.toString()) }
        }
        assertThrows(Exception::class.java) { SynthParameters(oscFreq = Float.NaN).validated() }
        assertThrows(Exception::class.java) { SynthParameters.fromJson("{malformed}") }
        assertThrows(Exception::class.java) { SynthParameters.fromJson(fixture().replace('"', '\'')) }
        assertThrows(Exception::class.java) { SynthParameters.fromJson(fixture() + " trailing input") }
    }
}
