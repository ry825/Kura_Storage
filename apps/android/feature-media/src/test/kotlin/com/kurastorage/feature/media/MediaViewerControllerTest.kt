@file:Suppress("LongParameterList", "MaxLineLength")

package com.kurastorage.feature.media

import com.kurastorage.core.data.media.MediaContentResult
import com.kurastorage.core.data.media.MediaMetadataResult
import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.data.media.NetworkQualityContextResolver
import com.kurastorage.core.data.media.NetworkTransport
import com.kurastorage.core.data.media.NetworkTransportSource
import com.kurastorage.core.data.media.QualityPreferenceStore
import com.kurastorage.core.data.media.RegisteredWifiSource
import com.kurastorage.core.data.media.TransferConfirmationPolicy
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaJobStatus
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaUiError
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.OriginalMetadata
import com.kurastorage.core.model.media.QualityPreferences
import com.kurastorage.core.model.media.VariantMetadata
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException

@OptIn(ExperimentalCoroutinesApi::class)
class MediaViewerControllerTest {
    @Test
    fun `photo starts with the configured quality for every network context`() =
        runTest {
            val preferences =
                QualityPreferences(
                    localDirect = MediaQuality.ORIGINAL,
                    registeredRemoteWifi = MediaQuality.MEDIUM,
                    unregisteredRemoteWifi = MediaQuality.ORIGINAL,
                    remoteMobile = MediaQuality.LOW,
                )
            val cases =
                listOf(
                    NetworkCase(
                        ConnectionRoute.LOCAL_DIRECT,
                        NetworkTransport.WIFI,
                        false,
                        NetworkQualityContext.LOCAL_DIRECT,
                        MediaVariant.ORIGINAL,
                    ),
                    NetworkCase(
                        ConnectionRoute.REMOTE_SECURE,
                        NetworkTransport.WIFI,
                        true,
                        NetworkQualityContext.REGISTERED_REMOTE_WIFI,
                        MediaVariant.IMAGE_MEDIUM,
                    ),
                    NetworkCase(
                        ConnectionRoute.REMOTE_SECURE,
                        NetworkTransport.WIFI,
                        false,
                        NetworkQualityContext.UNREGISTERED_REMOTE_WIFI,
                        MediaVariant.ORIGINAL,
                    ),
                    NetworkCase(
                        ConnectionRoute.REMOTE_SECURE,
                        NetworkTransport.CELLULAR,
                        false,
                        NetworkQualityContext.REMOTE_MOBILE,
                        MediaVariant.IMAGE_LOW,
                    ),
                )

            cases.forEachIndexed { index, case ->
                val controller =
                    controller(
                        repository = FakeRepository(),
                        route = case.route,
                        scope = backgroundScope,
                        preferences = preferences,
                        network = case,
                    )
                controller.start("photo-$index", 1, MediaKind.IMAGE)

                assertEquals(case.context, controller.state.value?.networkContext)
                assertEquals(case.variant, controller.requestTicket()?.source?.variant)
                assertNull(controller.state.value?.confirmation)
            }
        }

    @Test
    fun `photo original is inspected and automatically starts without confirmation`() =
        runTest {
            val repository = FakeRepository()
            val controller = controller(repository, ConnectionRoute.LOCAL_DIRECT, backgroundScope)

            controller.start("file", 4, MediaKind.IMAGE)

            assertTrue(controller.state.value?.loadState is MediaLoadState.Loading)
            assertNull(controller.state.value?.confirmation)
            assertEquals("100 B", controller.state.value?.originalSizeLabel)
            assertEquals(MediaVariant.ORIGINAL, controller.requestTicket()?.source?.variant)
            assertEquals(0, repository.contentRequests)
        }

    @Test
    fun `video ignores configured derived quality and retains original confirmation`() =
        runTest {
            val repository = FakeRepository().apply { originalSize = 1024L * 1024 }
            val controller =
                controller(
                    repository,
                    ConnectionRoute.REMOTE_SECURE,
                    backgroundScope,
                    preferences = QualityPreferences(remoteMobile = MediaQuality.LOW),
                )

            controller.start("video", 4, MediaKind.VIDEO)

            assertEquals(MediaQuality.ORIGINAL, controller.state.value?.quality)
            assertEquals(MediaVariant.ORIGINAL, controller.state.value?.requestedVariant)
            assertTrue(controller.state.value?.loadState is MediaLoadState.ConfirmingTransfer)
            assertNull(controller.requestTicket())
            controller.confirmOriginal()
            assertEquals(MediaVariant.ORIGINAL, controller.requestTicket()?.source?.variant)
        }

