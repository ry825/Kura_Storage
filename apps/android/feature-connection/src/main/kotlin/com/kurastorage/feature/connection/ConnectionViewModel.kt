package com.kurastorage.feature.connection

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.network.ConnectionDetector
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

class ConnectionViewModel(
    private val detector: ConnectionDetector,
) : ViewModel() {
    private val mutableState = MutableStateFlow<ConnectionStatus>(ConnectionStatus.Checking)
    val state: StateFlow<ConnectionStatus> = mutableState.asStateFlow()

    fun check() {
        mutableState.value = ConnectionStatus.Checking
        viewModelScope.launch {
            mutableState.value = detector.detect()
        }
    }
}
