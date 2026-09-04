@file:Suppress("MaxLineLength")

package com.kurastorage.feature.text

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class BoundedLineDiffTest {
    @Test
    fun `comparison bounds lines and characters without attempting a merge`() {
        val content = List(BoundedLineDiff.MAX_LINES + 50) { "x".repeat(BoundedLineDiff.MAX_LINE_CHARS + 50) }.joinToString("\n")

        val result = BoundedLineDiff.compare(content, "$content\nextra")

        assertEquals(BoundedLineDiff.MAX_LINES, result.size)
        assertTrue(result.all { (it.current?.length ?: 0) <= BoundedLineDiff.MAX_LINE_CHARS })
        assertTrue(BoundedLineDiff.isTruncated(content, "$content\nextra"))
    }
}
