package com.kurastorage.core.data.backup

import android.content.Context
import android.location.LocationManager
import android.net.ConnectivityManager
import android.net.wifi.WifiManager
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AndroidCurrentWifiSourceTest {
    @Test
    fun missingRuntimePermissionFailsClosedBeforeWifiIdentifiersAreRead() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val source =
            AndroidCurrentWifiSource(
                context,
                context.getSystemService(ConnectivityManager::class.java),
                context.getSystemService(WifiManager::class.java),
                context.getSystemService(LocationManager::class.java),
            )

        val result = source.read()
        assertTrue(result is CurrentWifiResult.PermissionRequired || result is CurrentWifiResult.PermissionPermanentlyDenied)
    }
}
