package com.kurastorage.core.model

import java.nio.charset.StandardCharsets
import java.time.Instant

object SupportedTextMimeTypes {
    const val MAX_CONTENT_BYTES = 1_048_576

    private val values =
        setOf(
            "text/plain",
            "text/markdown",
            "text/csv",
            "application/json",
            "application/xml",
            "application/yaml",
        )

    fun isSupported(mimeType: String?): Boolean = normalize(mimeType) in values

    fun encodedSize(content: String): Int = content.toByteArray(StandardCharsets.UTF_8).size

    fun isWithinSizeLimit(content: String): Boolean = encodedSize(content) <= MAX_CONTENT_BYTES

    private fun normalize(mimeType: String?): String? =
        mimeType
            ?.substringBefore(';')
            ?.trim()
            ?.lowercase()
}

data class TextDocument(
    val content: String,
    val encoding: String,
    val fileVersion: Long,
    val size: Long,
    val sha256: String,
)

enum class FileVersionChangeKind {
    UPLOAD,
    TEXT_EDIT,
    EXTERNAL_CHANGE,
    RESTORE,
    UNKNOWN,
    ;

    companion object {
        fun fromWire(value: String?): FileVersionChangeKind = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

data class FileVersionItem(
    val version: Long,
    val size: Long,
    val sha256: String,
    val changeKind: FileVersionChangeKind,
    val actorDisplayName: String,
    val createdAt: Instant,
)

data class FileVersionPage(
    val items: List<FileVersionItem>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Long,
) {
    val hasNextPage: Boolean get() = page.toLong() * pageSize < totalCount
}

data class TextMutationResult(
    val fileVersion: Long,
    val size: Long,
    val sha256: String,
    val changeKind: FileVersionChangeKind,
    val createdAt: Instant,
)

data class TextConflict(
    val draft: String,
    val expectedVersion: Long,
    val current: TextDocument,
)

fun canEditText(
    permission: SharePermission,
    source: PermissionSource,
): Boolean =
    permission != SharePermission.UNKNOWN &&
        source != PermissionSource.UNKNOWN &&
        (source == PermissionSource.OWNER || permission.strength >= SharePermission.EDITOR.strength)
