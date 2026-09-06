package com.kurastorage.feature.media.photo

import org.junit.Assert.assertEquals
import org.junit.Test

class PhotoFitPolicyTest {
    @Test
    fun `portrait landscape and square images fit isotropically`() {
        val cases =
            listOf(
                PhotoRenderSize(500f, 1_000f) to PhotoRenderSize(400f, 800f),
                PhotoRenderSize(1_600f, 900f) to PhotoRenderSize(800f, 450f),
                PhotoRenderSize(1_000f, 1_000f) to PhotoRenderSize(800f, 800f),
            )
        cases.forEach { (source, expected) ->
            assertEquals(
                expected,
                PhotoFitPolicy.fitWithoutUpscaling(source.widthPx, source.heightPx, 800f, 800f),
            )
        }
    }

    @Test
    fun `small images and exif rotated dimensions are never enlarged`() {
        assertEquals(
            PhotoRenderSize(120f, 80f),
            PhotoFitPolicy.fitWithoutUpscaling(120f, 80f, 1_080f, 1_920f),
        )
        assertEquals(
            PhotoRenderSize(450f, 800f),
            PhotoFitPolicy.fitWithoutUpscaling(900f, 1_600f, 800f, 800f),
        )
    }

    @Test
    fun `pan boundary depends on the uniformly zoomed rendered size`() {
        assertEquals(0f, PhotoFitPolicy.maximumPanPx(400f, 800f, 1f))
        assertEquals(0f, PhotoFitPolicy.maximumPanPx(400f, 800f, 2f))
        assertEquals(200f, PhotoFitPolicy.maximumPanPx(400f, 800f, 3f))
    }
}
