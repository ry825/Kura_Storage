package com.kurastorage.core.data.media

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class MediaRangeResponseValidatorTest {
    @Test
    fun `valid partial response accepts initial range and seek`() {
        assertEquals(
            1_000L,
            MediaRangeResponseValidator
                .validate(0, MediaRangeRequest.LENGTH_UNSET, 206, "bytes 0-999/2000", 1_000)
                .responseLength,
        )
        assertEquals(
            500L,
            MediaRangeResponseValidator
                .validate(1_500, MediaRangeRequest.LENGTH_UNSET, 206, "bytes 1500-1999/2000", 500)
                .responseLength,
        )
    }

    @Test
    fun `status and content range must match requested range`() {
        assertInvalid { MediaRangeResponseValidator.validate(0, -1, 200, null, 2_000) }
        assertInvalid { MediaRangeResponseValidator.validate(500, -1, 206, "bytes 0-499/2000", 500) }
        assertInvalid { MediaRangeResponseValidator.validate(0, -1, 206, "invalid", 500) }
        assertInvalid { MediaRangeResponseValidator.validate(0, -1, 206, "bytes 0-999/1000", 900) }
        assertInvalid { MediaRangeResponseValidator.validate(0, 100, 206, "bytes 0-199/1000", 200) }
    }

    private fun assertInvalid(block: () -> Unit) {
        assertThrows(MediaDataSourceIOException.InvalidRange::class.java, block)
    }
}
