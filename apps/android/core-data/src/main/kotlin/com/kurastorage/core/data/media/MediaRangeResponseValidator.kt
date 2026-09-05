package com.kurastorage.core.data.media

internal data class ValidatedMediaRange(
    val responseLength: Long?,
)

internal object MediaRangeResponseValidator {
    fun validate(
        requestedPosition: Long,
        requestedLength: Long,
        statusCode: Int,
        contentRange: String?,
        contentLength: Long?,
    ): ValidatedMediaRange {
        require(requestedPosition >= 0)
        if (statusCode != HTTP_PARTIAL_CONTENT) invalidRange()
        val range = parse(contentRange) ?: invalidRange()
        if (range.start != requestedPosition) invalidRange()
        if (range.end < range.start) invalidRange()
        if (range.total != null && range.total <= range.end) invalidRange()
        if (range.total != null && range.total <= 0) invalidRange()
        val declaredRangeLength = range.end - range.start + RANGE_LENGTH_OFFSET
        if (contentLength != null && contentLength != declaredRangeLength) invalidRange()
        if (requestedLength != MediaRangeRequest.LENGTH_UNSET && declaredRangeLength > requestedLength) {
            invalidRange()
        }
        return ValidatedMediaRange(contentLength ?: declaredRangeLength)
    }

    @Suppress("ReturnCount")
    private fun parse(contentRange: String?): ParsedMediaRange? {
        val match = contentRange?.let(CONTENT_RANGE::matchEntire) ?: return null
        val start = match.groupValues[START_GROUP].toLongOrNull() ?: return null
        val end = match.groupValues[END_GROUP].toLongOrNull() ?: return null
        val totalValue = match.groupValues[TOTAL_GROUP]
        val total = if (totalValue == "*") null else totalValue.toLongOrNull() ?: return null
        return ParsedMediaRange(start, end, total)
    }

    private fun invalidRange(): Nothing = throw MediaDataSourceIOException.InvalidRange

    private val CONTENT_RANGE = Regex("bytes ([0-9]+)-([0-9]+)/([0-9]+|\\*)")
    private const val HTTP_PARTIAL_CONTENT = 206
    private const val START_GROUP = 1
    private const val END_GROUP = 2
    private const val TOTAL_GROUP = 3
    private const val RANGE_LENGTH_OFFSET = 1
}

private data class ParsedMediaRange(
    val start: Long,
    val end: Long,
    val total: Long?,
)
