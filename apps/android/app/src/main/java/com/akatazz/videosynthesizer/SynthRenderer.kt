package com.akatazz.videosynthesizer

import android.content.Context
import android.opengl.GLES30.*
import android.opengl.GLSurfaceView
import android.os.SystemClock
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.opengles.GL10
import kotlin.math.min

class SynthRenderer(context: Context) : GLSurfaceView.Renderer {
    private val vertexSource = context.assets.open("fullscreen.vert").bufferedReader().use { it.readText() }
    private val fragmentSource = context.assets.open("synthesis.frag").bufferedReader().use { it.readText() }
    private val displaySource = context.assets.open("display.frag").bufferedReader().use { it.readText() }
    // Activity writes immutable snapshots; only the GL thread touches GL state.
    @Volatile var parameters = SynthParameters()
    @Volatile var audioBands = AudioBands()
    @Volatile var paused = false
    @Volatile var resetGeneration = 0
    @Volatile var frameCount = 0
        private set
    @Volatile var error: String? = null
        private set
    var onError: ((String) -> Unit)? = null
    private var previousGeneration = -1
    private var clearPending = true
    private var synthProgram = 0
    private var displayProgram = 0
    private var parameterLocation = -1
    private var outputWidth = 1
    private var outputHeight = 1
    private var width = 1
    private var height = 1
    private val textures = IntArray(2)
    private val buffers = IntArray(2)
    private val vertexArray = IntArray(1)
    private var readIndex = 0
    private var previousTime = 0L
    private var time = 0f

    private inline fun safely(action: () -> Unit) {
        if (error != null) return
        try { action() } catch (failure: Exception) {
            error = failure.message ?: "OpenGL rendering failed."
            onError?.invoke(error!!)
        }
    }

    override fun onSurfaceCreated(gl: GL10?, config: EGLConfig?) {
        error = null
        // Old names belong to a lost context and must never be deleted in a new one.
        textures.fill(0); buffers.fill(0)
        safely {
            synthProgram = program(vertexSource, fragmentSource)
            displayProgram = program(vertexSource, displaySource)
            parameterLocation = glGetUniformLocation(synthProgram, "p[0]")
            glGenVertexArrays(1, vertexArray, 0)
            glBindVertexArray(vertexArray[0])
            for (program in intArrayOf(synthProgram, displayProgram)) {
                glUseProgram(program)
                glUniform1i(glGetUniformLocation(program, "history"), 0)
            }
            clearPending = true
            previousTime = 0
        }
    }

    override fun onSurfaceChanged(gl: GL10?, w: Int, h: Int) = safely {
        outputWidth = w.coerceAtLeast(1); outputHeight = h.coerceAtLeast(1)
        val scale = min(1f, min(1280f / outputWidth, 720f / outputHeight))
        width = (outputWidth * scale).toInt().coerceAtLeast(1)
        height = (outputHeight * scale).toInt().coerceAtLeast(1)
        glDeleteTextures(2, textures, 0)
        glDeleteFramebuffers(2, buffers, 0)
        glGenTextures(2, textures, 0)
        glGenFramebuffers(2, buffers, 0)
        for (i in 0..1) {
            glBindTexture(GL_TEXTURE_2D, textures[i])
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR)
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR)
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE)
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE)
            // RGBA8 is color-renderable on every ES 3 device; no float extension required.
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, null)
            glBindFramebuffer(GL_FRAMEBUFFER, buffers[i])
            glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, textures[i], 0)
            check(glCheckFramebufferStatus(GL_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE) { "Feedback framebuffer is incomplete." }
        }
        clearPending = true
    }

    override fun onDrawFrame(gl: GL10?) = safely {
        if (textures[0] == 0) return@safely
        val now = SystemClock.elapsedRealtimeNanos()
        val delta = if (previousTime == 0L) 0f else min((now - previousTime) / 1e9f, 0.1f)
        previousTime = now
        val generation = resetGeneration
        if (generation != previousGeneration) {
            previousGeneration = generation
            clearPending = true
            time = 0f
        }
        val reset = clearPending
        if (reset) {
            glClearColor(0f, 0f, 0f, 1f)
            for (buffer in buffers) { glBindFramebuffer(GL_FRAMEBUFFER, buffer); glClear(GL_COLOR_BUFFER_BIT) }
            clearPending = false
        }
        glBindVertexArray(vertexArray[0])
        glActiveTexture(GL_TEXTURE0)
        if (!paused || reset) {
            if (!paused && !reset) time += delta
            val values = parameters.values()
            val audio = audioBands
            val uniforms = floatArrayOf(time, width.toFloat(), height.toFloat(), *values, audio.bass, audio.mid, audio.high)
            glUseProgram(synthProgram)
            glUniform1fv(parameterLocation, uniforms.size, uniforms, 0)
            glBindFramebuffer(GL_FRAMEBUFFER, buffers[1 - readIndex])
            glViewport(0, 0, width, height)
            glBindTexture(GL_TEXTURE_2D, textures[readIndex])
            glDrawArrays(GL_TRIANGLES, 0, 3)
            readIndex = 1 - readIndex
        }
        glBindFramebuffer(GL_FRAMEBUFFER, 0)
        glViewport(0, 0, outputWidth, outputHeight)
        glUseProgram(displayProgram)
        glBindTexture(GL_TEXTURE_2D, textures[readIndex])
        glDrawArrays(GL_TRIANGLES, 0, 3)
        check(glGetError() == GL_NO_ERROR) { "OpenGL reported a rendering error." }
        frameCount++
    }

    private fun shader(type: Int, source: String): Int {
        val shader = glCreateShader(type)
        glShaderSource(shader, source)
        glCompileShader(shader)
        val success = IntArray(1)
        glGetShaderiv(shader, GL_COMPILE_STATUS, success, 0)
        if (success[0] == 0) {
            val log = glGetShaderInfoLog(shader); glDeleteShader(shader)
            error("Shader compilation failed: $log")
        }
        return shader
    }

    private fun program(vertex: String, fragment: String): Int {
        val vs = shader(GL_VERTEX_SHADER, vertex)
        val fs = shader(GL_FRAGMENT_SHADER, fragment)
        val program = glCreateProgram()
        glAttachShader(program, vs); glAttachShader(program, fs); glLinkProgram(program)
        glDeleteShader(vs); glDeleteShader(fs)
        val success = IntArray(1)
        glGetProgramiv(program, GL_LINK_STATUS, success, 0)
        check(success[0] != 0) { "Shader link failed: ${glGetProgramInfoLog(program)}" }
        return program
    }
}
