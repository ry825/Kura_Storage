package com.kurastorage.core.ui.formatting

import com.kurastorage.core.model.formatUserFileSize

/** Formats a byte count for user-facing Android UI with binary thresholds. */
fun formatFileSize(
    bytes: Long?,
    unknownLabel: String = "Unknown",
): String = formatUserFileSize(bytes, unknownLabel)
