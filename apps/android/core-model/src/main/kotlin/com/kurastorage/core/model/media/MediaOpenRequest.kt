package com.kurastorage.core.model.media

data class MediaOpenRequest(
    val fileId: String,
    val contextId: String? = null,
) {
    init {
        require(fileId.isNotBlank())
        require(contextId == null || contextId.isNotBlank())
    }
}
