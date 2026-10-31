package com.akatazz.videosynthesizer

import android.app.Activity
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
        renderer = SynthRenderer(this).apply {
            parameters = this@MainActivity.parameters
            paused = this@MainActivity.paused
            onError = { message -> runOnUiThread { status.text = message } }
        }
        surface = GLSurfaceView(this).apply {
            setEGLContextClientVersion(3)
            setRenderer(renderer)
            contentDescription = "Live synthesis preview"
        }
        root.addView(surface, LinearLayout.LayoutParams(-1, 0, 1f))
        controls = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        root.addView(ScrollView(this).apply { addView(controls) }, LinearLayout.LayoutParams(-1, 0, 1f))
        root.addView(status)
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

    override fun onResume() { super.onResume(); surface.onResume() }
    override fun onPause() { surface.onPause(); super.onPause() }
}
