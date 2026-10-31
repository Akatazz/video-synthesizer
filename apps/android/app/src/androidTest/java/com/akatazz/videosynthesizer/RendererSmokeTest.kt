package com.akatazz.videosynthesizer

import android.content.Intent
import android.test.InstrumentationTestCase
import android.os.SystemClock

@Suppress("DEPRECATION")
class RendererSmokeTest : InstrumentationTestCase() {
    fun testNativeRendererStartsAndResets() {
        val intent = Intent(instrumentation.targetContext, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        val activity = instrumentation.startActivitySync(intent) as MainActivity
        try {
            val deadline = SystemClock.elapsedRealtime() + 15000
            while (activity.renderer.frameCount < 3 && activity.renderer.error == null && SystemClock.elapsedRealtime() < deadline)
                Thread.sleep(50)
            assertNull(activity.renderer.error)
            assertTrue("Renderer did not present frames", activity.renderer.frameCount >= 3)
            val before = activity.renderer.frameCount
            instrumentation.runOnMainSync {
                activity.renderer.paused = true
                activity.renderer.resetGeneration++
            }
            val resetDeadline = SystemClock.elapsedRealtime() + 5000
            while (activity.renderer.frameCount <= before + 1 && activity.renderer.error == null && SystemClock.elapsedRealtime() < resetDeadline)
                Thread.sleep(50)
            assertNull(activity.renderer.error)
            assertTrue(activity.renderer.frameCount > before + 1)
        } finally { instrumentation.runOnMainSync { activity.finish() } }
    }
}
