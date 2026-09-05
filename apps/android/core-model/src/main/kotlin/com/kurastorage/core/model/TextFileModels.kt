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

    fun isDirectlyOpenable(
        name: String,
        mimeType: String?,
    ): Boolean {
        val normalizedMime = normalize(mimeType)
        val hasTextMimeType = normalizedMime?.startsWith("text/") == true || normalizedMime in values
        val hasTextApplicationMimeType =
            normalizedMime in setOf("application/javascript", "application/x-yaml", "application/toml")
        val hasTextExtension =
            name.substringAfterLast('.', "").lowercase() in
                setOf(
                    "txt",
                    "md",
                    "markdown",
                    "csv",
                    "tsv",
                    "json",
                    "xml",
                    "yaml",
                    "yml",
                    "log",
                    "ini",
                    "conf",
                    "config",
                    "properties",
                    "toml",
                    "sql",
                    "sh",
                    "kt",
                    "java",
                    "cs",
                    "js",
                    "ts",
                    "css",
                    "html",
                    "htm",
                    "svg",
                )
        return hasTextMimeType || hasTextApplicationMimeType || hasTextExtension
    }

    fun isLikelyText(content: String): Boolean {
        val controls = content.count { it.isISOControl() && it !in setOf('\t', '\n', '\r', '\u000C') }
        return when {
            '\u0000' in content -> false
            content.isEmpty() -> true
            else -> controls.toDouble() / content.length <= MAX_CONTROL_CHARACTER_RATIO
        }
    }

    fun encodedSize(content: String): Int = content.toByteArray(StandardCharsets.UTF_8).size

    fun isWithinSizeLimit(content: String): Boolean = encodedSize(content) <= MAX_CONTENT_BYTES

    private fun normalize(mimeType: String?): String? =
        mimeType
            ?.substringBefore(';')
            ?.trim()
            ?.lowercase()

    private const val MAX_CONTROL_CHARACTER_RATIO = 0.02
}

data class TextDocument(
    val content: String,
    val encoding: String,
    val fileVersion: Long,
    val size: Long,
    val sha256: String,
    val decodeStatus: TextDecodeStatus = TextDecodeStatus.EXACT,
)

enum class TextDecodeStatus {
    EXACT,
    LOSSY,
    UNKNOWN,
    ;

    companion object {
        fun fromWire(value: String?): TextDecodeStatus = entries.firstOrNull { it.name == value } ?: UNKNOWN
    }
}

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
