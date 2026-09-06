package com.kurastorage.core.data

import org.junit.Assert.assertEquals
import org.junit.Test

class UploadSelectionReaderTest {
    @Test
    fun `selection keeps stable order and removes duplicate URIs`() {
        val source = FakeUploadDocumentSource()
        val results = UploadSelectionReader(source).read(listOf("content://b", "content://a", "content://b"))

        assertEquals(listOf("content://b", "content://a"), results.map { it.sourceUri })
        assertEquals(2, source.readChecks.size)
    }

    @Test
    fun `invalid metadata and unreadable document fail per item without hiding valid selection`() {
        val source =
            FakeUploadDocumentSource().apply {
                metadata["content://blank"] = UploadDocumentMetadata(" ", 3, "text/plain")
                metadata["content://size"] = UploadDocumentMetadata("size.txt", -1, "text/plain")
                unreadable += "content://locked"
            }

        val results =
            UploadSelectionReader(source).read(
                listOf("content://ok", "content://blank", "content://size", "content://locked"),
            )

        assertEquals(UploadSelectionResult.Ready("content://ok", "ok.txt", 4, "text/plain"), results[0])
        assertEquals(UploadSelectionFailure.INVALID_NAME, (results[1] as UploadSelectionResult.Rejected).reason)
        assertEquals(UploadSelectionFailure.INVALID_SIZE, (results[2] as UploadSelectionResult.Rejected).reason)
        assertEquals(UploadSelectionFailure.PERMISSION_LOST, (results[3] as UploadSelectionResult.Rejected).reason)
    }

    @Test
    fun `missing metadata and permission persistence failure are explicit`() {
        val source =
            FakeUploadDocumentSource().apply {
                metadata.remove("content://missing")
                persistenceFailures += "content://persist"
            }

        val results = UploadSelectionReader(source).read(listOf("content://missing", "content://persist"))

        assertEquals(UploadSelectionFailure.METADATA_UNAVAILABLE, (results[0] as UploadSelectionResult.Rejected).reason)
        assertEquals(UploadSelectionFailure.PERMISSION_LOST, (results[1] as UploadSelectionResult.Rejected).reason)
    }

    private class FakeUploadDocumentSource : UploadDocumentSource {
        val metadata =
            mutableMapOf(
                "content://ok" to UploadDocumentMetadata("ok.txt", 4, "text/plain"),
                "content://a" to UploadDocumentMetadata("a.txt", 1, "text/plain"),
                "content://b" to UploadDocumentMetadata("b.txt", 2, "text/plain"),
                "content://blank" to UploadDocumentMetadata("blank.txt", 3, "text/plain"),
                "content://size" to UploadDocumentMetadata("size.txt", 3, "text/plain"),
                "content://locked" to UploadDocumentMetadata("locked.txt", 4, "text/plain"),
                "content://persist" to UploadDocumentMetadata("persist.txt", 5, "text/plain"),
            )
        val unreadable = mutableSetOf<String>()
        val persistenceFailures = mutableSetOf<String>()
        val readChecks = mutableListOf<String>()

        override fun metadata(sourceUri: String): UploadDocumentMetadata? = metadata[sourceUri]

        override fun persistReadPermission(sourceUri: String): Boolean = sourceUri !in persistenceFailures

        override fun canRead(sourceUri: String): Boolean {
            readChecks += sourceUri
            return sourceUri !in unreadable
        }
    }
}
