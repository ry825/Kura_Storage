package com.kurastorage.core.data.media

import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.OriginalMetadata
import kotlinx.coroutines.test.runTest
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.Response
import okhttp3.ResponseBody.Companion.toResponseBody
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class TemporaryPdfStoreTest {
    @get:Rule val temporary = TemporaryFolder()

    @Test
    fun `streams valid PDF to server controlled hashed name`() =
        runTest {
            val bytes = "%PDF-1.7\ncontent".toByteArray()
            val store =
                TemporaryPdfStore(
                    temporary.root,
                    "scope",
                    FakeRepository(bytes),
                    availableBytes = { Long.MAX_VALUE },
                )

            val file = store.download("../../unsafe name", 4, metadata(bytes.size.toLong()))

            assertTrue(file.name.matches(Regex("[0-9a-f]{64}\\.pdf")))
            assertEquals(bytes.size.toLong(), file.length())
            assertFalse(file.absolutePath.contains("unsafe"))
        }

    @Test
    fun `short or corrupt response removes partial file`() =
        runTest {
            val bytes = "not-pdf".toByteArray()
            val store =
                TemporaryPdfStore(
                    temporary.root,
                    "scope",
                    FakeRepository(bytes),
                    availableBytes = { Long.MAX_VALUE },
                )

            val error = runCatching { store.download("file", 1, metadata(bytes.size.toLong())) }.exceptionOrNull()

            assertTrue(error is InvalidPdfException)
            assertTrue(temporary.root.walkTopDown().none { it.name.endsWith(".part") })
        }

    @Test
    fun `rejects oversized PDF and insufficient free space before content request`() =
        runTest {
            val repository = FakeRepository("%PDF-".toByteArray())
            val store = TemporaryPdfStore(temporary.root, "scope", repository, availableBytes = { 1L })

            assertTrue(
                runCatching {
                    store.download("file", 1, metadata(TemporaryPdfStore.MAX_FILE_BYTES + 1))
                }.exceptionOrNull() is PdfTooLargeException,
            )
            assertTrue(
                runCatching { store.download("file", 1, metadata(5)) }.exceptionOrNull() is
                    InsufficientPdfStorageException,
            )
            assertEquals(0, repository.requests)
        }

    @Test
    fun `cleanup preserves active lease then removes it after release`() =
        runTest {
            val store =
                TemporaryPdfStore(
                    temporary.root,
                    "scope",
                    FakeRepository("%PDF-".toByteArray()),
                    availableBytes = { Long.MAX_VALUE },
                )
            val directory = java.io.File(temporary.root, "media-pdf/scope")
            val cached =
                java.io.File(directory, "cached.pdf").apply {
                    writeBytes("%PDF-cache".toByteArray())
                    setLastModified(System.currentTimeMillis() - TemporaryPdfStore.UNREFERENCED_TTL.toMillis() - 1)
                }
            val lease = store.acquire(cached)
            cached.setLastModified(System.currentTimeMillis() - TemporaryPdfStore.UNREFERENCED_TTL.toMillis() - 1)

            store.cleanupExpired()
            assertTrue(cached.exists())

            lease.close()
            cached.setLastModified(System.currentTimeMillis() - TemporaryPdfStore.UNREFERENCED_TTL.toMillis() - 1)
            store.cleanupExpired()
            assertFalse(cached.exists())
        }

    private fun metadata(size: Long) = OriginalMetadata(ByteCount(size), "application/pdf", true)

    private class FakeRepository(
        private val bytes: ByteArray,
    ) : MediaRepository {
        var requests = 0

        @Suppress("MaxLineLength")
        override suspend fun inspectOriginal(fileId: String) = OriginalMetadata(ByteCount(bytes.size.toLong()), "application/pdf", true)

        override suspend fun job(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun retryJob(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun openContent(
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): MediaContentResult {
            requests++
            val response =
                Response
                    .Builder()
                    .request(Request.Builder().url("https://api.example/content").build())
                    .protocol(Protocol.HTTP_1_1)
                    .code(200)
                    .message("OK")
                    .body(bytes.toResponseBody())
                    .build()
            return MediaContentResult.Ready(ReadyMediaContent(response))
        }
    }
}
