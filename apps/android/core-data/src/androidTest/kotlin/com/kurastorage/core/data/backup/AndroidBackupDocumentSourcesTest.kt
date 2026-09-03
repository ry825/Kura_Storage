package com.kurastorage.core.data.backup

import android.database.MatrixCursor
import android.net.Uri
import android.os.Build
import android.provider.DocumentsContract
import android.provider.MediaStore
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupNetworkMode
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.LocalBackupRule
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.time.Instant
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class AndroidBackupDocumentSourcesTest {
    @Test
    fun mediaStoreUsesGenerationSelectionAndStreamsProjectedMetadata() =
        runBlocking {
            val query = FakeMediaQuery()
            val source = AndroidMediaStoreDocumentSource(query, MediaStoreSnapshotReader { SourceSnapshot("v1", 9) })
            val observed = mutableListOf<ScannedDocumentMetadata>()

            val outcome = source.scan(rule(BackupSourceType.MEDIA_IMAGES, MediaStore.VOLUME_EXTERNAL), 4, observed::add)

            assertTrue(outcome.completed)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                assertEquals(listOf("4", "4"), query.selectionArguments?.toList())
            } else {
                assertTrue(query.selectionArguments == null)
            }
            assertEquals(1, observed.size)
            assertEquals("DCIM/Camera/photo.jpg", observed.single().relativePath)
            assertTrue(observed.single().providerKey.startsWith("media:MEDIA_IMAGES:external:"))
        }

    @Test
    fun safTreeStreamsNestedChildrenAndRejectsProviderCycles() =
        runBlocking {
            val query = FakeDocumentsQuery()
            val source = AndroidSafTreeDocumentSource(query)
            val observed = mutableListOf<ScannedDocumentMetadata>()

            source.scan(rule(BackupSourceType.SAF_TREE, "content://$DOCUMENT_AUTHORITY/tree/root"), null, observed::add)

            assertEquals(listOf("top.jpg", "Folder/nested.jpg"), observed.map { it.relativePath })
            query.returnCycle = true
            assertThrows(IllegalArgumentException::class.java) {
                runBlocking {
                    source.scan(rule(BackupSourceType.SAF_TREE, "content://$DOCUMENT_AUTHORITY/tree/root"), null) {}
                }
            }
            Unit
        }

    @Test
    fun providerFailureAndSafHardLimitFailWithoutReportingCompletion() {
        val denied = AndroidSafTreeDocumentSource(AndroidContentQuery { _, _, _, _, _ -> throw SecurityException() })
        assertThrows(SecurityException::class.java) {
            runBlocking {
                denied.scan(rule(BackupSourceType.SAF_TREE, "content://$DOCUMENT_AUTHORITY/tree/root"), null) {}
            }
        }

        val limited = AndroidSafTreeDocumentSource(FakeDocumentsQuery(), maximumItems = 1)
        assertThrows(IllegalArgumentException::class.java) {
            runBlocking {
                limited.scan(rule(BackupSourceType.SAF_TREE, "content://$DOCUMENT_AUTHORITY/tree/root"), null) {}
            }
        }
    }

    private fun rule(
        sourceType: BackupSourceType,
        sourceLocator: String,
    ) = LocalBackupRule(
        id = BackupRuleId(UUID.randomUUID().toString()),
        accountScopeId = AccountScopeId("a".repeat(64)),
        sourceType = sourceType,
        sourceLocator = sourceLocator,
        displayName = "Source",
        remoteFolderId = UUID.randomUUID().toString(),
        enabled = true,
        networkMode = BackupNetworkMode.LOCAL_DIRECT_ONLY,
        requiresChargingForInitialRun = false,
        minimumBatteryPercent = 20,
        initialRunCompletedAt = null,
        pausedAt = null,
        createdAt = Instant.EPOCH,
        updatedAt = Instant.EPOCH,
    )

    private companion object {
        const val DOCUMENT_AUTHORITY = "com.kurastorage.test.documents"
    }
}

private class FakeMediaQuery : AndroidContentQuery {
    var selectionArguments: Array<String>? = null

    override fun query(
        uri: Uri,
        projection: Array<String>,
        selection: String?,
        selectionArgs: Array<String>?,
        sortOrder: String?,
    ): MatrixCursor {
        selectionArguments = selectionArgs
        val values =
            projection
                .map<String, Any> { column ->
                    when (column) {
                        MediaStore.MediaColumns._ID -> 7L
                        MediaStore.MediaColumns.DISPLAY_NAME -> "photo.jpg"
                        MediaStore.MediaColumns.MIME_TYPE -> "image/jpeg"
                        MediaStore.MediaColumns.SIZE -> 512L
                        MediaStore.MediaColumns.DATE_MODIFIED -> 3L
                        MediaStore.MediaColumns.DATE_ADDED -> 2L
                        MediaStore.MediaColumns.RELATIVE_PATH -> "DCIM/Camera/"
                        MediaStore.MediaColumns.GENERATION_ADDED -> 5L
                        MediaStore.MediaColumns.GENERATION_MODIFIED -> 8L
                        else -> error("Unexpected projection")
                    }
                }.toTypedArray()
        return MatrixCursor(projection).apply { addRow(values) }
    }
}

private class FakeDocumentsQuery : AndroidContentQuery {
    var returnCycle = false

    override fun query(
        uri: Uri,
        projection: Array<String>,
        selection: String?,
        selectionArgs: Array<String>?,
        sortOrder: String?,
    ): MatrixCursor {
        val cursor = MatrixCursor(projection)
        val documentIndex = uri.pathSegments.indexOf("document")
        val parentId = uri.pathSegments.getOrNull(documentIndex + 1) ?: "root"
        if (parentId == "root") {
            cursor.addDocument("folder", "Folder", DocumentsContract.Document.MIME_TYPE_DIR, 0, 0)
            cursor.addDocument("top", "top.jpg", "image/jpeg", 10, 1)
            if (returnCycle) cursor.addDocument("root", "Loop", DocumentsContract.Document.MIME_TYPE_DIR, 0, 0)
        } else if (parentId == "folder") {
            cursor.addDocument("nested", "nested.jpg", "image/jpeg", 20, 2)
        }
        return cursor
    }

    private fun MatrixCursor.addDocument(
        id: String,
        name: String,
        mime: String,
        size: Long,
        modified: Long,
    ) {
        addRow(arrayOf<Any>(id, name, mime, size, modified))
    }
}
