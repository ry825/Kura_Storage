@file:Suppress("MaxLineLength", "MagicNumber")

package com.kurastorage.feature.text

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.TextFileRepository
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileVersionItem
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.TextDocument
import com.kurastorage.core.model.canEditText
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.util.UUID

data class VersionHistoryUiState(
    val loading: Boolean = true,
    val refreshing: Boolean = false,
    val loadingMore: Boolean = false,
    val items: List<FileVersionItem> = emptyList(),
    val page: Int = 0,
    val hasNextPage: Boolean = false,
    val current: TextDocument? = null,
    val canRestore: Boolean = false,
    val preview: TextDocument? = null,
    val previewDiff: List<LineDiff>? = null,
    val restoreConfirmationVersion: Long? = null,
    val restoring: Boolean = false,
    val restoreConflict: Boolean = false,
    val errorCode: ErrorCode? = null,
    val requestId: String? = null,
)

@Suppress("TooManyFunctions")
class VersionHistoryViewModel(
    private val fileId: String,
    private val files: FileRepository,
    private val text: TextFileRepository,
    private val operationIdFactory: () -> String = { UUID.randomUUID().toString() },
) : ViewModel() {
    private val mutableState = MutableStateFlow(VersionHistoryUiState())
    private var generation = 0L
    private var previewGeneration = 0L
    private var listJob: Job? = null
    private var previewJob: Job? = null
    private var restoreJob: Job? = null

    val state: StateFlow<VersionHistoryUiState> = mutableState.asStateFlow()

    init {
        refresh()
    }

    fun refresh() {
        listJob?.cancel()
        previewJob?.cancel()
        previewGeneration += 1
        val ticket = ++generation
        mutableState.value =
            mutableState.value.copy(
                loading = mutableState.value.items.isEmpty(),
                refreshing = true,
                loadingMore = false,
                restoreConflict = false,
                errorCode = null,
                requestId = null,
            )
        listJob =
            viewModelScope.launch {
                runCatching {
                    val file = files.detail(fileId)
                    Triple(text.current(fileId), text.versions(fileId, 1, TextFileRepository.DEFAULT_PAGE_SIZE), file)
                }.onSuccess { (current, page, file) ->
                    if (ticket != generation) return@onSuccess
                    mutableState.value =
                        mutableState.value.copy(
                            loading = false,
                            refreshing = false,
                            loadingMore = false,
                            current = current,
                            canRestore = canEditText(file.permission, file.permissionSource),
                            items = page.items,
                            page = page.page,
                            hasNextPage = page.hasNextPage,
                            preview = null,
                            previewDiff = null,
                        )
                }.onFailure { error -> if (ticket == generation) updateError(error) }
            }
    }

    fun loadMore() {
        val snapshot = mutableState.value
        if (!snapshot.hasNextPage || snapshot.refreshing || snapshot.loadingMore) return
        val ticket = generation
        mutableState.value = snapshot.copy(loadingMore = true, errorCode = null, requestId = null)
        listJob =
            viewModelScope.launch {
                runCatching { text.versions(fileId, snapshot.page + 1, TextFileRepository.DEFAULT_PAGE_SIZE) }
                    .onSuccess { page ->
                        if (ticket != generation) return@onSuccess
                        mutableState.value =
                            mutableState.value.copy(
                                items = snapshot.items + page.items,
                                page = page.page,
                                hasNextPage = page.hasNextPage,
                                loadingMore = false,
                            )
                    }.onFailure { error -> if (ticket == generation) updateError(error) }
            }
    }

    fun preview(version: Long) {
        previewJob?.cancel()
        val ticket = ++previewGeneration
        previewJob =
            viewModelScope.launch {
                runCatching { text.version(fileId, version) }
                    .onSuccess { preview ->
                        if (ticket != previewGeneration) return@onSuccess
                        val current = mutableState.value.current
                        mutableState.value =
                            mutableState.value.copy(
                                preview = preview,
                                previewDiff = current?.let { BoundedLineDiff.compare(preview.content, it.content) },
                                errorCode = null,
                                requestId = null,
                            )
                    }.onFailure { error -> if (ticket == previewGeneration) updateError(error) }
            }
    }

    fun dismissPreview() {
        previewJob?.cancel()
        previewGeneration += 1
        mutableState.value = mutableState.value.copy(preview = null, previewDiff = null)
    }

    fun requestRestore(version: Long) {
        if (!mutableState.value.canRestore) return
        previewJob?.cancel()
        previewGeneration += 1
        mutableState.value =
            mutableState.value.copy(
                preview = null,
                previewDiff = null,
                restoreConfirmationVersion = version,
            )
    }

    fun dismissRestore() {
        mutableState.value = mutableState.value.copy(restoreConfirmationVersion = null)
    }

    @Suppress("TooGenericExceptionCaught")
    fun confirmRestore() {
        val version = mutableState.value.restoreConfirmationVersion ?: return
        val ticket = ++generation
        restoreJob?.cancel()
        mutableState.value = mutableState.value.copy(restoring = true, restoreConflict = false)
        restoreJob =
            viewModelScope.launch {
                try {
                    val latestFile = files.detail(fileId)
                    if (!canEditText(latestFile.permission, latestFile.permissionSource)) {
                        throw KuraStorageException.Api(
                            com.kurastorage.core.model
                                .ApiError(ErrorCode.FILE_NOT_FOUND, null, 403),
                        )
                    }
                    val current = text.current(fileId)
                    text.restore(fileId, version, current.fileVersion, operationIdFactory())
                    if (ticket != generation) return@launch
                    mutableState.value = mutableState.value.copy(restoring = false, restoreConfirmationVersion = null)
                    refresh()
                } catch (error: Throwable) {
                    if (ticket != generation) return@launch
                    val api = error as? KuraStorageException.Api
                    if (api?.error?.code == ErrorCode.FILE_VERSION_CONFLICT) {
                        mutableState.value =
                            mutableState.value.copy(
                                restoring = false,
                                restoreConfirmationVersion = null,
                                restoreConflict = true,
                                errorCode = ErrorCode.FILE_VERSION_CONFLICT,
                                requestId = api.error.requestId,
                            )
                    } else {
                        updateError(error)
                    }
                }
            }
    }

    private fun updateError(error: Throwable) {
        val api = error as? KuraStorageException.Api
        mutableState.value =
            mutableState.value.copy(
                loading = false,
                refreshing = false,
                loadingMore = false,
                restoring = false,
                errorCode = api?.error?.code ?: ErrorCode.UNKNOWN,
                requestId = api?.error?.requestId,
            )
    }
}