    @Test
    fun `video confirmation is limited to cellular files at least one mebibyte`() =
        runTest {
            listOf(
                1_048_575L to false,
                1_048_576L to true,
            ).forEach { (size, confirmationExpected) ->
                val repository = FakeRepository().apply { originalSize = size }
                val controller = controller(repository, ConnectionRoute.REMOTE_SECURE, backgroundScope)

                controller.start("video-$size", 1, MediaKind.VIDEO)

                assertEquals(confirmationExpected, controller.state.value?.confirmation != null)
                assertEquals(!confirmationExpected, controller.requestTicket() != null)
                assertEquals(0, repository.contentRequests)
            }
        }

    @Test
    fun `wifi ethernet and unknown transports prepare original video without a mobile dialog`() =
        runTest {
            listOf(NetworkTransport.WIFI, NetworkTransport.ETHERNET, NetworkTransport.OTHER_OR_UNKNOWN)
                .forEach { transport ->
                    val repository = FakeRepository().apply { originalSize = 2L * 1024 * 1024 }
                    val controller =
                        controller(
                            repository,
                            ConnectionRoute.REMOTE_SECURE,
                            backgroundScope,
                            network =
                                NetworkCase(
                                    ConnectionRoute.REMOTE_SECURE,
                                    transport,
                                    false,
                                    NetworkQualityContext.UNREGISTERED_REMOTE_WIFI,
                                    MediaVariant.ORIGINAL,
                                ),
                        )

                    controller.start("video-${transport.name}", 1, MediaKind.VIDEO)

                    assertNull(controller.state.value?.confirmation)
                    assertEquals(MediaVariant.ORIGINAL, controller.requestTicket()?.source?.variant)
                }
        }

    @Test
    fun `unknown cellular size requires confirmation and cancel starts no content request`() =
        runTest {
            val repository = FakeRepository().apply { inspectError = KuraStorageException.Network(IOException("offline")) }
            val controller = controller(repository, ConnectionRoute.REMOTE_SECURE, backgroundScope)

            controller.start("video", 1, MediaKind.VIDEO)
            assertEquals(
                "Size unavailable",
                controller.state.value
                    ?.confirmation
                    ?.formattedSize,
            )

            controller.cancelOriginalConfirmation()

            assertNull(controller.requestTicket())
            assertEquals(0, repository.contentRequests)
        }

    @Test
    fun `mobile approval is discarded when file identity changes`() =
        runTest {
            val repository = FakeRepository().apply { originalSize = 2L * 1024 * 1024 }
            val controller = controller(repository, ConnectionRoute.REMOTE_SECURE, backgroundScope)
            controller.start("first", 1, MediaKind.VIDEO)
            controller.confirmOriginal()
            assertEquals("first", controller.requestTicket()?.source?.fileId)

            controller.start("second", 2, MediaKind.VIDEO)

            assertNull(controller.requestTicket())
            assertEquals(
                "second",
                controller.state.value
                    ?.confirmation
                    ?.fileId,
            )
            assertEquals(
                2L,
                controller.state.value
                    ?.confirmation
                    ?.fileVersion,
            )
        }

