package com.akatazz.videosynthesizer

import android.app.Activity
import android.Manifest
import android.content.pm.PackageManager
import android.content.Intent
import android.graphics.Color
import android.opengl.GLSurfaceView
import android.os.Bundle
import android.widget.*
import java.util.Locale

class MainActivity : Activity() {
    lateinit var renderer: SynthRenderer
        private set
    private lateinit var surface: GLSurfaceView
    private lateinit var controls: LinearLayout
    private lateinit var status: TextView
    private var parameters = SynthParameters()
    private var paused = false
    private var pendingSave: String? = null
    private lateinit var microphone: MicrophoneInput
    private lateinit var microphoneButton: Button
    private lateinit var audioStatus: TextView
    private var permissionPending = false
    private var foreground = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        savedInstanceState?.getString("preset")?.let { text ->
            runCatching { SynthParameters.fromJson(text) }.onSuccess { parameters = it }
        }
        paused = savedInstanceState?.getBoolean("paused") ?: false
        pendingSave = savedInstanceState?.getString("pendingSave")
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(20, 16, 20, 16)
            setBackgroundColor(Color.rgb(16, 20, 25))
            fitsSystemWindows = true
        }
        setContentView(root)
        root.addView(TextView(this).apply { text = "LZX-V"; textSize = 28f; setTextColor(Color.rgb(156,245,204)) })
        val actions = LinearLayout(this)
        root.addView(actions)
        actions.addView(Button(this).apply {
            text = if (paused) "Resume" else "Pause"
            setOnClickListener { paused = !paused; renderer.paused = paused; text = if (paused) "Resume" else "Pause" }
        })
        actions.addView(Button(this).apply { text = "Reset"; setOnClickListener { renderer.resetGeneration++ } })
        actions.addView(Button(this).apply {
            text = "Presets"
            setOnClickListener {
                PopupMenu(this@MainActivity, this).apply {
                    menu.add("Open preset…").setOnMenuItemClickListener {
                        startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
                            type = "application/json"; addCategory(Intent.CATEGORY_OPENABLE)
                        }, 1); true
                    }
                    menu.add("Save preset…").setOnMenuItemClickListener {
                        pendingSave = parameters.toJson()
                        startActivityForResult(Intent(Intent.ACTION_CREATE_DOCUMENT).apply {
                            type = "application/json"; addCategory(Intent.CATEGORY_OPENABLE)
                            putExtra(Intent.EXTRA_TITLE, "synth-preset.json")
                        }, 2); true
                    }
                    show()
                }
            }
        })
        status = TextView(this).apply { text = "Live · OpenGL ES preview" }
        audioStatus = TextView(this).apply { text = "Microphone off" }
        renderer = SynthRenderer(this).apply {
            parameters = this@MainActivity.parameters
            paused = this@MainActivity.paused
            onError = { message -> runOnUiThread { status.text = message } }
        }
        microphone = MicrophoneInput(onLevels = { levels ->
            renderer.audioBands = levels
            if (levels != AudioBands()) audioStatus.text = String.format(Locale.ROOT,
                "Microphone live · Bass %.2f · Mid %.2f · High %.2f", levels.bass, levels.mid, levels.high)
        }, onError = { message ->
            microphoneButton.text = "Enable microphone"
            audioStatus.text = message
        })
        microphoneButton = Button(this).apply {
            text = "Enable microphone"
            setOnClickListener {
                if (microphone.active || permissionPending) { stopMicrophone(); return@setOnClickListener }
                if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
                    permissionPending = true
                    requestPermissions(arrayOf(Manifest.permission.RECORD_AUDIO), 10)
                } else startMicrophone()
            }
        }
        root.addView(microphoneButton)
        surface = GLSurfaceView(this).apply {
            setEGLContextClientVersion(3)
            setRenderer(renderer)
            contentDescription = "Live synthesis preview"
        }
        root.addView(surface, LinearLayout.LayoutParams(-1, 0, 1f))
        controls = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        root.addView(ScrollView(this).apply { addView(controls) }, LinearLayout.LayoutParams(-1, 0, 1f))
        root.addView(status)
        root.addView(audioStatus)
        buildControls()
    }

    private fun buildControls() {
        controls.removeAllViews()
        SynthParameters.fields.forEachIndexed { index, field ->
            val value = parameters.values()[index]
            val label = TextView(this).apply { text = String.format(Locale.ROOT, "%s  %.3f", field.label, value) }
            controls.addView(label)
            controls.addView(SeekBar(this).apply {
                max = 1000
                progress = ((value - field.min) / (field.max - field.min) * 1000).toInt()
                contentDescription = field.label
                setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                    override fun onStartTrackingTouch(bar: SeekBar?) {}
                    override fun onStopTrackingTouch(bar: SeekBar?) {}
                    override fun onProgressChanged(bar: SeekBar?, progress: Int, fromUser: Boolean) {
                        if (!fromUser) return
                        val next = (field.min + (field.max - field.min) * progress / 1000f).coerceIn(field.min, field.max)
                        parameters = parameters.changed(index, next)
                        renderer.parameters = parameters
                        label.text = String.format(Locale.ROOT, "%s  %.3f", field.label, next)
                    }
                })
            })
        }
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        val save = pendingSave
        if (requestCode == 2) pendingSave = null
        if (resultCode != RESULT_OK) return
        val uri = data?.data ?: return
        try {
            if (requestCode == 1) {
                val text = contentResolver.openInputStream(uri)?.bufferedReader()?.use { it.readText() }
                    ?: error("Could not open preset.")
                parameters = SynthParameters.fromJson(text)
                renderer.parameters = parameters
                renderer.resetGeneration++
                buildControls()
            } else if (requestCode == 2) {
                requireNotNull(save) { "No preset selected for export." }
                val stream = contentResolver.openOutputStream(uri) ?: error("Could not save preset.")
                stream.bufferedWriter().use { it.write(save) }
            }
        } catch (error: Exception) { status.text = error.message }
    }

    override fun onSaveInstanceState(outState: Bundle) {
        outState.putString("preset", parameters.toJson())
        outState.putBoolean("paused", paused)
        outState.putString("pendingSave", pendingSave)
        super.onSaveInstanceState(outState)
    }

    private fun startMicrophone() {
        if (!foreground) return
        try {
            microphone.start()
            microphoneButton.text = "Disable microphone"
            audioStatus.text = "Microphone live"
        } catch (failure: Exception) { audioStatus.text = "Microphone unavailable: ${failure.message}" }
    }

    private fun stopMicrophone() {
        permissionPending = false
        microphone.stop()
        microphoneButton.text = "Enable microphone"
        audioStatus.text = "Microphone off"
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode != 10 || !permissionPending) return
        permissionPending = false
        if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED && foreground) startMicrophone()
        else audioStatus.text = "Microphone access denied or request interrupted. Tap Enable microphone to retry."
    }

    override fun onResume() { super.onResume(); foreground = true; surface.onResume() }
    override fun onPause() { foreground = false; stopMicrophone(); surface.onPause(); super.onPause() }
}
