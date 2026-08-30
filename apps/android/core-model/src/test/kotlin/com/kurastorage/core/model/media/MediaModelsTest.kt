package com.kurastorage.core.model.media

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class MediaModelsTest {
    @Test
    fun `photo mime support is explicit and normalized`() {
        assertTrue(SupportedMediaMimeTypes.isPhoto("image/jpeg"))
        assertTrue(SupportedMediaMimeTypes.isPhoto(" IMAGE/HEIC ; profile=main "))
        assertFalse(SupportedMediaMimeTypes.isPhoto("image/svg+xml"))
        assertFalse(SupportedMediaMimeTypes.isPhoto(null))
    }

    @Test
    fun `variant resolver permits quality only for images and videos`() {
        assertEquals(MediaVariant.IMAGE_LOW, MediaVariantResolver.resolve(MediaKind.IMAGE, MediaQuality.LOW))
        assertEquals(MediaVariant.VIDEO_MEDIUM, MediaVariantResolver.resolve(MediaKind.VIDEO, MediaQuality.MEDIUM))
        assertEquals(MediaVariant.ORIGINAL, MediaVariantResolver.resolve(MediaKind.AUDIO, MediaQuality.ORIGINAL))
        assertThrows(IllegalArgumentException::class.java) {
            MediaVariantResolver.resolve(MediaKind.PDF, MediaQuality.LOW)
        }
    }

    @Test
    fun `typed numeric values reject unsafe ranges`() {
        assertEquals(0, ByteCount(0).value)
        assertThrows(IllegalArgumentException::class.java) { ByteCount(-1) }
        assertEquals(0L, MediaPositionMs(0).value)
        assertThrows(IllegalArgumentException::class.java) { MediaPositionMs(-1) }
        assertEquals(0.5f, PlaybackRate(0.5f).value)
        assertEquals(3.0f, PlaybackRate(3.0f).value)
        assertThrows(IllegalArgumentException::class.java) { PlaybackRate(3.01f) }
    }

    @Test
    fun `wire values and unknown job states map without guessing`() {
        assertEquals(MediaVariant.IMAGE_MEDIUM, MediaVariant.fromWireValue("image-medium"))
        assertEquals(null, MediaVariant.fromWireValue("future"))
        assertEquals(MediaJobStatus.UNKNOWN, MediaJobStatus.fromWireValue("FUTURE"))
    }
}
