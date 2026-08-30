package com.kurastorage.feature.media

import com.kurastorage.core.data.media.MediaContentResult
import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.data.media.NetworkQualityContextResolver
import com.kurastorage.core.data.media.NetworkTransport
import com.kurastorage.core.data.media.NetworkTransportSource
import com.kurastorage.core.data.media.QualityPreferenceStore
import com.kurastorage.core.data.media.RegisteredWifiSource
import com.kurastorage.core.data.media.TransferConfirmationPolicy
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaJobStatus
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.OriginalMetadata
import com.kurastorage.core.model.media.QualityPreferences
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class MediaViewerControllerTest {
    @Test
    fun `original requires matching approval and never prefetches content`() =
        runTest {
            val repository = FakeRepository()
            val controller = controller(repository, ConnectionRoute.LOCAL_DIRECT, backgroundScope)

            controller.start("file", 4, MediaKind.IMAGE)

            assertTrue(controller.state.value?.loadState is MediaLoadState.ConfirmingTransfer)
            assertNull(controller.requestTicket())
            assertEquals(0, repository.contentRequests)
            controller.confirmOriginal()
            assertEquals(MediaVariant.ORIGINAL, controller.requestTicket()?.source?.variant)
            assertEquals(0, repository.contentRequests)
        }

    @Test
    fun `last quality selection wins and stale completion cannot overwrite`() =
        runTest {
            val repository = FakeRepository()
            val controller = controller(repository, ConnectionRoute.REMOTE_SECURE, backgroundScope)
            controller.start("file", 4, MediaKind.IMAGE)
            val low = controller.requestTicket()!!
            assertEquals(MediaVariant.IMAGE_LOW, low.source.variant)

            controller.selectQuality(MediaQuality.MEDIUM)
            val medium = controller.requestTicket()!!
            controller.contentReady(low)
            assertTrue(controller.state.value?.loadState is MediaLoadState.Loading)
            controller.contentReady(medium)
            val ready = controller.state.value?.loadState as MediaLoadState.Ready
            assertEquals(MediaVariant.IMAGE_MEDIUM, ready.source.variant)
        }

    @Test
    fun `polling respects retry delay and ready retries selected quality without fallback`() =
        runTest {
            val repository =
                FakeRepository().apply {
                    jobs += job(MediaJobStatus.GENERATING, retry = 2)
                    jobs += job(MediaJobStatus.READY, retry = 0)
                }
            val controller = controller(repository, ConnectionRoute.REMOTE_SECURE, backgroundScope)
            controller.start("file", 4, MediaKind.VIDEO)
            val ticket = controller.requestTicket()!!
            controller.contentGenerating(ticket, job(MediaJobStatus.GENERATING, retry = 2))

            advanceTimeBy(1_999)
            runCurrent()
            assertEquals(0, repository.jobRequests)
            advanceTimeBy(1)
            runCurrent()
            assertEquals(1, repository.jobRequests)
            advanceTimeBy(2_000)
            runCurrent()
            assertEquals(2, repository.jobRequests)
            assertTrue(controller.state.value?.loadState is MediaLoadState.Loading)
            assertEquals(MediaVariant.VIDEO_LOW, controller.requestTicket()?.source?.variant)
        }

    @Test
    fun `only retryable failed jobs can be explicitly retried`() =
        runTest {
            val repository =
                FakeRepository().apply {
                    jobs += job(MediaJobStatus.FAILED, retry = 0, retryable = true)
                    retryResult = job(MediaJobStatus.GENERATING, retry = 2)
                }
            val controller = controller(repository, ConnectionRoute.REMOTE_SECURE, backgroundScope)
            controller.start("file", 4, MediaKind.VIDEO)
            controller.contentGenerating(controller.requestTicket()!!, job(MediaJobStatus.GENERATING, retry = 1))
            advanceTimeBy(1_000)
            runCurrent()
            assertTrue(controller.state.value?.canRetryGeneration == true)

            controller.retryGeneration()

            assertEquals(1, repository.retryRequests)
            assertTrue(controller.state.value?.loadState is MediaLoadState.Generating)
            assertEquals(MediaQuality.LOW, controller.state.value?.quality)
        }

    @Test
    fun `closing the screen cancels duplicate polling without cancelling server job`() =
        runTest {
            val repository = FakeRepository()
            val controller = controller(repository, ConnectionRoute.REMOTE_SECURE, backgroundScope)
            controller.start("file", 4, MediaKind.VIDEO)
            val ticket = controller.requestTicket()!!
            val generating = job(MediaJobStatus.GENERATING, retry = 1)
            controller.contentGenerating(ticket, generating)
            controller.contentGenerating(ticket, generating)

            controller.close()
            advanceTimeBy(1_000)
            runCurrent()

            assertEquals(0, repository.jobRequests)
            assertEquals(0, repository.retryRequests)
            assertNull(controller.state.value)
        }

    private fun controller(
        repository: FakeRepository,
        route: ConnectionRoute,
        scope: kotlinx.coroutines.CoroutineScope,
    ) = MediaViewerController(
        repository = repository,
        qualityStore = FakeQualityStore(),
        contextResolver =
            NetworkQualityContextResolver(
                NetworkTransportSource {
                    if (route ==
                        ConnectionRoute.REMOTE_SECURE
                    ) {
                        NetworkTransport.CELLULAR
                    } else {
                        NetworkTransport.WIFI
                    }
                },
                RegisteredWifiSource { false },
            ),
        confirmationPolicy = TransferConfirmationPolicy(repository),
        route = route,
        parentScope = scope,
    )

    private class FakeQualityStore : QualityPreferenceStore {
        override suspend fun read() = QualityPreferences()

        override suspend fun update(
            context: com.kurastorage.core.model.media.NetworkQualityContext,
            quality: MediaQuality,
        ) = Unit
    }

    private class FakeRepository : MediaRepository {
        val jobs = ArrayDeque<MediaJobSnapshot>()
        var jobRequests = 0
        var retryRequests = 0
        var retryResult: MediaJobSnapshot? = null
        var contentRequests = 0

        override suspend fun inspectOriginal(fileId: String) = OriginalMetadata(ByteCount(100), "image/jpeg", true)

        override suspend fun job(jobId: String): MediaJobSnapshot {
            jobRequests++
            return jobs.removeFirst()
        }

        override suspend fun retryJob(jobId: String): MediaJobSnapshot {
            retryRequests++
            return checkNotNull(retryResult)
        }

        override suspend fun openContent(
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): MediaContentResult {
            contentRequests++
            error("controller must not fetch content")
        }
    }

    private companion object {
        fun job(
            status: MediaJobStatus,
            retry: Int,
            retryable: Boolean = status == MediaJobStatus.FAILED,
        ) = MediaJobSnapshot("job", status, null, null, null, null, retry, retryable)
    }
}
