package com.kurastorage.core.data

import android.content.Context
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.DeviceRegistrationMetadata
import com.kurastorage.core.model.SessionMetadata
import com.kurastorage.core.model.UserRole
import kotlinx.coroutines.flow.first
import java.time.Instant
import java.util.UUID

internal val Context.credentialDataStore by preferencesDataStore(name = "credential_metadata")

interface CredentialMetadataStore {
    suspend fun readRegistration(): DeviceRegistrationMetadata?

    suspend fun writeRegistration(metadata: DeviceRegistrationMetadata)

    suspend fun readSession(): SessionMetadata?

    suspend fun writeSession(metadata: SessionMetadata)

    suspend fun clearSession()

    suspend fun clearRegistration()
}

class DataStoreCredentialMetadataStore(
    private val context: Context,
) : CredentialMetadataStore {
    @Suppress("ReturnCount")
    override suspend fun readRegistration(): DeviceRegistrationMetadata? {
        val values = context.credentialDataStore.data.first()
        val rawDeviceId = values[DEVICE_ID]
        if (rawDeviceId == null) {
            if (values.containsRegistrationOrSessionData()) clearRegistration()
            return null
        }
        if (!isUuid(rawDeviceId)) {
            clearRegistration()
            return null
        }
        val deviceId = DeviceId(rawDeviceId)
        return DeviceRegistrationMetadata(deviceId, values[LAST_USERNAME])
    }

    override suspend fun writeRegistration(metadata: DeviceRegistrationMetadata) {
        require(isUuid(metadata.deviceId.value)) { "Device ID must be a UUID" }
        context.credentialDataStore.edit { values ->
            values[DEVICE_ID] = metadata.deviceId.value
            metadata.username?.let { values[LAST_USERNAME] = it } ?: values.remove(LAST_USERNAME)
        }
    }

    @Suppress("ReturnCount")
    override suspend fun readSession(): SessionMetadata? {
        val values = context.credentialDataStore.data.first()
        val expiresAt =
            values[REFRESH_EXPIRES_AT]
                ?.let { runCatching { Instant.parse(it) }.getOrNull() }
                ?: return null
        val roleValue = values[ROLE]
        val role =
            if (roleValue == null) {
                UserRole.MEMBER
            } else {
                runCatching { UserRole.valueOf(roleValue) }.getOrNull() ?: return null
            }
        val userId = values[USER_ID]?.takeIf(::isUuid) ?: return null
        return SessionMetadata(userId, expiresAt, role)
    }

    override suspend fun writeSession(metadata: SessionMetadata) {
        require(isUuid(metadata.userId)) { "User ID must be a UUID" }
        context.credentialDataStore.edit { values ->
            values[USER_ID] = metadata.userId
            values[REFRESH_EXPIRES_AT] = metadata.refreshTokenExpiresAt.toString()
            values[ROLE] = metadata.role.name
        }
    }

    override suspend fun clearSession() {
        context.credentialDataStore.edit { values ->
            values.remove(USER_ID)
            values.remove(REFRESH_EXPIRES_AT)
            values.remove(ROLE)
        }
    }

    override suspend fun clearRegistration() {
        context.credentialDataStore.edit { values ->
            values.remove(DEVICE_ID)
            values.remove(LAST_USERNAME)
            values.remove(USER_ID)
            values.remove(REFRESH_EXPIRES_AT)
            values.remove(ROLE)
        }
    }

    private fun isUuid(value: String): Boolean = runCatching { UUID.fromString(value) }.isSuccess

    private fun Preferences.containsRegistrationOrSessionData(): Boolean =
        this[LAST_USERNAME] != null ||
            this[USER_ID] != null ||
            this[REFRESH_EXPIRES_AT] != null ||
            this[ROLE] != null

    private companion object {
        val DEVICE_ID = stringPreferencesKey("device_id")
        val USER_ID = stringPreferencesKey("user_id")
        val REFRESH_EXPIRES_AT = stringPreferencesKey("refresh_token_expires_at")
        val LAST_USERNAME = stringPreferencesKey("last_username")
        val ROLE = stringPreferencesKey("role")
    }
}
