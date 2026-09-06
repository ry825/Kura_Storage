package com.kurastorage.core.data

import android.database.MatrixCursor
import android.net.Uri
import android.provider.DocumentsContract
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AndroidFolderUploadTreeSourceTest {
    @Test
    fun documentProviderRowsRetainIdsHierarchyEmptyFoldersAndReadability() =
        runBlocking {
            val query = FakeFolderQuery()
            val source = AndroidFolderUploadTreeSource(query, FolderDocumentReadability { !it.toString().endsWith("locked") })
            val treeUri = "content://$AUTHORITY/tree/root"

            val root = source.root(treeUri)
            val rootChildren = source.children(treeUri, root.documentId)
            val nestedChildren = source.children(treeUri, "nested")

            assertEquals("root", root.documentId)
            assertTrue(root.isDirectory)
            assertEquals(listOf("empty", "nested", "top", "locked"), rootChildren.map { it.documentId })
            assertEquals("child", nestedChildren.single().documentId)
            assertTrue(rootChildren.first().isDirectory)
            assertFalse(rootChildren.last().readable)
            assertTrue(rootChildren.all { it.withinTree })
        }

    private class FakeFolderQuery : FolderDocumentQuery {
        override fun query(
            uri: Uri,
            projection: Array<String>,
        ): MatrixCursor {
            val cursor = MatrixCursor(projection)
            if (!uri.pathSegments.contains("children")) {
                cursor.addDocument("root", "Selected", DocumentsContract.Document.MIME_TYPE_DIR, null)
                return cursor
            }
            val documentIndex = uri.pathSegments.indexOf("document")
            when (uri.pathSegments.getOrNull(documentIndex + 1)) {
                "root" -> {
                    cursor.addDocument("empty", "Empty", DocumentsContract.Document.MIME_TYPE_DIR, null)
                    cursor.addDocument("nested", "Nested", DocumentsContract.Document.MIME_TYPE_DIR, null)
                    cursor.addDocument("top", "top.txt", "text/plain", 3)
                    cursor.addDocument("locked", "locked.txt", "text/plain", 4)
                }
                "nested" -> cursor.addDocument("child", "child.txt", "text/plain", 5)
            }
            return cursor
        }

        private fun MatrixCursor.addDocument(
            id: String,
            name: String,
            mimeType: String,
            size: Long?,
        ) {
            addRow(arrayOf<Any?>(id, name, mimeType, size))
        }
    }

    private companion object {
        const val AUTHORITY = "com.kurastorage.test.folder"
    }
}
