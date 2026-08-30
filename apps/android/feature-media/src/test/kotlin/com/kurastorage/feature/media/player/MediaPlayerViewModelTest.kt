package com.kurastorage.feature.media.player

import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.media.MediaContentResult
import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.data.media.NetworkQualityContextResolver
import com.kurastorage.core.data.media.NetworkTransport
import com.kurastorage.core.data.media.NetworkTransportSource
import com.kurastorage.core.data.media.QualityPreferenceStore
import com.kurastorage.core.data.media.RegisteredWifiSource
import com.kurastorage.core.data.media.TransferConfirmationPolicy
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.FilePage
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.OriginalMetadata
import com.kurastorage.core.model.media.PlaybackRate
import com.kurastorage.core.model.media.QualityPreferences
import com.kurastorage.core.model.media.ReadyMediaSource
import com.kurastorage.feature.media.MediaViewerController
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class MediaPlayerViewModelTest {
    private val dispatcher = UnconfinedTestDispatcher()

    @Before
    fun setUp() = Dispatchers.setMain(dispatcher)

    @After
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `video starts selected mobile quality and preserves position rate and play state when quality changes`() =
        runTest(dispatcher) {
            val repository = FakeMediaRepository("video/mp4")
            val controller = controller(repository, backgroundScope)
            val engine = FakeEngine()
            val viewModel =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.VIDEO,
                    FakeFiles(file("video/mp4")),
                    controller,
                    MediaReadinessProbe { MediaReadiness.Ready },
                )

            viewModel.attachEngine(engine)
            assertEquals(MediaVariant.VIDEO_LOW, engine.preparedSource?.variant)
            engine.emit(
                PlayerSnapshot(
                    positionMs = 12_000,
                    durationMs = 60_000,
                    seekable = true,
                    playWhenReady = true,
                    rate = PlaybackRate(1.5f),
                    phase = PlayerPhase.READY,
                ),
            )
            viewModel.selectQuality(MediaQuality.MEDIUM)

            assertEquals(MediaVariant.VIDEO_MEDIUM, engine.preparedSource?.variant)
            assertEquals(12_000, engine.preparedPosition)
            assertEquals(1.5f, engine.preparedRate.value)
            assertTrue(engine.preparedPlayWhenReady)
        }

    @Test
    fun `audio requires original transfer confirmation before preparing content`() =
        runTest(dispatcher) {
            val repository = FakeMediaRepository("audio/mpeg")
            val viewModel =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.AUDIO,
                    FakeFiles(file("audio/mpeg")),
                    controller(repository, backgroundScope),
                    MediaReadinessProbe { MediaReadiness.Ready },
                )
            val engine = FakeEngine()

            viewModel.attachEngine(engine)
            assertNull(engine.preparedSource)
            assertEquals(
                "4.0 KiB",
                viewModel.state.value.media
                    ?.confirmation
                    ?.formattedSize,
            )
            viewModel.confirmOriginal()

            assertEquals(MediaVariant.ORIGINAL, engine.preparedSource?.variant)
        }

    @Test
    fun `cancelling original before initial video is ready returns to the previous quality`() =
        runTest(dispatcher) {
            val repository = FakeMediaRepository("video/mp4")
            val viewModel =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.VIDEO,
                    FakeFiles(file("video/mp4")),
                    controller(repository, backgroundScope),
                    MediaReadinessProbe { MediaReadiness.Ready },
                )
            val engine = FakeEngine()
            viewModel.attachEngine(engine)

            viewModel.selectQuality(MediaQuality.ORIGINAL)
            assertEquals(
                MediaQuality.ORIGINAL,
                viewModel.state.value.media
                    ?.quality,
            )
            viewModel.cancelOriginal()

            assertEquals(
                MediaQuality.LOW,
                viewModel.state.value.media
                    ?.quality,
            )
            assertEquals(MediaVariant.VIDEO_LOW, engine.preparedSource?.variant)
        }

    @Test
    fun `cancelling an initially configured original video falls back to medium`() =
        runTest(dispatcher) {
            val repository = FakeMediaRepository("video/mp4")
            val viewModel =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.VIDEO,
                    FakeFiles(file("video/mp4")),
                    localController(repository, backgroundScope),
                    MediaReadinessProbe { MediaReadiness.Ready },
                )
            val engine = FakeEngine()
            viewModel.attachEngine(engine)

            assertEquals(
                MediaQuality.ORIGINAL,
                viewModel.state.value.media
                    ?.quality,
            )
            assertNull(engine.preparedSource)

            viewModel.cancelOriginal()

            assertEquals(
                MediaQuality.MEDIUM,
                viewModel.state.value.media
                    ?.quality,
            )
            assertEquals(MediaVariant.VIDEO_MEDIUM, engine.preparedSource?.variant)
        }

    @Test
    fun `detaching for rotation preserves position rate and playing state for the new engine`() =
        runTest(dispatcher) {
            val repository = FakeMediaRepository("video/mp4")
            val viewModel =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.VIDEO,
                    FakeFiles(file("video/mp4")),
                    controller(repository, backgroundScope),
                    MediaReadinessProbe { MediaReadiness.Ready },
                )
            val first = FakeEngine()
            viewModel.attachEngine(first)
            first.emit(
                PlayerSnapshot(
                    positionMs = 23_000,
                    durationMs = 60_000,
                    seekable = true,
                    playWhenReady = true,
                    rate = PlaybackRate(1.75f),
                    phase = PlayerPhase.READY,
                ),
            )

            viewModel.detachEngine()
            val replacement = FakeEngine()
            viewModel.attachEngine(replacement)

            assertEquals(23_000, replacement.preparedPosition)
            assertEquals(1.75f, replacement.preparedRate.value)
            assertTrue(replacement.preparedPlayWhenReady)
        }

    @Test
    fun `replacing engine while readiness is pending never prepares the closed engine`() =
        runTest(dispatcher) {
            val repository = FakeMediaRepository("video/mp4")
            val readiness = CompletableDeferred<MediaReadiness>()
            val viewModel =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.VIDEO,
                    FakeFiles(file("video/mp4")),
                    controller(repository, backgroundScope),
                    MediaReadinessProbe { readiness.await() },
                )
            val first = FakeEngine()
            viewModel.attachEngine(first)

            viewModel.detachEngine(first)
            first.close()
            val replacement = FakeEngine()
            viewModel.attachEngine(replacement)
            readiness.complete(MediaReadiness.Ready)

            assertNull(first.preparedSource)
            assertEquals(MediaVariant.VIDEO_LOW, replacement.preparedSource?.variant)
        }

    @Test
    fun `generating video keeps player source unset and exposes the server job`() =
        runTest(dispatcher) {
            val repository = FakeMediaRepository("video/mp4")
            val generating =
                MediaJobSnapshot(
                    "job",
                    com.kurastorage.core.model.media.MediaJobStatus.GENERATING,
                    40,
                    null,
                    null,
                    2,
                    3,
                    false,
                )
            val viewModel =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.VIDEO,
                    FakeFiles(file("video/mp4")),
                    controller(repository, backgroundScope),
                    MediaReadinessProbe { MediaReadiness.Generating(generating) },
                )
            val engine = FakeEngine()

            viewModel.attachEngine(engine)

            assertNull(engine.preparedSource)
            assertEquals(
                generating,
                (
                    viewModel.state.value.media
                        ?.loadState as MediaLoadState.Generating
                ).job,
            )
        }

    @Test
    fun `codec failure stops playback and maps to unsupported without automatic retry`() =
        runTest(dispatcher) {
            val repository = FakeMediaRepository("video/mp4")
            val viewModel =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.VIDEO,
                    FakeFiles(file("video/mp4")),
                    controller(repository, backgroundScope),
                    MediaReadinessProbe { MediaReadiness.Ready },
                )
            val engine = FakeEngine()
            viewModel.attachEngine(engine)

            engine.emit(PlayerSnapshot(phase = PlayerPhase.FAILED, error = PlayerFailure.UNSUPPORTED_CODEC))

            assertEquals(
                com.kurastorage.core.model.media.MediaUiError.UNSUPPORTED,
                (
                    viewModel.state.value.media
                        ?.loadState as MediaLoadState.Failed
                ).error,
            )
        }

    @Test
    fun `player commands background pause and reconnect preserve bounded state`() =
        runTest(dispatcher) {
            val repository = FakeMediaRepository("video/mp4")
            val viewModel =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.VIDEO,
                    FakeFiles(file("video/mp4")),
                    controller(repository, backgroundScope),
                    MediaReadinessProbe { MediaReadiness.Ready },
                )
            val engine = FakeEngine()
            viewModel.attachEngine(engine)
            engine.emit(
                PlayerSnapshot(positionMs = 5_000, durationMs = 20_000, seekable = true, phase = PlayerPhase.READY),
            )
            viewModel.play()
            viewModel.seekTo(8_000)
            viewModel.skipBack(3_000)
            viewModel.skipForward(10_000)
            viewModel.setRate(PlaybackRate(2f))
            assertEquals(15_000, engine.snapshot.positionMs)
            assertEquals(2f, engine.snapshot.rate.value)
            viewModel.onAppBackgrounded()
            assertTrue(!engine.snapshot.playWhenReady)

            viewModel.retryPlayback()
            assertEquals(MediaVariant.VIDEO_LOW, engine.preparedSource?.variant)
            viewModel.detachEngine(FakeEngine())
            viewModel.pause()
        }

    @Test
    fun `readiness and player failures map to explicit UI errors`() =
        runTest(dispatcher) {
            val repository = FakeMediaRepository("video/mp4")
            val disconnected =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.VIDEO,
                    FakeFiles(file("video/mp4")),
                    controller(repository, backgroundScope),
                    MediaReadinessProbe { error("offline") },
                )
            disconnected.attachEngine(FakeEngine())
            assertEquals(
                com.kurastorage.core.model.media.MediaUiError.DISCONNECTED,
                (
                    disconnected.state.value.media
                        ?.loadState as MediaLoadState.Failed
                ).error,
            )

            PlayerFailure.entries.forEach { failure ->
                val viewModel =
                    MediaPlayerViewModel(
                        FILE_ID,
                        MediaKind.VIDEO,
                        FakeFiles(file("video/mp4")),
                        controller(repository, backgroundScope),
                        MediaReadinessProbe { MediaReadiness.Ready },
                    )
                val engine = FakeEngine()
                viewModel.attachEngine(engine)
                engine.emit(PlayerSnapshot(phase = PlayerPhase.FAILED, error = failure))
                assertTrue(
                    viewModel.state.value.media
                        ?.loadState is MediaLoadState.Failed,
                )
            }
        }

    @Test
    fun `invalid details and mismatched kind fail closed without preparing`() =
        runTest(dispatcher) {
            val repository = FakeMediaRepository("video/mp4")
            val wrongKind =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.AUDIO,
                    FakeFiles(file("video/mp4")),
                    controller(repository, backgroundScope),
                    MediaReadinessProbe { MediaReadiness.Ready },
                )
            val wrongEngine = FakeEngine()
            wrongKind.attachEngine(wrongEngine)
            assertEquals(com.kurastorage.core.model.media.MediaUiError.UNSUPPORTED, wrongKind.state.value.error)
            assertNull(wrongEngine.preparedSource)

            val missing =
                MediaPlayerViewModel(
                    FILE_ID,
                    MediaKind.VIDEO,
                    FailingFiles,
                    controller(repository, backgroundScope),
                    MediaReadinessProbe { MediaReadiness.Ready },
                )
            missing.attachEngine(FakeEngine())
            assertEquals(com.kurastorage.core.model.media.MediaUiError.UNKNOWN, missing.state.value.error)
        }

    private fun controller(
        repository: MediaRepository,
        scope: kotlinx.coroutines.CoroutineScope,
    ) = MediaViewerController(
        repository,
        FakeQualityStore(),
        NetworkQualityContextResolver(
            NetworkTransportSource { NetworkTransport.CELLULAR },
            RegisteredWifiSource { false },
        ),
        TransferConfirmationPolicy(repository),
        ConnectionRoute.REMOTE_SECURE,
        scope,
    )

    private fun localController(
        repository: MediaRepository,
        scope: kotlinx.coroutines.CoroutineScope,
    ) = MediaViewerController(
        repository,
        FakeQualityStore(),
        NetworkQualityContextResolver(
            NetworkTransportSource { NetworkTransport.WIFI },
            RegisteredWifiSource { false },
        ),
        TransferConfirmationPolicy(repository),
        ConnectionRoute.LOCAL_DIRECT,
        scope,
    )

    private class FakeEngine : ObservablePlayerEngine {
        private val mutableStates = MutableStateFlow(PlayerSnapshot())
        override val states: StateFlow<PlayerSnapshot> = mutableStates
        override val snapshot: PlayerSnapshot get() = mutableStates.value
        var preparedSource: ReadyMediaSource? = null
        var preparedPosition: Long = 0
        var preparedRate = PlaybackRate(1f)
        var preparedPlayWhenReady = false
        private var closed = false

        override fun prepare(
            source: ReadyMediaSource,
            positionMs: Long,
            rate: PlaybackRate,
            playWhenReady: Boolean,
        ) {
            check(!closed) { "Player is closed" }
            preparedSource = source
            preparedPosition = positionMs
            preparedRate = rate
            preparedPlayWhenReady = playWhenReady
        }

        fun emit(value: PlayerSnapshot) {
            mutableStates.value = value
        }

        override fun play() = emit(snapshot.copy(playWhenReady = true))

        override fun pause() = emit(snapshot.copy(playWhenReady = false))

        override fun seekTo(positionMs: Long) = emit(snapshot.copy(positionMs = positionMs))

        override fun setRate(rate: PlaybackRate) = emit(snapshot.copy(rate = rate))

        override fun close() {
            closed = true
        }
    }

    private class FakeMediaRepository(
        private val mime: String,
    ) : MediaRepository {
        override suspend fun inspectOriginal(fileId: String) = OriginalMetadata(ByteCount(4096), mime, true)

        override suspend fun job(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun retryJob(jobId: String): MediaJobSnapshot = error("not used")

        override suspend fun openContent(
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): MediaContentResult = error("replaced by readiness probe")
    }

    private class FakeQualityStore : QualityPreferenceStore {
        override suspend fun read() = QualityPreferences()

        override suspend fun update(
            context: com.kurastorage.core.model.media.NetworkQualityContext,
            quality: MediaQuality,
        ) = Unit
    }

    private class FakeFiles(
        private val file: FileEntry,
    ) : FileRepository {
        override suspend fun detail(fileId: String) = file

        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ) = FilePage(parentId, listOf(file), page, pageSize, 1)

        override suspend fun createFolder(
            parentId: String?,
            name: String,
        ): FileEntry = error("not used")

        override suspend fun rename(
            fileId: String,
            name: String,
        ): FileEntry = error("not used")

        override suspend fun move(
            fileId: String,
            targetParentId: String,
        ): FileEntry = error("not used")

        override suspend fun trash(fileId: String): FileEntry = error("not used")

        override suspend fun listTrash(
            page: Int,
            pageSize: Int,
        ): FilePage = error("not used")

        override suspend fun restore(fileId: String): FileEntry = error("not used")
    }

    private object FailingFiles : FileRepository {
        override suspend fun detail(fileId: String): FileEntry = error("missing")

        override suspend fun list(
            parentId: String?,
            page: Int,
            pageSize: Int,
        ): FilePage = error("not used")

        override suspend fun createFolder(
            parentId: String?,
            name: String,
        ): FileEntry = error("not used")

        override suspend fun rename(
            fileId: String,
            name: String,
        ): FileEntry = error("not used")

        override suspend fun move(
            fileId: String,
            targetParentId: String,
        ): FileEntry = error("not used")

        override suspend fun trash(fileId: String): FileEntry = error("not used")

        override suspend fun listTrash(
            page: Int,
            pageSize: Int,
        ): FilePage = error("not used")

        override suspend fun restore(fileId: String): FileEntry = error("not used")
    }

    private fun file(mime: String) =
        FileEntry(
            FILE_ID,
            null,
            "media",
            FileEntryType.FILE,
            mime,
            4096,
            FileEntryStatus.ACTIVE,
            1,
            null,
            Instant.EPOCH,
            Instant.EPOCH,
        )

    private companion object {
        const val FILE_ID = "11111111-1111-1111-1111-111111111111"
    }
}
