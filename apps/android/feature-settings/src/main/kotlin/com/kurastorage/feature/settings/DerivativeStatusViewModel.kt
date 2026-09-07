package com.kurastorage.feature.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.media.AdminMediaDerivativeRepository
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.MediaDerivativeStatus
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class DerivativeStatusState(
    val loading: Boolean = true,
    val status: MediaDerivativeStatus? = null,
    val error: String? = null,
)

class DerivativeStatusViewModel(
    private val repository: AdminMediaDerivativeRepository,
) : ViewModel() {
    private val mutableState = MutableStateFlow(DerivativeStatusState())
    val state: StateFlow<DerivativeStatusState> = mutableState.asStateFlow()

    init {
        refresh()
    }

    fun refresh() {
        viewModelScope.launch {
            mutableState.update { it.copy(loading = true, error = null) }
            runCatching { repository.get() }.fold(
                onSuccess = { status -> mutableState.value = DerivativeStatusState(false, status) },
                onFailure = { failure ->
                    mutableState.update {
                        it.copy(
                            loading = false,
                            error =
                                if (failure is KuraStorageException.Api && failure.error.statusCode == 403) {
                                    "Administrator access is required."
                                } else {
                                    "Photo derivative status could not be loaded."
                                },
                        )
                    }
                },
            )
        }
    }
}
