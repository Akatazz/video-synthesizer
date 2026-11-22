package com.akatazz.videosynthesizer

import kotlinx.serialization.json.*

data class SynthParameters(
    val oscFreq: Float = 2f,
    val oscSpeed: Float = 0.5f,
    val fractalZoom: Float = 1.5f,
    val fractalDetail: Float = 10f,
    val fractalMorph: Float = 0.2f,
    val caFeed: Float = 0.045f,
    val caKill: Float = 0.06f,
    val feedbackMix: Float = 0.95f,
    val colorShift: Float = 0f,
    val audioToOsc: Float = 0.5f,
    val audioToFractal: Float = 0.5f,
    val audioToCA: Float = 0.2f,
) {
    fun values() = floatArrayOf(oscFreq, oscSpeed, fractalZoom, fractalDetail, fractalMorph, caFeed, caKill, feedbackMix, colorShift, audioToOsc, audioToFractal, audioToCA)

    fun validated(): SynthParameters {
        values().forEachIndexed { index, value ->
            require(value.isFinite() && value >= fields[index].min && value <= fields[index].max) {
                "${fields[index].label} is outside the supported range."
            }
        }
        return this
    }

    fun changed(index: Int, value: Float): SynthParameters {
        val values = values()
        values[index] = value
        return fromValues(values).validated()
    }

    fun toJson(): String {
        validated()
        return buildJsonObject {
            put("schemaVersion", 1)
            values().forEachIndexed { index, value -> put(fields[index].key, value) }
        }.toString()
    }

    companion object {
        data class Field(val key: String, val label: String, val min: Float, val max: Float)
        val fields = listOf(
            Field("oscFreq", "Oscillator frequency", 0f, 30f),
            Field("oscSpeed", "Oscillator speed", 0f, 5f),
            Field("fractalZoom", "Fractal zoom", 0.1f, 4f),
            Field("fractalDetail", "Fractal detail", 1f, 30f),
            Field("fractalMorph", "Fractal morph", -1f, 1f),
            Field("caFeed", "Cell feed", 0f, 0.1f),
            Field("caKill", "Cell kill", 0f, 0.1f),
            Field("feedbackMix", "Feedback mix", 0f, 1f),
            Field("colorShift", "Color shift", 0f, 1f),
            Field("audioToOsc", "Mid → oscillator", 0f, 1f),
            Field("audioToFractal", "Bass → fractal", 0f, 1f),
            Field("audioToCA", "High → cell feed", 0f, 1f),
        )

        private fun fromValues(v: FloatArray) = SynthParameters(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11])

        fun fromJson(text: String): SynthParameters {
            val json = Json.parseToJsonElement(text).jsonObject
            val version = json.getValue("schemaVersion").jsonPrimitive
            require(!version.isString && version.intOrNull == 1) { "Unsupported preset version." }
            val values = fields.map {
                if (it.key !in json && it.key == "audioToOsc") return@map 0.5f
                if (it.key !in json && it.key == "audioToFractal") return@map 0.5f
                if (it.key !in json && it.key == "audioToCA") return@map 0.2f
                val value = json.getValue(it.key).jsonPrimitive
                require(!value.isString) { "${it.label} must be a number." }
                requireNotNull(value.floatOrNull) { "${it.label} must be a number." }
            }.toFloatArray()
            return fromValues(values).validated()
        }
    }
}
