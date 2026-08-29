package com.kurastorage.feature.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.media.QualityPreferenceStore
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.QualityPreferences
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

data class QualitySettingsState(
    val preferences: QualityPreferences = QualityPreferences(),
    val loading: Boolean = true,
    val saving: NetworkQualityContext? = null,
    val error: String? = null,
)

class QualitySettingsViewModel(
    private val store: QualityPreferenceStore,
) : ViewModel() {
    private val mutableState = MutableStateFlow(QualitySettingsState())
    val state: StateFlow<QualitySettingsState> = mutableState.asStateFlow()

    init {
        viewModelScope.launch {
            mutableState.value =
                runCatching { store.read() }
                    .fold(
                        onSuccess = { QualitySettingsState(preferences = it, loading = false) },
                        onFailure = {
                            QualitySettingsState(loading = false, error = "Quality settings could not be loaded")
                        },
                    )
        }
    }

    fun update(
        context: NetworkQualityContext,
        quality: MediaQuality,
    ) {
        if (mutableState.value.saving != null) return
        viewModelScope.launch {
            mutableState.value = mutableState.value.copy(saving = context, error = null)
            runCatching { store.update(context, quality) }
                .onSuccess {
                    val preferences = mutableState.value.preferences.withQuality(context, quality)
                    mutableState.value = mutableState.value.copy(preferences = preferences, saving = null)
                }.onFailure {
                    mutableState.value =
                        mutableState.value.copy(saving = null, error = "Quality setting could not be saved")
                }
        }
    }
}

private fun QualityPreferences.withQuality(
    context: NetworkQualityContext,
    quality: MediaQuality,
): QualityPreferences =
    when (context) {
        NetworkQualityContext.LOCAL_DIRECT -> copy(localDirect = quality)
        NetworkQualityContext.REGISTERED_REMOTE_WIFI -> copy(registeredRemoteWifi = quality)
        NetworkQualityContext.UNREGISTERED_REMOTE_WIFI -> copy(unregisteredRemoteWifi = quality)
        NetworkQualityContext.REMOTE_MOBILE -> copy(remoteMobile = quality)
    }
