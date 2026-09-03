package com.kurastorage.core.data.backup

import android.content.ContentResolver
import android.database.Cursor
import android.net.Uri
import android.provider.DocumentsContract
import android.provider.MediaStore
import android.provider.OpenableColumns
import com.kurastorage.core.model.backup.LocalSyncItem
import java.io.FileNotFoundException
import java.io.InputStream

/** Opens content URIs and detects metadata changes at every upload chunk boundary. */
class AndroidBackupContentSource(
    private val resolver: ContentResolver,
) : BackupContentSource {
    override fun open(sourceLocator: String): InputStream =
        resolver.openInputStream(Uri.parse(sourceLocator))
            ?: throw FileNotFoundException("Backup source is unavailable")

    override suspend fun fingerprint(item: LocalSyncItem): String =
        if (metadataStillMatches(item)) item.sourceFingerprint else CHANGED_FINGERPRINT

    private fun metadataStillMatches(item: LocalSyncItem): Boolean {
        val uri = Uri.parse(item.sourceLocator)
        return runCatching {
            if (uri.authority == MediaStore.AUTHORITY) {
                resolver
                    .query(
                        uri,
                        arrayOf(MediaStore.MediaColumns.SIZE, MediaStore.MediaColumns.DATE_MODIFIED),
                        null,
                        null,
                        null,
                    )?.use { cursor -> cursor.matches(item, modifiedInSeconds = true) } ?: false
            } else {
                resolver
                    .query(
                        uri,
                        arrayOf(OpenableColumns.SIZE, DocumentsContract.Document.COLUMN_LAST_MODIFIED),
                        null,
                        null,
                        null,
                    )?.use { cursor -> cursor.matches(item, modifiedInSeconds = false) } ?: false
            }
        }.getOrDefault(false)
    }

    @Suppress("ReturnCount")
    private fun Cursor.matches(
        item: LocalSyncItem,
        modifiedInSeconds: Boolean,
    ): Boolean {
        if (!moveToFirst()) return false
        val sizeIndex = getColumnIndex(OpenableColumns.SIZE)
        if (sizeIndex < 0 || isNull(sizeIndex) || getLong(sizeIndex) != item.size) return false
        val modifiedColumn =
            if (modifiedInSeconds) {
                MediaStore.MediaColumns.DATE_MODIFIED
            } else {
                DocumentsContract.Document.COLUMN_LAST_MODIFIED
            }
        val modifiedIndex = getColumnIndex(modifiedColumn)
        if (modifiedIndex < 0 || isNull(modifiedIndex)) return true
        val expected = if (modifiedInSeconds) item.modifiedAt.epochSecond else item.modifiedAt.toEpochMilli()
        return getLong(modifiedIndex) == expected
    }

    private companion object {
        const val CHANGED_FINGERPRINT = ""
    }
}
