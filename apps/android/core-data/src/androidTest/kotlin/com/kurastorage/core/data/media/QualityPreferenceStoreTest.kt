package com.kurastorage.core.data.media

import androidx.test.core.app.ApplicationProvider
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.NetworkQualityContext
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Test

class QualityPreferenceStoreTest {
    @Test
    fun preferencesPersistIndependentlyForEveryNetworkContext() =
        runBlocking {
            val context = ApplicationProvider.getApplicationContext<android.content.Context>()
            val store = DataStoreQualityPreferenceStore(context)
            store.update(NetworkQualityContext.LOCAL_DIRECT, MediaQuality.ORIGINAL)
            store.update(NetworkQualityContext.REGISTERED_REMOTE_WIFI, MediaQuality.MEDIUM)
            store.update(NetworkQualityContext.UNREGISTERED_REMOTE_WIFI, MediaQuality.LOW)
            store.update(NetworkQualityContext.REMOTE_MOBILE, MediaQuality.MEDIUM)

            val preferences = store.read()

            assertEquals(MediaQuality.ORIGINAL, preferences.localDirect)
            assertEquals(MediaQuality.MEDIUM, preferences.registeredRemoteWifi)
            assertEquals(MediaQuality.LOW, preferences.unregisteredRemoteWifi)
            assertEquals(MediaQuality.MEDIUM, preferences.remoteMobile)
        }
}
