package com.kurastorage.feature.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.media.AdminMediaCacheRepository
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.AdminMediaCacheStatus
import com.kurastorage.core.model.media.MediaCleanupRunStatus
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

enum class CacheAccessState {
    AVAILABLE,
    FORBIDDEN,
}

data class CacheManagementState(
    val loading: Boolean = true,
    val status: AdminMediaCacheStatus? = null,
    val access: CacheAccessState = CacheAccessState.AVAILABLE,
    val requestingCleanup: Boolean = false,
    val unknownCleanupOutcome: Boolean = false,
    val error: String? = null,
)

class CacheManagementViewModel(
    private val repository: AdminMediaCacheRepository,
    private val pollDelay: suspend () -> Unit = { delay(POLL_INTERVAL_MILLIS) },
    private val maximumPolls: Int = MAXIMUM_POLLS,
) : ViewModel() {
    private val mutableState = MutableStateFlow(CacheManagementState())
    val state: StateFlow<CacheManagementState> = mutableState.asStateFlow()

    init {
        refresh()
    }

    fun refresh() {
        viewModelScope.launch { load(showLoading = true) }
    }

    fun requestCleanup() {
        if (mutableState.value.requestingCleanup) return
        viewModelScope.launch {
            mutableState.update { it.copy(requestingCleanup = true, error = null) }
            val accepted = runCatching { repository.requestCleanup() }
            if (accepted.isFailure) {
                val failure = accepted.exceptionOrNull()
                val recovered = runCatching { load(showLoading = false) }.isSuccess
                val unknown = failure is KuraStorageException.Network && repository.hasUnknownCleanupOutcome()
                mutableState.update {
                    it.copy(
                        requestingCleanup = false,
                        unknownCleanupOutcome = unknown,
                        error =
                            when {
                                unknown && recovered ->
                                    "The cleanup request result is unknown. Status was refreshed; " +
                                        "retry to resend the same request."
                                unknown -> "The cleanup request result is unknown. Retry will resend the same request."
                                else -> failure.toCacheMessage()
                            },
                    )
                }
                return@launch
            }

            mutableState.update { it.copy(unknownCleanupOutcome = false) }
            pollUntilTerminal(checkNotNull(accepted.getOrNull()).id)
            mutableState.update { it.copy(requestingCleanup = false) }
        }
    }

    private suspend fun pollUntilTerminal(acceptedRunId: String) {
        var attempt = 0
        var finished = false
        while (attempt < maximumPolls && !finished) {
            if (!load(showLoading = false)) {
                finished = true
            } else {
                val run = mutableState.value.status?.lastCleanupRun
                when {
                    run == null -> finished = true
                    run.id != acceptedRunId -> Unit
                    run.status == MediaCleanupRunStatus.UNKNOWN -> {
                        mutableState.update {
                            it.copy(error = "The server returned an unknown cleanup status. No action was assumed.")
                        }
                        finished = true
                    }
                    run.terminal -> finished = true
                }
            }
            attempt++
            if (!finished && attempt < maximumPolls) pollDelay()
        }
        if (!finished) {
            mutableState.update {
                it.copy(error = "Cleanup is still running. Refresh to check its latest server status.")
            }
        }
    }

    private suspend fun load(showLoading: Boolean): Boolean {
        if (showLoading) mutableState.update { it.copy(loading = true, error = null) }
        return runCatching { repository.get() }
            .fold(
                onSuccess = { status ->
                    mutableState.update {
                        it.copy(loading = false, status = status, access = CacheAccessState.AVAILABLE, error = null)
                    }
                    true
                },
                onFailure = { failure ->
                    val forbidden = failure is KuraStorageException.Api && failure.error.statusCode == HTTP_FORBIDDEN
                    mutableState.update {
                        it.copy(
                            loading = false,
                            status = if (forbidden) null else it.status,
                            access = if (forbidden) CacheAccessState.FORBIDDEN else it.access,
                            error = failure.toCacheMessage(),
                        )
                    }
                    false
                },
            )
    }
}

private fun Throwable?.toCacheMessage(): String =
    when {
        this is KuraStorageException.Api && error.statusCode == HTTP_FORBIDDEN ->
            "Cache management is available to administrators only. No management action was performed."
        this is KuraStorageException.Network -> "Cache status could not be reached. Check the connection and refresh."
        this is KuraStorageException.InvalidServerResponse -> "The server returned an invalid cache status."
        else -> "Cache management could not be completed."
    }

private const val HTTP_FORBIDDEN = 403
private const val POLL_INTERVAL_MILLIS = 2_000L
private const val MAXIMUM_POLLS = 30
