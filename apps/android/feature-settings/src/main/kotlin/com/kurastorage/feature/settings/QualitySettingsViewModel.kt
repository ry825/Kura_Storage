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
    val saving: Boolean = false,
    val dirty: Boolean = false,
    val error: String? = null,
)

class QualitySettingsViewModel(
    private val store: QualityPreferenceStore,
) : ViewModel() {
    private val mutableState = MutableStateFlow(QualitySettingsState())
    private var persisted = QualityPreferences()
    val state: StateFlow<QualitySettingsState> = mutableState.asStateFlow()

    init {
        viewModelScope.launch {
            mutableState.value =
                runCatching { store.read() }
                    .fold(
                        onSuccess = {
                            persisted = it
                            QualitySettingsState(preferences = it, loading = false)
                        },
                        onFailure = {
                            QualitySettingsState(loading = false, error = "Quality settings could not be loaded")
                        },
                    )
        }
    }

    fun select(
        context: NetworkQualityContext,
        quality: MediaQuality,
    ) {
        if (mutableState.value.loading || mutableState.value.saving) return
        val next = mutableState.value.preferences.withQuality(context, quality)
        mutableState.value = mutableState.value.copy(preferences = next, dirty = next != persisted, error = null)
    }

    fun save() {
        if (mutableState.value.loading || mutableState.value.saving || !mutableState.value.dirty) return
        persist(mutableState.value.preferences)
    }

    fun reset() {
        if (mutableState.value.loading || mutableState.value.saving) return
        val defaults = QualityPreferences()
        mutableState.value =
            mutableState.value.copy(preferences = defaults, dirty = defaults != persisted, error = null)
    }

    /** Retained for non-UI callers that intentionally persist one context immediately. */
    fun update(
        context: NetworkQualityContext,
        quality: MediaQuality,
    ) {
        if (mutableState.value.loading || mutableState.value.saving) return
        persist(mutableState.value.preferences.withQuality(context, quality))
    }

    private fun persist(preferences: QualityPreferences) {
        viewModelScope.launch {
            mutableState.value = mutableState.value.copy(saving = true, error = null)
            runCatching {
                NetworkQualityContext.entries.forEach { context ->
                    val next = preferences.qualityFor(context)
                    if (persisted.qualityFor(context) != next) store.update(context, next)
                }
            }.onSuccess {
                persisted = preferences
                mutableState.value = mutableState.value.copy(preferences = preferences, saving = false, dirty = false)
            }.onFailure {
                mutableState.value =
                    mutableState.value.copy(saving = false, error = "Quality setting could not be saved")
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
