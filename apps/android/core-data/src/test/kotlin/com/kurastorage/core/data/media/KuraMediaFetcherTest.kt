package com.kurastorage.core.data.media

import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaJobStatus
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.OriginalMetadata
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.Response
import okhttp3.ResponseBody.Companion.toResponseBody
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

@OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
class KuraMediaFetcherTest {
    @Test
    fun `cache key is session file version and variant only`() {
        val first = KuraMediaImage("session", "file", 7, MediaVariant.THUMBNAIL)
        val renamed = KuraMediaImage("session", "file", 7, MediaVariant.THUMBNAIL)

        assertEquals("session:file:7:thumbnail", first.cacheKey)
        assertEquals(first.cacheKey, renamed.cacheKey)
    }

    @Test
    fun `generating response is exposed without original fallback`() =
        runTest {
            val repository = FakeRepository(generating = true)
            val fetcher =
                KuraMediaFetcher(
                    KuraMediaImage("session", "file", 1, MediaVariant.THUMBNAIL),
                    repository,
                    Semaphore(8),
                )

            val error = runCatching { fetcher.fetch() }.exceptionOrNull()

            assertTrue(error is MediaGeneratingException)
            assertEquals(listOf(MediaVariant.THUMBNAIL), repository.variants)
        }

    @Test
    fun `shared semaphore bounds visible thumbnail requests to eight`() =
        runTest {
            val gate = CompletableDeferred<Unit>()
            val repository = ConcurrentRepository(gate)
            val permits = Semaphore(8)
            val requests =
                (1..9).map { index ->
                    async {
                        KuraMediaFetcher(
                            KuraMediaImage("session", "file-$index", 1, MediaVariant.THUMBNAIL),
                            repository,
                            permits,
                        ).fetch()
                    }
                }

            runCurrent()
            assertEquals(8, repository.maximumConcurrent)
            assertEquals(8, repository.started)
            gate.complete(Unit)
            requests.forEach { request ->
                val result = request.await() as coil3.fetch.SourceFetchResult
                result.source.close()
            }
            assertEquals(9, repository.started)
        }

    private class FakeRepository(
        private val generating: Boolean,
    ) : MediaRepository {
        val variants = mutableListOf<MediaVariant>()

        override suspend fun inspectOriginal(fileId: String) = OriginalMetadata(ByteCount(1), "image/webp", true)

        override suspend fun job(jobId: String) = job()

        override suspend fun retryJob(jobId: String) = job()

        override suspend fun openContent(
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): MediaContentResult {
            variants += variant
            if (generating) return MediaContentResult.Generating(job())
            val response =
                Response
                    .Builder()
                    .request(Request.Builder().url("https://api.example/content").build())
                    .protocol(Protocol.HTTP_1_1)
                    .code(200)
                    .message("OK")
                    .body("image".toResponseBody())
                    .build()
            return MediaContentResult.Ready(ReadyMediaContent(response))
        }

        private fun job() = MediaJobSnapshot("job", MediaJobStatus.GENERATING, null, null, null, null, 2, false)
    }

    private class ConcurrentRepository(
        private val gate: CompletableDeferred<Unit>,
    ) : MediaRepository {
        var started = 0
        var concurrent = 0
        var maximumConcurrent = 0

        override suspend fun inspectOriginal(fileId: String) = OriginalMetadata(ByteCount(1), "image/webp", true)

        override suspend fun job(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun retryJob(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun openContent(
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): MediaContentResult {
            started++
            concurrent++
            maximumConcurrent = maxOf(maximumConcurrent, concurrent)
            gate.await()
            concurrent--
            val response =
                Response
                    .Builder()
                    .request(Request.Builder().url("https://api.example/content").build())
                    .protocol(Protocol.HTTP_1_1)
                    .code(200)
                    .message("OK")
                    .body("image".toResponseBody())
                    .build()
            return MediaContentResult.Ready(ReadyMediaContent(response))
        }
    }
}
