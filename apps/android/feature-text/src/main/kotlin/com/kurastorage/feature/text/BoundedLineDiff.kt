package com.kurastorage.feature.text

enum class LineDiffKind { SAME, CHANGED, ADDED, REMOVED }

data class LineDiff(
    val lineNumber: Int,
    val current: String?,
    val proposed: String?,
    val kind: LineDiffKind,
)

object BoundedLineDiff {
    const val MAX_LINES = 400
    const val MAX_LINE_CHARS = 512

    fun compare(
        current: String,
        proposed: String,
    ): List<LineDiff> {
        val currentLines =
            current
                .lineSequence()
                .take(MAX_LINES)
                .map(::bound)
                .toList()
        val proposedLines =
            proposed
                .lineSequence()
                .take(MAX_LINES)
                .map(::bound)
                .toList()
        return List(maxOf(currentLines.size, proposedLines.size)) { index ->
            val old = currentLines.getOrNull(index)
            val new = proposedLines.getOrNull(index)
            LineDiff(
                lineNumber = index + 1,
                current = old,
                proposed = new,
                kind =
                    when {
                        old == null -> LineDiffKind.ADDED
                        new == null -> LineDiffKind.REMOVED
                        old == new -> LineDiffKind.SAME
                        else -> LineDiffKind.CHANGED
                    },
            )
        }
    }

    fun isTruncated(vararg values: String): Boolean =
        values.any { value ->
            var lines = 0
            value.lineSequence().any { line ->
                lines += 1
                lines > MAX_LINES || line.length > MAX_LINE_CHARS
            }
        }

    private fun bound(value: String): String = value.take(MAX_LINE_CHARS)
}
