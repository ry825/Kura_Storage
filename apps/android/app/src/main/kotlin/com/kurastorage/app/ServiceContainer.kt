package com.kurastorage.app

import android.content.Context
import android.net.ConnectivityManager
import com.kurastorage.core.data.AndroidContentStreamProvider
import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.data.DataStoreCredentialMetadataStore
import com.kurastorage.core.data.DefaultAdminStorageRepository
import com.kurastorage.core.data.DefaultAuthenticationRepository
import com.kurastorage.core.data.DefaultFileRepository
import com.kurastorage.core.data.DefaultOrganizationRepository
import com.kurastorage.core.data.DefaultRecentFileRepository
import com.kurastorage.core.data.DefaultSearchRepository
import com.kurastorage.core.data.DefaultSharingRepository
import com.kurastorage.core.data.DefaultTransferRepository
import com.kurastorage.core.data.media.AndroidNetworkTransportSource
import com.kurastorage.core.data.media.DataStoreQualityPreferenceStore
import com.kurastorage.core.data.media.DefaultMediaRepository
import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.data.media.NetworkQualityContextResolver
import com.kurastorage.core.data.media.QualityPreferenceStore
import com.kurastorage.core.data.media.TransferConfirmationPolicy
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.network.AndroidHealthProbe
import com.kurastorage.core.network.AndroidLocalNetworkSource
import com.kurastorage.core.network.ConnectionDetector
import com.kurastorage.core.network.FixedAddressDns
import com.kurastorage.core.network.KuraStorageApi
import com.kurastorage.core.network.media.OkHttpMediaApi
import com.kurastorage.core.security.AndroidKeystoreCredentialCipher
import com.kurastorage.core.security.SharedPreferencesEncryptedTokenStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import okhttp3.OkHttpClient
import java.io.Closeable
import java.time.Duration
import java.util.UUID

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
    private val qualityPreferenceStore: QualityPreferenceStore =
        DataStoreQualityPreferenceStore(applicationContext)
    private val networkQualityContextResolver =
        NetworkQualityContextResolver(
            AndroidNetworkTransportSource(
                applicationContext.getSystemService(ConnectivityManager::class.java),
            ),
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
            sharing = DefaultSharingRepository(api, executor),
            search = DefaultSearchRepository(api, executor),
            recentFiles = DefaultRecentFileRepository(api, executor),
            organization = DefaultOrganizationRepository(api, executor),
            adminStorage = DefaultAdminStorageRepository(api, executor, auth),
            transfers =
                DefaultTransferRepository(
                    api,
                    executor,
                    AndroidContentStreamProvider(applicationContext),
                ),
            qualityPreferences = qualityPreferenceStore,
            media = createMediaSession(apiClient, executor),
        )
    }

    private fun createMediaSession(
        apiClient: OkHttpClient,
        executor: AuthenticatedRequestExecutor,
    ): MediaSessionScope {
        val repository =
            DefaultMediaRepository(
                OkHttpMediaApi("https://${BuildConfig.API_HOSTNAME}/api/v1", apiClient),
                executor,
            )
        return MediaSessionScope(
            scopeId = UUID.randomUUID().toString(),
            repository = repository,
            qualityPreferences = qualityPreferenceStore,
            contextResolver = networkQualityContextResolver,
            confirmationPolicy = TransferConfirmationPolicy(repository),
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
    val sharing: DefaultSharingRepository,
    val search: DefaultSearchRepository,
    val recentFiles: DefaultRecentFileRepository,
    val organization: DefaultOrganizationRepository,
    val adminStorage: DefaultAdminStorageRepository,
    val transfers: DefaultTransferRepository,
    val qualityPreferences: QualityPreferenceStore,
    val media: MediaSessionScope,
) : Closeable {
    override fun close() = media.close()
}

class MediaSessionScope(
    val scopeId: String,
    val repository: MediaRepository,
    val qualityPreferences: QualityPreferenceStore,
    val contextResolver: NetworkQualityContextResolver,
    val confirmationPolicy: TransferConfirmationPolicy,
) : Closeable {
    val coroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    override fun close() {
        coroutineScope.cancel()
    }
}
