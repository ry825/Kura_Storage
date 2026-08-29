package com.kurastorage.core.data.media

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.QualityPreferences
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.first
import java.io.IOException

private val Context.mediaQualityDataStore by preferencesDataStore(name = "media_quality_preferences")

interface QualityPreferenceStore {
    suspend fun read(): QualityPreferences

    suspend fun update(
        context: NetworkQualityContext,
        quality: MediaQuality,
    )
}

class DataStoreQualityPreferenceStore(
    private val context: Context,
) : QualityPreferenceStore {
    override suspend fun read(): QualityPreferences {
        val preferences =
            context.mediaQualityDataStore.data
                .catch { error ->
                    if (error is IOException) emit(emptyPreferences()) else throw error
                }.first()
        return QualityPreferencesCodec.decode(
            NetworkQualityContext.entries.associateWith { preferences[key(it)] },
        )
    }

    override suspend fun update(
        context: NetworkQualityContext,
        quality: MediaQuality,
    ) {
        this.context.mediaQualityDataStore.edit { preferences -> preferences[key(context)] = quality.name }
    }

    private fun key(context: NetworkQualityContext) = stringPreferencesKey(context.name.lowercase())
}

object QualityPreferencesCodec {
    val manualChoices: List<MediaQuality> = MediaQuality.entries

    fun decode(values: Map<NetworkQualityContext, String?>): QualityPreferences {
        fun value(
            context: NetworkQualityContext,
            fallback: MediaQuality,
        ): MediaQuality = values[context]?.let { runCatching { MediaQuality.valueOf(it) }.getOrNull() } ?: fallback

        return QualityPreferences(
            localDirect = value(NetworkQualityContext.LOCAL_DIRECT, MediaQuality.ORIGINAL),
            registeredRemoteWifi = value(NetworkQualityContext.REGISTERED_REMOTE_WIFI, MediaQuality.MEDIUM),
            unregisteredRemoteWifi = value(NetworkQualityContext.UNREGISTERED_REMOTE_WIFI, MediaQuality.LOW),
            remoteMobile = value(NetworkQualityContext.REMOTE_MOBILE, MediaQuality.LOW),
        )
    }
}
