package com.akatazz.videosynthesizer

import org.junit.Assert.*
import org.junit.Test
import kotlin.math.*

class AudioTest {
    @Test fun classifiesCalibratedTones() {
        for (bin in listOf(2, 10, 64)) {
            val analysis = AudioAnalysis()
            var bands = AudioBands()
            for (sample in 0 until 512 * 40)
                bands = analysis.push(0.8f * sin(2 * PI * bin * sample / 512).toFloat(), 32000)
            val levels = listOf(bands.bass, bands.mid, bands.high)
            val expected = if (bin == 2) 0 else if (bin == 10) 1 else 2
            assertTrue(levels[expected] in 0.75f..0.85f)
            levels.forEachIndexed { index, value -> if (index != expected) assertTrue(value < 0.01f) }
        }
    }

    @Test fun rejectsDCAndNonFiniteInput() {
        val analysis = AudioAnalysis()
        var bands = AudioBands()
        repeat(1024) { bands = analysis.push(0.5f, 44100) }
        assertEquals(AudioBands(), bands)
        repeat(1024) { bands = analysis.push(Float.NaN, 48000) }
        assertEquals(AudioBands(), bands)
    }
}
