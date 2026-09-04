package com.kurastorage.core.data

import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.test.core.app.ApplicationProvider
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.DeviceRegistrationMetadata
import com.kurastorage.core.model.SessionMetadata
import com.kurastorage.core.model.UserRole
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.time.Instant

class CredentialMetadataStoreTest {
    @Test
    fun registrationSurvivesSessionClear() =
        withStore { store ->
            store.writeRegistration(registration())
            store.writeSession(session())

            assertEquals(registration(), store.readRegistration())
            assertEquals(session(), store.readSession())

            store.clearSession()

            assertEquals(registration(), store.readRegistration())
            assertNull(store.readSession())
        }

    @Test
    fun registrationClearRemovesRegistrationAndSession() =
        withStore { store ->
            store.writeRegistration(registration())
            store.writeSession(session())

            store.clearRegistration()

            assertNull(store.readRegistration())
            assertNull(store.readSession())
        }

    @Test
    fun existingKeysWithoutRoleRemainReadableAsMemberSession() =
        withStore { store ->
            val context = ApplicationProvider.getApplicationContext<android.content.Context>()
            context.credentialDataStore.edit { values ->
                values[stringPreferencesKey("device_id")] = DEVICE_ID
                values[stringPreferencesKey("last_username")] = "family"
                values[stringPreferencesKey("user_id")] = USER_ID
                values[stringPreferencesKey("refresh_token_expires_at")] = EXPIRY.toString()
            }

            assertEquals(registration(), store.readRegistration())
            assertEquals(session(role = UserRole.MEMBER), store.readSession())
        }

    @Test
    fun invalidSessionMetadataDoesNotRemoveRegistration() =
        withStore { store ->
            val context = ApplicationProvider.getApplicationContext<android.content.Context>()
            store.writeRegistration(registration())
            context.credentialDataStore.edit { values ->
                values[stringPreferencesKey("user_id")] = USER_ID
                values[stringPreferencesKey("refresh_token_expires_at")] = "not-an-instant"
                values[stringPreferencesKey("role")] = UserRole.ADMIN.name
            }

            assertNull(store.readSession())
            assertEquals(registration(), store.readRegistration())

            context.credentialDataStore.edit { values ->
                values[stringPreferencesKey("refresh_token_expires_at")] = EXPIRY.toString()
                values[stringPreferencesKey("role")] = "OWNER"
            }
            assertNull(store.readSession())
            assertEquals(registration(), store.readRegistration())

            context.credentialDataStore.edit { values ->
                values[stringPreferencesKey("user_id")] = "not-a-uuid"
                values[stringPreferencesKey("role")] = UserRole.ADMIN.name
            }
            assertNull(store.readSession())
            assertEquals(registration(), store.readRegistration())
        }

    @Test
    fun invalidDeviceIdClearsAllMetadata() =
        withStore { store ->
            val context = ApplicationProvider.getApplicationContext<android.content.Context>()
            context.credentialDataStore.edit { values ->
                values[stringPreferencesKey("device_id")] = "not-a-uuid"
                values[stringPreferencesKey("last_username")] = "family"
                values[stringPreferencesKey("user_id")] = USER_ID
                values[stringPreferencesKey("refresh_token_expires_at")] = EXPIRY.toString()
                values[stringPreferencesKey("role")] = UserRole.ADMIN.name
            }

            assertNull(store.readRegistration())
            assertNull(store.readSession())
        }

    private fun withStore(block: suspend (DataStoreCredentialMetadataStore) -> Unit) =
        runBlocking {
            val context = ApplicationProvider.getApplicationContext<android.content.Context>()
            val store = DataStoreCredentialMetadataStore(context)
            store.clearRegistration()
            try {
                block(store)
            } finally {
                store.clearRegistration()
            }
        }

    private fun registration() = DeviceRegistrationMetadata(DeviceId(DEVICE_ID), "family")

    private fun session(role: UserRole = UserRole.ADMIN) = SessionMetadata(USER_ID, EXPIRY, role)

    private companion object {
        const val DEVICE_ID = "11111111-1111-4111-8111-111111111111"
        const val USER_ID = "22222222-2222-4222-8222-222222222222"
        val EXPIRY: Instant = Instant.parse("2026-08-22T00:00:00Z")
    }
}
