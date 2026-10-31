package com.akatazz.videosynthesizer

import kotlin.math.*

data class AudioBands(val bass: Float = 0f, val mid: Float = 0f, val high: Float = 0f)

class AudioAnalysis {
    private val size = 512
    private val samples = FloatArray(size)
    private val real = DoubleArray(size)
    private val imaginary = DoubleArray(size)
    private var count = 0
    private var levels = AudioBands()

    fun push(sample: Float, sampleRate: Int): AudioBands {
        require(sampleRate in 8000..192000)
        samples[count++] = if (sample.isFinite()) sample.coerceIn(-1f, 1f) else 0f
        if (count != size) return levels
        count = 0
        val mean = samples.sumOf { it.toDouble() } / size
        for (i in 0 until size) {
            real[i] = (samples[i] - mean) * (0.5 - 0.5 * cos(2 * PI * i / size))
            imaginary[i] = 0.0
        }
        var j = 0
        for (i in 1 until size) {
            var bit = size shr 1
            while (j and bit != 0) { j = j xor bit; bit = bit shr 1 }
            j = j xor bit
            if (i < j) {
                val r = real[i]; real[i] = real[j]; real[j] = r
                val im = imaginary[i]; imaginary[i] = imaginary[j]; imaginary[j] = im
            }
        }
        var length = 2
        while (length <= size) {
            val angle = -2 * PI / length
            for (offset in 0 until size step length) for (index in 0 until length / 2) {
                val a = offset + index
                val b = a + length / 2
                val c = cos(angle * index); val s = sin(angle * index)
                val r = real[b] * c - imaginary[b] * s
                val im = real[b] * s + imaginary[b] * c
                real[b] = real[a] - r; imaginary[b] = imaginary[a] - im
                real[a] += r; imaginary[a] += im
            }
            length *= 2
        }
        val power = DoubleArray(3)
        for (bin in 1..size / 2) {
            val hz = bin.toDouble() * sampleRate / size
            if (hz < 20) continue
            val band = if (hz < 250) 0 else if (hz < 2000) 1 else 2
            power[band] += (real[bin] * real[bin] + imaginary[bin] * imaginary[bin]) * 16 / (size * size * 1.5)
        }
        levels = AudioBands(
            levels.bass * 0.8f + min(1.0, sqrt(power[0])).toFloat() * 0.2f,
            levels.mid * 0.8f + min(1.0, sqrt(power[1])).toFloat() * 0.2f,
            levels.high * 0.8f + min(1.0, sqrt(power[2])).toFloat() * 0.2f,
        )
        return levels
    }
}
