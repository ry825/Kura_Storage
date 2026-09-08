package com.kurastorage.core.data.media

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.PhotoDisplayMode
import com.kurastorage.core.model.media.QualityPreferences
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.first
import java.io.IOException

private val Context.mediaQualityDataStore by preferencesDataStore(name = "media_quality_preferences")

interface QualityPreferenceStore {
    suspend fun read(): QualityPreferences

    suspend fun update(
        context: NetworkQualityContext,
        quality: PhotoDisplayMode,
    )
}

class DataStoreQualityPreferenceStore(
    private val context: Context,
    private val dataStore: DataStore<Preferences> = context.mediaQualityDataStore,
) : QualityPreferenceStore {
    override suspend fun read(): QualityPreferences {
        var preferences =
            dataStore.data
                .catch { error ->
                    if (error is IOException) emit(emptyPreferences()) else throw error
                }.first()
        if (preferences[MIGRATED_TO_FAST_DISPLAY] != true) {
            preferences =
                dataStore.edit { mutable ->
                    NetworkQualityContext.entries.forEach { networkContext ->
                        mutable[fastKey(networkContext)] =
                            QualityPreferencesCodec.migrateLegacy(
                                mutable[legacyKey(networkContext)],
                                networkContext,
                            )
                        mutable.remove(legacyKey(networkContext))
                    }
                    mutable[MIGRATED_TO_FAST_DISPLAY] = true
                }
        }
        return QualityPreferencesCodec.decode(
            NetworkQualityContext.entries.associateWith { preferences[fastKey(it)] },
        )
    }

    override suspend fun update(
        context: NetworkQualityContext,
        quality: PhotoDisplayMode,
    ) {
        dataStore.edit { preferences ->
            preferences[fastKey(context)] = quality == PhotoDisplayMode.FAST
        }
    }

    private fun legacyKey(context: NetworkQualityContext) = stringPreferencesKey(context.name.lowercase())

    private fun fastKey(context: NetworkQualityContext) =
        booleanPreferencesKey(
            "fast_display_${context.name.lowercase()}",
        )

    private companion object {
        val MIGRATED_TO_FAST_DISPLAY = booleanPreferencesKey("migrated_to_fast_display_v1")
    }
}

object QualityPreferencesCodec {
    val manualChoices: List<PhotoDisplayMode> = listOf(PhotoDisplayMode.FAST, PhotoDisplayMode.ORIGINAL)

    fun decode(values: Map<NetworkQualityContext, Boolean?>): QualityPreferences {
        fun value(
            context: NetworkQualityContext,
            fallback: PhotoDisplayMode,
        ): PhotoDisplayMode =
            values[context]
                ?.let { if (it) PhotoDisplayMode.FAST else PhotoDisplayMode.ORIGINAL }
                ?: fallback

        return QualityPreferences(
            localDirect = value(NetworkQualityContext.LOCAL_DIRECT, PhotoDisplayMode.ORIGINAL),
            registeredRemoteWifi = value(NetworkQualityContext.REGISTERED_REMOTE_WIFI, PhotoDisplayMode.FAST),
            unregisteredRemoteWifi = value(NetworkQualityContext.UNREGISTERED_REMOTE_WIFI, PhotoDisplayMode.FAST),
            remoteMobile = value(NetworkQualityContext.REMOTE_MOBILE, PhotoDisplayMode.FAST),
        )
    }

    fun migrateLegacy(
        raw: String?,
        context: NetworkQualityContext,
    ): Boolean =
        when (raw) {
            "ORIGINAL" -> false
            "LOW", "MEDIUM", "FAST" -> true
            else -> context != NetworkQualityContext.LOCAL_DIRECT
        }
}
