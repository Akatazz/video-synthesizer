package com.akatazz.videosynthesizer

import android.annotation.SuppressLint
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.os.Handler
import android.os.Looper

class MicrophoneInput(private val onLevels: (AudioBands) -> Unit, private val onError: (String) -> Unit) {
    private var recorder: AudioRecord? = null
    private var worker: Thread? = null
    @Volatile private var generation = 0
    @Volatile var active = false
        private set
    private val main = Handler(Looper.getMainLooper())

    // Activity checks RECORD_AUDIO immediately before calling this method.
    @SuppressLint("MissingPermission")
    fun start() {
        stop()
        val id = generation
        val minimum = AudioRecord.getMinBufferSize(48000, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_FLOAT)
        require(minimum > 0) { "This device does not support mono float microphone capture." }
        val next = AudioRecord.Builder()
            .setAudioSource(MediaRecorder.AudioSource.MIC)
            .setAudioFormat(AudioFormat.Builder().setSampleRate(48000)
                .setChannelMask(AudioFormat.CHANNEL_IN_MONO).setEncoding(AudioFormat.ENCODING_PCM_FLOAT).build())
            .setBufferSizeInBytes(maxOf(minimum, 8192)).build()
        try {
            check(next.state == AudioRecord.STATE_INITIALIZED) { "Microphone initialization failed." }
            next.startRecording()
            check(next.recordingState == AudioRecord.RECORDSTATE_RECORDING) { "Microphone could not start." }
        } catch (failure: Exception) { next.release(); throw failure }
        recorder = next
        active = true
        worker = Thread({
            val analysis = AudioAnalysis()
            val samples = FloatArray(512)
            var frames = 0
            try {
                while (active && generation == id) {
                    val count = next.read(samples, 0, samples.size, AudioRecord.READ_BLOCKING)
                    if (!active || generation != id) break
                    check(count > 0) { "Microphone read failed ($count)." }
                    var levels = AudioBands()
                    for (index in 0 until count) levels = analysis.push(samples[index], next.sampleRate)
                    if (++frames % 3 == 0) {
                        val snapshot = levels
                        main.post { if (active && generation == id) onLevels(snapshot) }
                    }
                }
            } catch (failure: Exception) {
                main.post {
                    if (generation == id) {
                        active = false
                        onLevels(AudioBands())
                        onError(failure.message ?: "Microphone capture failed.")
                    }
                }
            } finally { next.release() }
        }, "SynthMicrophone").apply { isDaemon = true; start() }
    }

    fun stop() {
        generation++
        active = false
        runCatching { recorder?.stop() } // Unblocks a pending read before joining.
        worker?.join(1000)
        recorder = null
        worker = null
        onLevels(AudioBands())
    }
}
