package com.kurastorage.core.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.kurastorage.core.model.DeviceId
import kotlinx.coroutines.flow.first
import java.time.Instant

private val Context.credentialDataStore by preferencesDataStore(name = "credential_metadata")

data class CredentialMetadata(
    val deviceId: DeviceId,
    val refreshTokenExpiresAt: Instant,
    val username: String?,
)

interface CredentialMetadataStore {
    suspend fun read(): CredentialMetadata?

    suspend fun write(metadata: CredentialMetadata)

    suspend fun clear()
}

class DataStoreCredentialMetadataStore(
    private val context: Context,
) : CredentialMetadataStore {
    @Suppress("ReturnCount")
    override suspend fun read(): CredentialMetadata? {
        val values = context.credentialDataStore.data.first()
        val deviceId = values[DEVICE_ID]?.let(::DeviceId) ?: return null
        val expiresAt = values[REFRESH_EXPIRES_AT]?.let(Instant::parse) ?: return null
        return CredentialMetadata(deviceId, expiresAt, values[LAST_USERNAME])
    }

    override suspend fun write(metadata: CredentialMetadata) {
        context.credentialDataStore.edit { values ->
            values[DEVICE_ID] = metadata.deviceId.value
            values[REFRESH_EXPIRES_AT] = metadata.refreshTokenExpiresAt.toString()
            metadata.username?.let { values[LAST_USERNAME] = it } ?: values.remove(LAST_USERNAME)
        }
    }

    override suspend fun clear() {
        context.credentialDataStore.edit { it.clear() }
    }

    private companion object {
        val DEVICE_ID = stringPreferencesKey("device_id")
        val REFRESH_EXPIRES_AT = stringPreferencesKey("refresh_token_expires_at")
        val LAST_USERNAME = stringPreferencesKey("last_username")
    }
}
