package com.kurastorage.core.ui.formatting

import org.junit.Assert.assertEquals
import org.junit.Test

class FileSizeFormatterTest {
    @Test
    fun `uses binary thresholds with user-facing unit names`() {
        assertEquals("0 B", formatFileSize(0))
        assertEquals("1,023 B", formatFileSize(1_023))
        assertEquals("1 KB", formatFileSize(1_024))
        assertEquals("1.5 KB", formatFileSize(1_536))
        assertEquals("1,023.9 KB", formatFileSize(1_048_524))
        assertEquals("1 MB", formatFileSize(1_048_576))
        assertEquals("1.5 MB", formatFileSize(1_572_864))
        assertEquals("1 GB", formatFileSize(1_073_741_824))
    }

    @Test
    fun `keeps large values in GB without overflow`() {
        assertEquals("1,024 GB", formatFileSize(1_099_511_627_776))
        assertEquals("8,589,934,592 GB", formatFileSize(Long.MAX_VALUE))
    }

    @Test
    fun `does not present absent or invalid sizes as real data`() {
        assertEquals("Unknown", formatFileSize(null))
        assertEquals("Unknown", formatFileSize(-1))
        assertEquals("Not available", formatFileSize(null, unknownLabel = "Not available"))
    }
}
