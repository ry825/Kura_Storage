package com.kurastorage.core.data.media

object MediaRangeRequest {
    const val LENGTH_UNSET = -1L

    fun header(
        position: Long,
        length: Long,
    ): String? {
        require(position >= 0) { "Range position must not be negative" }
        require(length == LENGTH_UNSET || length > 0) { "Range length must be positive or unset" }
        return when {
            position == 0L && length == LENGTH_UNSET -> null
            length == LENGTH_UNSET -> "bytes=$position-"
            else -> {
                require(position <= Long.MAX_VALUE - (length - 1)) { "Range end overflow" }
                "bytes=$position-${position + length - 1}"
            }
        }
    }
}
