package com.akatazz.videosynthesizer

import org.json.JSONObject

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
) {
    fun values() = floatArrayOf(oscFreq, oscSpeed, fractalZoom, fractalDetail, fractalMorph, caFeed, caKill, feedbackMix, colorShift)

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
        val json = JSONObject().put("schemaVersion", 1)
        values().forEachIndexed { index, value -> json.put(fields[index].key, value.toDouble()) }
        return json.toString(2)
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
        )

        private fun fromValues(v: FloatArray) = SynthParameters(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8])

        fun fromJson(text: String): SynthParameters {
            val json = JSONObject(text)
            val version = json.get("schemaVersion")
            require(version is Number && version.toDouble() == 1.0) { "Unsupported preset version." }
            val values = fields.map {
                val value = json.get(it.key)
                require(value is Number) { "${it.label} must be a number." }
                value.toFloat()
            }.toFloatArray()
            return fromValues(values).validated()
        }
    }
}
