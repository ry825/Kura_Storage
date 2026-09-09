package com.kurastorage.core.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Test

class ReliabilityModelsTest {
    @Test
    fun `bulk trash summary exposes only aggregate counts`() {
        assertEquals(3, BulkTrashSummary(2, 1).attemptedCount)
        assertThrows(IllegalArgumentException::class.java) { BulkTrashSummary(-1, 0) }
    }

    @Test
    fun `session scoped keys reject negative generations`() {
        ThumbnailFailureNoticeKey(0, 1)
        ThumbnailRetryKey(2)
        PhotoNavigationRequestToken(3)

        assertThrows(IllegalArgumentException::class.java) { ThumbnailFailureNoticeKey(-1, 0) }
        assertThrows(IllegalArgumentException::class.java) { ThumbnailRetryKey(-1) }
        assertThrows(IllegalArgumentException::class.java) { PhotoNavigationRequestToken(-1) }
    }

    @Test
    fun `user facing summaries and session keys do not retain string identifiers or details`() {
        val boundaryTypes =
            listOf(
                BulkTrashSummary::class.java,
                ThumbnailFailureNoticeKey::class.java,
                ThumbnailRetryKey::class.java,
                PhotoNavigationRequestToken::class.java,
            )

        boundaryTypes.forEach { type ->
            assertFalse(type.declaredFields.any { it.type == String::class.java })
        }
    }
}
