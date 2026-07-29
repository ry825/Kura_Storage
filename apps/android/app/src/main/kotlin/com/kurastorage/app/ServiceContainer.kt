package com.kurastorage.app

import android.content.Context
import android.net.ConnectivityManager
import com.kurastorage.core.data.DataStoreCredentialMetadataStore
import com.kurastorage.core.data.DefaultAuthenticationRepository
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.network.AndroidHealthProbe
import com.kurastorage.core.network.AndroidLocalNetworkSource
import com.kurastorage.core.network.ConnectionDetector
import com.kurastorage.core.network.FixedAddressDns
import com.kurastorage.core.network.KuraStorageApi
import com.kurastorage.core.security.AndroidKeystoreCredentialCipher
import com.kurastorage.core.security.SharedPreferencesEncryptedTokenStore
import okhttp3.OkHttpClient
import java.time.Duration

class ServiceContainer(
    context: Context,
) {
    private val applicationContext = context.applicationContext
    private val baseClient =
        OkHttpClient
            .Builder()
            .connectTimeout(Duration.ofSeconds(CONNECT_TIMEOUT_SECONDS))
            .readTimeout(Duration.ofSeconds(READ_TIMEOUT_SECONDS))
            .callTimeout(Duration.ofSeconds(CALL_TIMEOUT_SECONDS))
            .build()
    private val localNetworkSource =
        AndroidLocalNetworkSource(
            applicationContext.getSystemService(ConnectivityManager::class.java),
        )

    val connectionDetector =
        ConnectionDetector(
            apiHostname = BuildConfig.API_HOSTNAME,
            lanApiAddress = BuildConfig.LAN_API_ADDRESS,
            remoteApiAddress = BuildConfig.ZEROTIER_API_ADDRESS,
            localNetworkSource = localNetworkSource,
            healthProbe = AndroidHealthProbe(localNetworkSource, baseClient),
        )

    fun authenticationRepository(route: ConnectionRoute): DefaultAuthenticationRepository {
        val address =
            when (route) {
                ConnectionRoute.LOCAL_DIRECT -> BuildConfig.LAN_API_ADDRESS
                ConnectionRoute.REMOTE_SECURE -> BuildConfig.ZEROTIER_API_ADDRESS
            }
        val apiClient =
            baseClient
                .newBuilder()
                .dns(FixedAddressDns(BuildConfig.API_HOSTNAME, address))
                .apply {
                    if (route == ConnectionRoute.LOCAL_DIRECT) {
                        socketFactory(checkNotNull(localNetworkSource.lastNetwork()).socketFactory)
                    }
                }.build()
        return DefaultAuthenticationRepository(
            api =
                KuraStorageApi(
                    baseUrl = "https://${BuildConfig.API_HOSTNAME}/api/v1",
                    client = apiClient,
                ),
            metadataStore = DataStoreCredentialMetadataStore(applicationContext),
            tokenStore =
                SharedPreferencesEncryptedTokenStore(
                    applicationContext,
                    AndroidKeystoreCredentialCipher(),
                ),
        )
    }

    private companion object {
        const val CONNECT_TIMEOUT_SECONDS = 5L
        const val READ_TIMEOUT_SECONDS = 10L
        const val CALL_TIMEOUT_SECONDS = 15L
    }
}
