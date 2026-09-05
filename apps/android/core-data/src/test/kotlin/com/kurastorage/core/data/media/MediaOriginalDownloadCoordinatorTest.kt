package com.kurastorage.core.data.media

import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaVariant
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.test.runTest
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.Response
import okhttp3.ResponseBody.Companion.toResponseBody
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.OutputStream

class MediaOriginalDownloadCoordinatorTest {
    @Test
    fun `download always streams original and reports bytes after close`() =
        runTest {
            val repository = FakeRepository("original".encodeToByteArray())
            val target = FakeTarget()

            val outcome = MediaOriginalDownloadCoordinator(MediaContentDownloader(repository)).download("file", target)

            assertEquals(MediaDownloadOutcome.Completed(8), outcome)
            assertEquals(MediaVariant.ORIGINAL, repository.variant)
            assertEquals("original", (target.output as ByteArrayOutputStream).toString(Charsets.UTF_8.name()))
            assertTrue(target.closed)
            assertFalse(target.deleted)
        }

    @Test
    fun `open and copy failures remove incomplete target`() =
        runTest {
            val openFailure = FakeTarget(openable = false)
            val openOutcome =
                MediaOriginalDownloadCoordinator(MediaContentDownloader(FakeRepository()))
                    .download("file", openFailure)
            assertEquals(MediaDownloadOutcome.Failed(false), openOutcome)
            assertTrue(openFailure.deleted)

            val copyFailure = FakeTarget(output = FailingOutputStream())
            val copyOutcome =
                MediaOriginalDownloadCoordinator(MediaContentDownloader(FakeRepository()))
                    .download("file", copyFailure)
            assertEquals(MediaDownloadOutcome.Failed(false), copyOutcome)
            assertTrue(copyFailure.deleted)
        }

    @Test
    fun `failed cleanup reports that an incomplete target may remain`() =
        runTest {
            val target = FakeTarget(openable = false, deleteSucceeds = false)

            val outcome =
                MediaOriginalDownloadCoordinator(MediaContentDownloader(FakeRepository()))
                    .download("file", target)

            assertEquals(MediaDownloadOutcome.Failed(true), outcome)
        }

    @Test
    fun `close failure is not reported as success and removes target`() =
        runTest {
            val target = FakeTarget(output = CloseFailingOutputStream())

            val outcome =
                MediaOriginalDownloadCoordinator(MediaContentDownloader(FakeRepository()))
                    .download("file", target)

            assertEquals(MediaDownloadOutcome.Failed(false), outcome)
            assertTrue(target.deleted)
        }

    @Test
    fun `cancellation removes target and remains cancellation`() =
        runTest {
            val target = FakeTarget()
            val repository = FakeRepository(failure = CancellationException("cancelled"))

            val error =
                runCatching {
                    MediaOriginalDownloadCoordinator(MediaContentDownloader(repository)).download("file", target)
                }.exceptionOrNull()

            assertTrue(error is CancellationException)
            assertTrue(target.deleted)
        }

    private class FakeRepository(
        private val bytes: ByteArray = byteArrayOf(1, 2, 3),
        private val failure: Throwable? = null,
    ) : MediaRepository {
        var variant: MediaVariant? = null

        override suspend fun inspectOriginal(fileId: String) = error("unused")

        override suspend fun job(jobId: String): MediaJobSnapshot = error("unused")

        override suspend fun retryJob(jobId: String): MediaJobSnapshot = error("unused")

        override suspend fun openContent(
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): MediaContentResult {
            this.variant = variant
            failure?.let { throw it }
            return MediaContentResult.Ready(
                ReadyMediaContent(
                    Response
                        .Builder()
                        .request(Request.Builder().url("https://example.test/content").build())
                        .protocol(Protocol.HTTP_1_1)
                        .code(200)
                        .message("OK")
                        .body(bytes.toResponseBody("application/octet-stream".toMediaType()))
                        .build(),
                ),
            )
        }
    }

    private class FakeTarget(
        private val openable: Boolean = true,
        private val deleteSucceeds: Boolean = true,
        val output: OutputStream = ByteArrayOutputStream(),
    ) : MediaDownloadTarget {
        var closed = false
        var deleted = false

        override fun openOutputStream(): OutputStream? =
            if (openable) {
                object : OutputStream() {
                    override fun write(value: Int) = output.write(value)

                    override fun write(
                        bytes: ByteArray,
                        offset: Int,
                        length: Int,
                    ) = output.write(bytes, offset, length)

                    override fun close() {
                        output.close()
                        closed = true
                    }
                }
            } else {
                null
            }

        override fun delete(): Boolean {
            deleted = true
            return deleteSucceeds
        }
    }

    private class FailingOutputStream : OutputStream() {
        override fun write(value: Int) = throw IOException("full")
    }

    private class CloseFailingOutputStream : ByteArrayOutputStream() {
        override fun close() = throw IOException("flush failed")
    }
}
