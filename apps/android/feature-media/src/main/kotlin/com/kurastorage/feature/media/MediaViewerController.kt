package com.kurastorage.feature.media

import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.data.media.NetworkQualityContextResolver
import com.kurastorage.core.data.media.QualityPreferenceStore
import com.kurastorage.core.data.media.TransferConfirmationPolicy
import com.kurastorage.core.data.media.TransferConfirmationPrompt
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.core.model.media.MediaJobStatus
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaUiError
import com.kurastorage.core.model.media.MediaVariantResolver
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.ReadyMediaSource
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlin.coroutines.coroutineContext

data class MediaViewerState(
    val fileId: String,
    val fileVersion: Long,
    val kind: MediaKind,
    val quality: MediaQuality,
    val networkContext: NetworkQualityContext,
    val loadState: MediaLoadState,
    val confirmation: TransferConfirmationPrompt? = null,
)

data class MediaRequestTicket(
    internal val generation: Long,
    val source: ReadyMediaSource,
)

@Suppress("TooManyFunctions")
class MediaViewerController(
    private val repository: MediaRepository,
    private val qualityStore: QualityPreferenceStore,
    private val contextResolver: NetworkQualityContextResolver,
    private val confirmationPolicy: TransferConfirmationPolicy,
    private val route: ConnectionRoute,
    parentScope: CoroutineScope,
) : AutoCloseable {
    private val controllerJob = SupervisorJob(parentScope.coroutineContext[Job])
    private val scope = CoroutineScope(parentScope.coroutineContext + controllerJob)
    private val mutableState = MutableStateFlow<MediaViewerState?>(null)
    private var generation = 0L
    private var approvedPrompt: TransferConfirmationPrompt? = null
    private var pollingJob: Job? = null
    private var activeJob: MediaJobSnapshot? = null
    private var retrying = false

    val state: StateFlow<MediaViewerState?> = mutableState.asStateFlow()

    suspend fun start(
        fileId: String,
        fileVersion: Long,
        kind: MediaKind,
    ) {
        require(fileId.isNotBlank())
        require(fileVersion >= 0)
        invalidateRequests()
        approvedPrompt = null
        activeJob = null
        val context = contextResolver.resolve(route)
        val configured = qualityStore.read().qualityFor(context)
        val initialQuality = if (kind == MediaKind.AUDIO || kind == MediaKind.PDF) MediaQuality.ORIGINAL else configured
        mutableState.value =
            MediaViewerState(fileId, fileVersion, kind, initialQuality, context, MediaLoadState.Idle)
        prepareSelectedQuality(generation)
    }

    suspend fun selectQuality(quality: MediaQuality) {
        val current = checkNotNull(mutableState.value) { "Media has not been started" }
        MediaVariantResolver.resolve(current.kind, quality)
        invalidateRequests()
        approvedPrompt = null
        activeJob = null
        mutableState.value = current.copy(quality = quality, loadState = MediaLoadState.Idle, confirmation = null)
        prepareSelectedQuality(generation)
    }

    fun confirmOriginal() {
        val current = checkNotNull(mutableState.value)
        val prompt = checkNotNull(current.confirmation) { "No original transfer is awaiting confirmation" }
        approvedPrompt = prompt
        mutableState.value = current.copy(loadState = MediaLoadState.Loading, confirmation = null)
    }

    @Suppress("ReturnCount")
    fun requestTicket(): MediaRequestTicket? {
        val current = mutableState.value ?: return null
        if (current.loadState !is MediaLoadState.Loading) return null
        val variant = MediaVariantResolver.resolve(current.kind, current.quality)
        if (variant == com.kurastorage.core.model.media.MediaVariant.ORIGINAL) {
            val approved = approvedPrompt ?: return null
            if (!approved.approve().matches(current.fileId, current.fileVersion, variant, approved.size)) return null
        }
        return MediaRequestTicket(generation, current.toSource())
    }

    fun contentReady(ticket: MediaRequestTicket) {
        if (!ticket.isCurrent()) return
        mutableState.value = mutableState.value?.copy(loadState = MediaLoadState.Ready(ticket.source))
    }

    fun contentGenerating(
        ticket: MediaRequestTicket,
        job: MediaJobSnapshot,
    ) {
        if (!ticket.isCurrent() || job.status != MediaJobStatus.GENERATING) return
        pollingJob?.cancel()
        activeJob = job
        mutableState.value = mutableState.value?.copy(loadState = MediaLoadState.Generating(job))
        pollingJob = scope.launch { poll(ticket, job) }
    }

    fun contentFailed(
        ticket: MediaRequestTicket,
        error: MediaUiError,
    ) {
        if (!ticket.isCurrent()) return
        pollingJob?.cancel()
        mutableState.value = mutableState.value?.copy(loadState = MediaLoadState.Failed(error))
    }

    @Suppress("ReturnCount")
    suspend fun retryGeneration() {
        val failed = activeJob?.takeIf { it.status == MediaJobStatus.FAILED && it.retryable } ?: return
        if (retrying) return
        retrying = true
        val expectedGeneration = generation
        try {
            val retried = repository.retryJob(failed.jobId)
            if (generation != expectedGeneration) return
            val current = mutableState.value ?: return
            val ticket = MediaRequestTicket(generation, current.toSource())
            if (retried.status == MediaJobStatus.GENERATING) {
                contentGenerating(ticket, retried)
            } else {
                activeJob = retried
                mutableState.value = current.copy(loadState = retried.toTerminalLoadState())
            }
        } catch (error: KuraStorageException) {
            if (generation == expectedGeneration) fail(error)
        } finally {
            retrying = false
        }
    }

    override fun close() {
        invalidateRequests()
        scope.cancel()
        mutableState.value = null
    }

    private suspend fun prepareSelectedQuality(expectedGeneration: Long) {
        val current = mutableState.value ?: return
        val variant = MediaVariantResolver.resolve(current.kind, current.quality)
        if (variant != com.kurastorage.core.model.media.MediaVariant.ORIGINAL) {
            if (generation == expectedGeneration) {
                mutableState.value = current.copy(loadState = MediaLoadState.Loading)
            }
            return
        }
        mutableState.value = current.copy(loadState = MediaLoadState.ConfirmingTransfer)
        try {
            val prompt = confirmationPolicy.prepare(current.fileId, current.fileVersion, current.kind)
            if (generation == expectedGeneration) {
                mutableState.value = current.copy(loadState = MediaLoadState.ConfirmingTransfer, confirmation = prompt)
            }
        } catch (error: KuraStorageException) {
            if (generation == expectedGeneration) fail(error)
        }
    }

    @Suppress("ReturnCount")
    private suspend fun poll(
        ticket: MediaRequestTicket,
        initial: MediaJobSnapshot,
    ) {
        var snapshot = initial
        while (coroutineContext.isActive && ticket.isCurrent()) {
            delay(snapshot.retryAfterSeconds.coerceAtLeast(1) * MILLIS_PER_SECOND)
            if (!ticket.isCurrent()) return
            snapshot =
                try {
                    repository.job(snapshot.jobId)
                } catch (error: KuraStorageException) {
                    fail(error)
                    return
                }
            if (!ticket.isCurrent()) return
            activeJob = snapshot
            when (snapshot.status) {
                MediaJobStatus.GENERATING ->
                    mutableState.value = mutableState.value?.copy(loadState = MediaLoadState.Generating(snapshot))
                MediaJobStatus.READY -> {
                    mutableState.value = mutableState.value?.copy(loadState = MediaLoadState.Loading)
                    return
                }
                MediaJobStatus.FAILED,
                MediaJobStatus.CANCELLED,
                MediaJobStatus.UNKNOWN,
                -> {
                    mutableState.value =
                        mutableState.value?.copy(loadState = MediaLoadState.Failed(MediaUiError.GENERATION_FAILED))
                    return
                }
            }
        }
    }

    private fun invalidateRequests() {
        generation++
        pollingJob?.cancel()
        pollingJob = null
        retrying = false
    }

    private fun fail(error: KuraStorageException) {
        val uiError =
            when (error) {
                is KuraStorageException.Network -> MediaUiError.DISCONNECTED
                is KuraStorageException.Api ->
                    when (error.error.statusCode) {
                        HTTP_UNAUTHORIZED -> MediaUiError.AUTHENTICATION_REQUIRED
                        HTTP_FORBIDDEN -> MediaUiError.PERMISSION_DENIED
                        HTTP_NOT_FOUND -> MediaUiError.NOT_FOUND
                        HTTP_CONFLICT -> MediaUiError.FILE_CHANGED
                        HTTP_RANGE_NOT_SATISFIABLE -> MediaUiError.RANGE_INVALID
                        else -> MediaUiError.UNKNOWN
                    }
                else -> MediaUiError.UNKNOWN
            }
        mutableState.value = mutableState.value?.copy(loadState = MediaLoadState.Failed(uiError))
    }

    private fun MediaJobSnapshot.toTerminalLoadState(): MediaLoadState =
        when (status) {
            MediaJobStatus.READY -> MediaLoadState.Loading
            MediaJobStatus.FAILED,
            MediaJobStatus.CANCELLED,
            MediaJobStatus.UNKNOWN,
            MediaJobStatus.GENERATING,
            -> MediaLoadState.Failed(MediaUiError.GENERATION_FAILED)
        }

    private fun MediaRequestTicket.isCurrent(): Boolean {
        val currentSource = mutableState.value?.toSource()
        return generation == this.generation && currentSource == source
    }

    private fun MediaViewerState.toSource(): ReadyMediaSource =
        ReadyMediaSource(fileId, fileVersion, MediaVariantResolver.resolve(kind, quality))

    private companion object {
        const val MILLIS_PER_SECOND = 1_000L
        const val HTTP_UNAUTHORIZED = 401
        const val HTTP_FORBIDDEN = 403
        const val HTTP_NOT_FOUND = 404
        const val HTTP_CONFLICT = 409
        const val HTTP_RANGE_NOT_SATISFIABLE = 416
    }
}
