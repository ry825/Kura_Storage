package com.kurastorage.core.data.media

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Test

class MediaRangeRequestTest {
    @Test
    fun `initial unbounded request omits range and seek requests remaining bytes`() {
        assertNull(MediaRangeRequest.header(position = 0, length = MediaRangeRequest.LENGTH_UNSET))
        assertEquals("bytes=4096-", MediaRangeRequest.header(position = 4096, length = MediaRangeRequest.LENGTH_UNSET))
    }

    @Test
    fun `bounded request uses inclusive end without overflow`() {
        assertEquals("bytes=10-19", MediaRangeRequest.header(position = 10, length = 10))
        assertEquals(
            "bytes=${Long.MAX_VALUE - 1}-${Long.MAX_VALUE}",
            MediaRangeRequest.header(position = Long.MAX_VALUE - 1, length = 2),
        )
    }

    @Test
    fun `invalid position and length fail before network access`() {
        assertThrows(IllegalArgumentException::class.java) { MediaRangeRequest.header(-1, 10) }
        assertThrows(IllegalArgumentException::class.java) { MediaRangeRequest.header(0, 0) }
        assertThrows(IllegalArgumentException::class.java) { MediaRangeRequest.header(Long.MAX_VALUE, 2) }
    }
}
