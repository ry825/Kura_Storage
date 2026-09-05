package com.kurastorage.feature.files

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.AdminStorageRepository
import com.kurastorage.core.model.AdminStorageStatus
import com.kurastorage.core.ui.formatting.formatFileSize
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class AdminStorageState(
    val loading: Boolean = true,
    val status: AdminStorageStatus? = null,
    val error: Boolean = false,
) {
    val visible: Boolean get() = status != null || error
}

class AdminStorageViewModel(
    private val repository: AdminStorageRepository,
) : ViewModel() {
    private val mutableState = MutableStateFlow(AdminStorageState())
    val state: StateFlow<AdminStorageState> = mutableState.asStateFlow()

    init {
        refresh()
    }

    fun refresh() {
        viewModelScope.launch {
            mutableState.update { it.copy(loading = true, error = false) }
            runCatching { repository.get() }
                .onSuccess { status ->
                    mutableState.value = AdminStorageState(loading = false, status = status)
                }.onFailure {
                    mutableState.update { state -> state.copy(loading = false, error = true) }
                }
        }
    }
}

internal fun formatBytes(bytes: Long?): String = formatFileSize(bytes, unknownLabel = "Unknown")
