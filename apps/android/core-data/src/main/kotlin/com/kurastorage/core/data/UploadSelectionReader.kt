package com.kurastorage.core.data

import android.content.ContentResolver
import android.content.Intent
import android.net.Uri
import android.provider.OpenableColumns

data class UploadDocumentMetadata(
    val displayName: String,
    val size: Long,
    val contentType: String?,
)

enum class UploadSelectionFailure(
    val userMessage: String,
) {
    METADATA_UNAVAILABLE("File information could not be read."),
    INVALID_NAME("The selected file has an invalid name."),
    INVALID_SIZE("The selected file size is unavailable."),
    PERMISSION_LOST("Read access to the selected file is unavailable."),
}

sealed interface UploadSelectionResult {
    val sourceUri: String

    data class Ready(
        override val sourceUri: String,
        val displayName: String,
        val size: Long,
        val contentType: String?,
    ) : UploadSelectionResult

    data class Rejected(
        override val sourceUri: String,
        val displayName: String?,
        val reason: UploadSelectionFailure,
    ) : UploadSelectionResult
}

interface UploadDocumentSource {
    fun metadata(sourceUri: String): UploadDocumentMetadata?

    fun persistReadPermission(sourceUri: String): Boolean

    fun canRead(sourceUri: String): Boolean
}

class UploadSelectionReader(
    private val source: UploadDocumentSource,
) {
    fun read(sourceUris: List<String>): List<UploadSelectionResult> =
        sourceUris.distinct().map { sourceUri ->
            val metadata =
                source.metadata(sourceUri)
                    ?: return@map UploadSelectionResult.Rejected(
                        sourceUri,
                        null,
                        UploadSelectionFailure.METADATA_UNAVAILABLE,
                    )
            when {
                !metadata.displayName.isValidUploadName() ->
                    UploadSelectionResult.Rejected(
                        sourceUri,
                        metadata.displayName,
                        UploadSelectionFailure.INVALID_NAME,
                    )
                metadata.size < 0 ->
                    UploadSelectionResult.Rejected(sourceUri, metadata.displayName, UploadSelectionFailure.INVALID_SIZE)
                !source.persistReadPermission(sourceUri) || !source.canRead(sourceUri) ->
                    UploadSelectionResult.Rejected(
                        sourceUri,
                        metadata.displayName,
                        UploadSelectionFailure.PERMISSION_LOST,
                    )
                else ->
                    UploadSelectionResult.Ready(
                        sourceUri,
                        metadata.displayName,
                        metadata.size,
                        metadata.contentType,
                    )
            }
        }
}

class AndroidUploadDocumentSource(
    private val resolver: ContentResolver,
) : UploadDocumentSource {
    override fun metadata(sourceUri: String): UploadDocumentMetadata? =
        runCatching {
            val uri = Uri.parse(sourceUri)
            resolver
                .query(
                    uri,
                    arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE),
                    null,
                    null,
                    null,
                )?.use { cursor ->
                    if (!cursor.moveToFirst()) return@use null
                    val nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    val sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE)
                    val requiredColumnsPresent = nameIndex >= 0 && sizeIndex >= 0
                    if (!requiredColumnsPresent || cursor.isNull(nameIndex) || cursor.isNull(sizeIndex)) {
                        return@use null
                    }
                    UploadDocumentMetadata(
                        displayName = cursor.getString(nameIndex),
                        size = cursor.getLong(sizeIndex),
                        contentType =
                            resolver
                                .getType(uri)
                                ?.substringBefore(';')
                                ?.trim()
                                ?.takeIf(String::isNotBlank),
                    )
                }
        }.getOrNull()

    override fun persistReadPermission(sourceUri: String): Boolean =
        runCatching {
            resolver.takePersistableUriPermission(Uri.parse(sourceUri), Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }.isSuccess

    override fun canRead(sourceUri: String): Boolean =
        runCatching {
            resolver.openInputStream(Uri.parse(sourceUri))?.use { } != null
        }.getOrDefault(false)
}

private fun String.isValidUploadName(): Boolean =
    isNotBlank() &&
        this !in setOf(".", "..") &&
        none { it == '/' || it == '\\' || it.code < MIN_PRINTABLE_CHARACTER_CODE }

private const val MIN_PRINTABLE_CHARACTER_CODE = 32
