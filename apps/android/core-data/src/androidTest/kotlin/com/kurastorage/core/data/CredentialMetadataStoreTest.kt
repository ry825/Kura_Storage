package com.kurastorage.core.data

import androidx.test.core.app.ApplicationProvider
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.UserRole
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.time.Instant

class CredentialMetadataStoreTest {
    @Test
    fun roleIsPersistedAndRemovedWithCredentialMetadata() =
        runBlocking {
            val context = ApplicationProvider.getApplicationContext<android.content.Context>()
            val store = DataStoreCredentialMetadataStore(context)
            store.clear()
            try {
                store.write(
                    CredentialMetadata(
                        deviceId = DeviceId("11111111-1111-1111-1111-111111111111"),
                        refreshTokenExpiresAt = Instant.parse("2026-08-22T00:00:00Z"),
                        username = "family",
                        role = UserRole.ADMIN,
                        userId = "22222222-2222-2222-2222-222222222222",
                    ),
                )

                assertEquals(UserRole.ADMIN, store.read()?.role)
                assertEquals("22222222-2222-2222-2222-222222222222", store.read()?.userId)
                store.clear()
                assertNull(store.read())
            } finally {
                store.clear()
            }
        }
}
