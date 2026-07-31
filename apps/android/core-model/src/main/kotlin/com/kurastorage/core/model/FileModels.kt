package com.kurastorage.core.model

import java.time.Instant

enum class FileEntryType { FILE, FOLDER }

enum class FileEntryStatus { ACTIVE, TRASHED }

data class FileEntry(
    val id: String,
    val parentId: String?,
    val name: String,
    val entryType: FileEntryType,
    val mimeType: String?,
    val size: Long,
    val status: FileEntryStatus,
    val fileVersion: Long,
    val trashedAt: Instant?,
    val createdAt: Instant,
    val updatedAt: Instant,
)

data class FilePage(
    val parentId: String?,
    val items: List<FileEntry>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Long,
) {
    val hasNextPage: Boolean get() = page.toLong() * pageSize < totalCount
}

data class UploadOperation(
    val sourceUri: String,
    val destinationFolderId: String,
    val fileName: String,
    val size: Long,
    val contentType: String?,
    val sha256: String? = null,
    val idempotencyKey: String,
)

data class DownloadOperation(
    val file: FileEntry,
    val destinationUri: String,
)

sealed interface TransferEvent {
    data class Progress(
        val transferredBytes: Long,
        val totalBytes: Long?,
    ) : TransferEvent

    data class UploadCompleted(
        val file: FileEntry,
    ) : TransferEvent

    data class DownloadCompleted(
        val destinationUri: String,
    ) : TransferEvent

    data class Failed(
        val error: Throwable,
        val partialFileRemoved: Boolean? = null,
    ) : TransferEvent
}
