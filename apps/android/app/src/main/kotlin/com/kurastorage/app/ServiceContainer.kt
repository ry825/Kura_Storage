package com.kurastorage.app

import android.content.Context
import android.net.ConnectivityManager
import com.kurastorage.core.data.AndroidContentStreamProvider
import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.data.DataStoreCredentialMetadataStore
import com.kurastorage.core.data.DefaultAdminStorageRepository
import com.kurastorage.core.data.DefaultAuthenticationRepository
import com.kurastorage.core.data.DefaultFileRepository
import com.kurastorage.core.data.DefaultTransferRepository
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

    fun sessionServices(route: ConnectionRoute): SessionServices {
        val address =
            when (route) {
                ConnectionRoute.LOCAL_DIRECT -> BuildConfig.LAN_API_ADDRESS
                ConnectionRoute.REMOTE_SECURE -> BuildConfig.ZEROTIER_API_ADDRESS
            }
        val apiClient =
            baseClient
                .newBuilder()
                .dns(FixedAddressDns(BuildConfig.API_HOSTNAME, address))
                .readTimeout(Duration.ofMinutes(TRANSFER_TIMEOUT_MINUTES))
                .callTimeout(Duration.ZERO)
                .apply {
                    if (route == ConnectionRoute.LOCAL_DIRECT) {
                        socketFactory(localNetworkSource.refreshingSocketFactory())
                    }
                }.build()
        val api =
            KuraStorageApi(
                baseUrl = "https://${BuildConfig.API_HOSTNAME}/api/v1",
                client = apiClient,
            )
        val auth =
            DefaultAuthenticationRepository(
                api = api,
                metadataStore = DataStoreCredentialMetadataStore(applicationContext),
                tokenStore =
                    SharedPreferencesEncryptedTokenStore(
                        applicationContext,
                        AndroidKeystoreCredentialCipher(),
                    ),
            )
        val executor = AuthenticatedRequestExecutor(auth)
        return SessionServices(
            authentication = auth,
            files = DefaultFileRepository(api, executor),
            adminStorage = DefaultAdminStorageRepository(api, executor, auth),
            transfers =
                DefaultTransferRepository(
                    api,
                    executor,
                    AndroidContentStreamProvider(applicationContext),
                ),
        )
    }

    private companion object {
        const val CONNECT_TIMEOUT_SECONDS = 5L
        const val READ_TIMEOUT_SECONDS = 10L
        const val CALL_TIMEOUT_SECONDS = 15L
        const val TRANSFER_TIMEOUT_MINUTES = 30L
    }
}

data class SessionServices(
    val authentication: DefaultAuthenticationRepository,
    val files: DefaultFileRepository,
    val adminStorage: DefaultAdminStorageRepository,
    val transfers: DefaultTransferRepository,
)
