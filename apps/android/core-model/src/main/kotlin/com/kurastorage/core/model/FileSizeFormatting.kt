package com.kurastorage.core.model

import java.math.BigDecimal
import java.math.RoundingMode
import java.text.DecimalFormat
import java.text.DecimalFormatSymbols
import java.util.Locale

/** Presentation primitive shared by UI-facing modules without introducing a data-to-UI dependency. */
fun formatUserFileSize(
    bytes: Long?,
    unknownLabel: String = "Unknown",
): String {
    if (bytes == null || bytes < 0) return unknownLabel
    val (divisor, unit) =
        when {
            bytes >= BYTES_PER_GB -> BYTES_PER_GB to "GB"
            bytes >= BYTES_PER_MB -> BYTES_PER_MB to "MB"
            bytes >= BYTES_PER_KB -> BYTES_PER_KB to "KB"
            else -> 1L to "B"
        }
    val pattern = if (divisor == 1L) "#,##0" else "#,##0.#"
    val value =
        BigDecimal
            .valueOf(bytes)
            .divide(BigDecimal.valueOf(divisor), if (divisor == 1L) 0 else 1, RoundingMode.HALF_UP)
    return "${DecimalFormat(pattern, DecimalFormatSymbols.getInstance(Locale.US)).format(value)} $unit"
}

private const val BYTES_PER_KB = 1024L
private const val BYTES_PER_MB = BYTES_PER_KB * 1024L
private const val BYTES_PER_GB = BYTES_PER_MB * 1024L