    @Test
    fun `transport change reevaluates pending video without trusting wifi registration`() =
        runTest {
            val repository = FakeRepository().apply { originalSize = 2L * 1024 * 1024 }
            val transport = MutableTransport(NetworkTransport.CELLULAR)
            val controller =
                controller(
                    repository,
                    ConnectionRoute.REMOTE_SECURE,
                    backgroundScope,
                    transportSource = transport,
                )
            controller.start("video", 1, MediaKind.VIDEO)
            assertTrue(controller.state.value?.loadState is MediaLoadState.ConfirmingTransfer)

            transport.update(NetworkTransport.ETHERNET)
            runCurrent()

            assertNull(controller.state.value?.confirmation)
            assertEquals(NetworkTransport.ETHERNET, controller.state.value?.transport)
            assertEquals(MediaVariant.ORIGINAL, controller.requestTicket()?.source?.variant)
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
    fun `displayed source and size change atomically only when the selected variant is ready`() =
        runTest {
            val repository =
                FakeRepository().apply {
                    variantSizes[MediaVariant.IMAGE_LOW] = 1_024
                    variantSizes[MediaVariant.IMAGE_MEDIUM] = 2_048
                }
            val controller = controller(repository, ConnectionRoute.REMOTE_SECURE, backgroundScope)
            controller.start("file", 4, MediaKind.IMAGE)
            val low = controller.requestTicket()!!
            controller.contentReady(low)
            assertEquals(
                MediaVariant.IMAGE_LOW,
                controller.state.value
                    ?.displayedSource
                    ?.variant,
            )
            assertEquals("1 KB", controller.state.value?.displayedSizeLabel)

            controller.selectQuality(MediaQuality.MEDIUM)
            val medium = controller.requestTicket()!!
            assertEquals(
                MediaVariant.IMAGE_LOW,
                controller.state.value
                    ?.displayedSource
                    ?.variant,
            )
            assertEquals("1 KB", controller.state.value?.displayedSizeLabel)
            assertEquals(
                2_048L,
                controller.state.value
                    ?.requestedMetadata
                    ?.size
                    ?.value,
            )

            controller.contentReady(low)
            assertEquals(
                MediaVariant.IMAGE_LOW,
                controller.state.value
                    ?.displayedSource
                    ?.variant,
            )
            controller.contentReady(medium)
            assertEquals(
                MediaVariant.IMAGE_MEDIUM,
                controller.state.value
                    ?.displayedSource
                    ?.variant,
            )
            assertEquals("2 KB", controller.state.value?.displayedSizeLabel)
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
            controller.start("file", 4, MediaKind.IMAGE)
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
            assertEquals(MediaVariant.IMAGE_LOW, controller.requestTicket()?.source?.variant)
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
            controller.start("file", 4, MediaKind.IMAGE)
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
            controller.start("file", 4, MediaKind.IMAGE)
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

    @Test
    fun `audio confirmation can be cancelled and stale tickets cannot change state`() =
        runTest {
            val repository = FakeRepository()
            val controller = controller(repository, ConnectionRoute.REMOTE_SECURE, backgroundScope)
            controller.start("audio", 1, MediaKind.AUDIO)
            assertEquals(MediaQuality.ORIGINAL, controller.state.value?.quality)
            controller.cancelOriginalConfirmation()
            assertTrue(controller.state.value?.loadState is MediaLoadState.Idle)
            assertNull(controller.requestTicket())

            controller.selectQuality(MediaQuality.ORIGINAL)
            controller.confirmOriginal()
            val stale = checkNotNull(controller.requestTicket())
            controller.start("replacement", 2, MediaKind.IMAGE)
            controller.contentFailed(stale, MediaUiError.PERMISSION_DENIED)
            assertEquals("replacement", controller.state.value?.fileId)
            assertTrue(controller.state.value?.loadState is MediaLoadState.Loading)
        }

    @Test
    fun `polling terminal and network failures remain explicit`() =
        runTest {
            val failedRepository = FakeRepository().apply { jobs += job(MediaJobStatus.FAILED, 0, false) }
            val failed = controller(failedRepository, ConnectionRoute.REMOTE_SECURE, backgroundScope)
            failed.start("file", 1, MediaKind.IMAGE)
            failed.contentGenerating(failed.requestTicket()!!, job(MediaJobStatus.GENERATING, 1))
            advanceTimeBy(1_000)
            runCurrent()
            assertEquals(MediaUiError.GENERATION_FAILED, (failed.state.value?.loadState as MediaLoadState.Failed).error)
            assertEquals(false, failed.state.value?.canRetryGeneration)

            val disconnectedRepository =
                FakeRepository().apply {
                    jobError = KuraStorageException.Network(IOException("offline"))
                }
            val disconnected = controller(disconnectedRepository, ConnectionRoute.REMOTE_SECURE, backgroundScope)
            disconnected.start("file", 1, MediaKind.IMAGE)
            disconnected.contentGenerating(disconnected.requestTicket()!!, job(MediaJobStatus.GENERATING, 1))
            advanceTimeBy(1_000)
            runCurrent()
            assertEquals(
                MediaUiError.DISCONNECTED,
                (disconnected.state.value?.loadState as MediaLoadState.Failed).error,
            )
        }

    @Test
    fun `retry handles ready terminal result and API failure`() =
        runTest {
            val repository =
                FakeRepository().apply {
                    jobs += job(MediaJobStatus.FAILED, 0, true)
                    retryResult = job(MediaJobStatus.READY, 0)
                }
            val controller = controller(repository, ConnectionRoute.REMOTE_SECURE, backgroundScope)
            controller.start("file", 1, MediaKind.IMAGE)
            controller.contentGenerating(controller.requestTicket()!!, job(MediaJobStatus.GENERATING, 1))
            advanceTimeBy(1_000)
            runCurrent()
            controller.retryGeneration()
            assertTrue(controller.state.value?.loadState is MediaLoadState.Loading)

            repository.jobs += job(MediaJobStatus.FAILED, 0, true)
            controller.contentGenerating(controller.requestTicket()!!, job(MediaJobStatus.GENERATING, 1))
            advanceTimeBy(1_000)
            runCurrent()
            repository.retryError = apiError(403)
            controller.retryGeneration()
            assertEquals(
                MediaUiError.PERMISSION_DENIED,
                (controller.state.value?.loadState as MediaLoadState.Failed).error,
            )
        }

    @Test
    fun `original inspection maps every stable HTTP failure without content fallback`() =
        runTest {
            val expected =
                mapOf(
                    401 to MediaUiError.AUTHENTICATION_REQUIRED,
                    403 to MediaUiError.PERMISSION_DENIED,
                    404 to MediaUiError.NOT_FOUND,
                    409 to MediaUiError.FILE_CHANGED,
                    416 to MediaUiError.RANGE_INVALID,
                )
            expected.forEach { (status, uiError) ->
                val repository = FakeRepository().apply { inspectError = apiError(status) }
                val controller = controller(repository, ConnectionRoute.LOCAL_DIRECT, backgroundScope)
                controller.start("file-$status", 1, MediaKind.IMAGE)
                assertEquals(uiError, (controller.state.value?.loadState as MediaLoadState.Failed).error)
                assertEquals(0, repository.contentRequests)
            }
            val unavailable = FakeRepository().apply { inspectError = apiError(500) }
            val controller = controller(unavailable, ConnectionRoute.LOCAL_DIRECT, backgroundScope)
            controller.start("file-500", 1, MediaKind.IMAGE)
            assertEquals(
                "Size unavailable",
                controller.state.value
                    ?.originalSizeLabel,
            )
        }

    private fun controller(
        repository: FakeRepository,
        route: ConnectionRoute,
        scope: kotlinx.coroutines.CoroutineScope,
        preferences: QualityPreferences = QualityPreferences(),
        network: NetworkCase? = null,
        transportSource: NetworkTransportSource? = null,
    ) = MediaViewerController(
        repository = repository,
        qualityStore = FakeQualityStore(preferences),
        contextResolver =
            NetworkQualityContextResolver(
                transportSource ?: NetworkTransportSource {
                    network?.transport
                        ?: when (route) {
                            ConnectionRoute.REMOTE_SECURE -> NetworkTransport.CELLULAR
                            ConnectionRoute.LOCAL_DIRECT -> NetworkTransport.WIFI
                        }
                },
                RegisteredWifiSource { network?.registeredWifi ?: false },
            ),
        confirmationPolicy = TransferConfirmationPolicy(repository),
        route = route,
        parentScope = scope,
    )

    private class FakeQualityStore(
        private val preferences: QualityPreferences,
    ) : QualityPreferenceStore {
        override suspend fun read() = preferences

        override suspend fun update(
            context: com.kurastorage.core.model.media.NetworkQualityContext,
            quality: MediaQuality,
        ) = Unit
    }

    private class MutableTransport(
        initial: NetworkTransport,
    ) : NetworkTransportSource {
        private val values = MutableStateFlow(initial)

        override fun activeTransport(): NetworkTransport = values.value

        override fun observe(): Flow<NetworkTransport> = values

        fun update(value: NetworkTransport) {
            values.value = value
        }
    }

    private data class NetworkCase(
        val route: ConnectionRoute,
        val transport: NetworkTransport,
        val registeredWifi: Boolean,
        val context: com.kurastorage.core.model.media.NetworkQualityContext,
        val variant: MediaVariant,
    )

    private class FakeRepository : MediaRepository {
        val jobs = ArrayDeque<MediaJobSnapshot>()
        var jobRequests = 0
        var retryRequests = 0
        var retryResult: MediaJobSnapshot? = null
        var retryError: KuraStorageException? = null
        var jobError: KuraStorageException? = null
        var inspectError: KuraStorageException? = null
        var contentRequests = 0
        var originalSize = 100L
        val variantSizes = mutableMapOf<MediaVariant, Long>()

        override suspend fun inspectOriginal(fileId: String): OriginalMetadata {
            inspectError?.let { throw it }
            return OriginalMetadata(ByteCount(originalSize), "image/jpeg", true)
        }

        override suspend fun inspectVariant(
            fileId: String,
            variant: MediaVariant,
        ): MediaMetadataResult {
            val original = inspectOriginal(fileId)
            return MediaMetadataResult.Ready(
                VariantMetadata(
                    variant,
                    ByteCount(variantSizes[variant] ?: original.size.value),
                    original.mimeType,
                    original.acceptsRanges,
                ),
            )
        }

        override suspend fun job(jobId: String): MediaJobSnapshot {
            jobRequests++
            jobError?.let { throw it }
            return jobs.removeFirst()
        }

        override suspend fun retryJob(jobId: String): MediaJobSnapshot {
            retryRequests++
            retryError?.let { throw it }
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
        fun apiError(status: Int) = KuraStorageException.Api(ApiError(ErrorCode.UNKNOWN, "request", status))

        fun job(
            status: MediaJobStatus,
            retry: Int,
            retryable: Boolean = status == MediaJobStatus.FAILED,
        ) = MediaJobSnapshot("job", status, null, null, null, null, retry, retryable)
    }
}
