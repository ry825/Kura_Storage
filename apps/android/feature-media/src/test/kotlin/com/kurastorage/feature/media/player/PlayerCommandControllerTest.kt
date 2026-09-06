package com.kurastorage.feature.media.player

import com.kurastorage.core.model.media.LONG_SKIP_MS
import com.kurastorage.core.model.media.PlaybackRate
import com.kurastorage.core.model.media.SHORT_SKIP_MS
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PlayerCommandControllerTest {
    @Test
    fun `play pause seek and skip clamp to a seekable duration`() {
        val engine = FakePlayerEngine(PlayerSnapshot(durationMs = 20_000, seekable = true))
        val controller = PlayerCommandController(engine)

        controller.play()
        assertTrue(engine.snapshot.playWhenReady)
        controller.pause()
        assertFalse(engine.snapshot.playWhenReady)
        controller.seekTo(19_000)
        controller.skipForward(LONG_SKIP_MS)
        assertEquals(20_000, engine.snapshot.positionMs)
        controller.skipBack(SHORT_SKIP_MS)
        assertEquals(17_000, engine.snapshot.positionMs)
    }

    @Test
    fun `seek actions are ignored for unseekable media`() {
        val engine = FakePlayerEngine(PlayerSnapshot(positionMs = 4_000, durationMs = 20_000, seekable = false))
        val controller = PlayerCommandController(engine)

        controller.seekTo(10_000)
        controller.skipBack(SHORT_SKIP_MS)

        assertEquals(4_000, engine.snapshot.positionMs)
    }

    @Test
    fun `only the approved playback rates are accepted`() {
        val engine = FakePlayerEngine()
        val controller = PlayerCommandController(engine)

        controller.setRate(PlaybackRate(1.75f))

        assertEquals(1.75f, engine.snapshot.rate.value)
        org.junit.Assert.assertThrows(IllegalArgumentException::class.java) {
            controller.setRate(PlaybackRate(1.1f))
        }
    }

    @Test
    fun `next playback rate advances and wraps unknown or final values`() {
        assertEquals(1.25f, PlayerCommandController.nextRate(PlaybackRate(1f)).value)
        assertEquals(0.5f, PlayerCommandController.nextRate(PlaybackRate(3f)).value)
        assertEquals(0.5f, PlayerCommandController.nextRate(PlaybackRate(1.1f)).value)
    }

    private class FakePlayerEngine(
        initial: PlayerSnapshot = PlayerSnapshot(),
    ) : PlayerEngine {
        override var snapshot: PlayerSnapshot = initial
            private set

        override fun play() {
            snapshot = snapshot.copy(playWhenReady = true)
        }

        override fun pause() {
            snapshot = snapshot.copy(playWhenReady = false)
        }

        override fun seekTo(positionMs: Long) {
            snapshot = snapshot.copy(positionMs = positionMs)
        }

        override fun setRate(rate: PlaybackRate) {
            snapshot = snapshot.copy(rate = rate)
        }
    }
}
