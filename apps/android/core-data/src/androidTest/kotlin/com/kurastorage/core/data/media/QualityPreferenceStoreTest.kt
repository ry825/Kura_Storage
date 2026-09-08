package com.kurastorage.core.data.media

import androidx.datastore.preferences.core.PreferenceDataStoreFactory
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStoreFile
import androidx.test.core.app.ApplicationProvider
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.PhotoDisplayMode
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Test

class QualityPreferenceStoreTest {
    @Test
    fun preferencesPersistIndependentlyForEveryNetworkContext() =
        runBlocking {
            val context = ApplicationProvider.getApplicationContext<android.content.Context>()
            val store = DataStoreQualityPreferenceStore(context)
            store.update(NetworkQualityContext.LOCAL_DIRECT, PhotoDisplayMode.ORIGINAL)
            store.update(NetworkQualityContext.REGISTERED_REMOTE_WIFI, PhotoDisplayMode.FAST)
            store.update(NetworkQualityContext.UNREGISTERED_REMOTE_WIFI, PhotoDisplayMode.FAST)
            store.update(NetworkQualityContext.REMOTE_MOBILE, PhotoDisplayMode.FAST)

            val preferences = store.read()

            assertEquals(PhotoDisplayMode.ORIGINAL, preferences.localDirect)
            assertEquals(PhotoDisplayMode.FAST, preferences.registeredRemoteWifi)
            assertEquals(PhotoDisplayMode.FAST, preferences.unregisteredRemoteWifi)
            assertEquals(PhotoDisplayMode.FAST, preferences.remoteMobile)
        }

    @Test
    fun legacyMigrationIsAppliedOnlyOnce() =
        runBlocking {
            val context = ApplicationProvider.getApplicationContext<android.content.Context>()
            val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
            val dataStore =
                PreferenceDataStoreFactory.create(scope = scope) {
                    context.preferencesDataStoreFile("quality-preferences-once-${System.nanoTime()}")
                }
            try {
                dataStore.edit { preferences ->
                    preferences[stringPreferencesKey("registered_remote_wifi")] = "LOW"
                }
                val store = DataStoreQualityPreferenceStore(context, dataStore)

                assertEquals(PhotoDisplayMode.FAST, store.read().registeredRemoteWifi)

                dataStore.edit { preferences ->
                    preferences[stringPreferencesKey("registered_remote_wifi")] = "ORIGINAL"
                }

                assertEquals(PhotoDisplayMode.FAST, store.read().registeredRemoteWifi)
            } finally {
                scope.cancel()
            }
        }
}
