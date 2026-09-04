package com.kurastorage.app

import android.content.Context
import android.location.LocationManager
import android.net.ConnectivityManager
import android.net.wifi.WifiManager
import coil3.ImageLoader
import com.kurastorage.core.data.AndroidContentStreamProvider
import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.data.DataStoreCredentialMetadataStore
import com.kurastorage.core.data.DefaultActivityRepository
import com.kurastorage.core.data.DefaultAdminStorageRepository
import com.kurastorage.core.data.DefaultAuthenticationRepository
import com.kurastorage.core.data.DefaultFileRepository
import com.kurastorage.core.data.DefaultOrganizationRepository
import com.kurastorage.core.data.DefaultRecentFileRepository
import com.kurastorage.core.data.DefaultSearchRepository
import com.kurastorage.core.data.DefaultSharingRepository
import com.kurastorage.core.data.DefaultTextFileRepository
import com.kurastorage.core.data.DefaultTransferRepository
import com.kurastorage.core.data.backup.AccountScopeHasher
import com.kurastorage.core.data.backup.AndroidCurrentWifiSource
import com.kurastorage.core.data.backup.AndroidPersistableSourcePermissionController
import com.kurastorage.core.data.backup.AuthenticatedBackupRemoteDataSource
import com.kurastorage.core.data.backup.FileRepositoryRemoteBackupFolderValidator
import com.kurastorage.core.data.backup.LocalBackupStateRepository
import com.kurastorage.core.data.backup.RoomBackupRuleRepository
import com.kurastorage.core.data.backup.RoomExternalWifiPolicyRepository
import com.kurastorage.core.data.media.AndroidNetworkTransportSource
import com.kurastorage.core.data.media.DataStoreQualityPreferenceStore
import com.kurastorage.core.data.media.DefaultAdminMediaCacheRepository
import com.kurastorage.core.data.media.DefaultMediaRepository
import com.kurastorage.core.data.media.MediaContentDownloader
import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.data.media.NetworkQualityContextResolver
import com.kurastorage.core.data.media.QualityPreferenceStore
import com.kurastorage.core.data.media.TemporaryPdfStore
import com.kurastorage.core.data.media.TransferConfirmationPolicy
import com.kurastorage.core.database.backup.BackupDatabaseAccess
import com.kurastorage.core.database.backup.createBackupDatabase
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.network.AndroidHealthProbe
import com.kurastorage.core.network.AndroidLocalNetworkSource
import com.kurastorage.core.network.ConnectionDetector
import com.kurastorage.core.network.FixedAddressDns
import com.kurastorage.core.network.KuraStorageApi
import com.kurastorage.core.network.media.OkHttpMediaApi
import com.kurastorage.core.security.AndroidKeystoreCredentialCipher
import com.kurastorage.core.security.SharedPreferencesEncryptedTokenStore
import com.kurastorage.feature.backup.BackupCoordinator
import com.kurastorage.feature.backup.BackupWorkerRuntime
import com.kurastorage.feature.backup.createBackupCoordinator
import com.kurastorage.feature.media.MediaImageLoaderFactory
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import okhttp3.OkHttpClient
import java.io.Closeable
import java.time.Duration
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean

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
    private val backupDatabase: BackupDatabaseAccess by lazy {
        createBackupDatabase(applicationContext)
    }
    private val qualityPreferenceStore: QualityPreferenceStore =
        DataStoreQualityPreferenceStore(applicationContext)
    private val networkQualityContextResolver =
        NetworkQualityContextResolver(
            AndroidNetworkTransportSource(
                applicationContext.getSystemService(ConnectivityManager::class.java),
            ),
        )

    init {
        MediaImageLoaderFactory.cleanupPreviousSessions(applicationContext)
        TemporaryPdfStore.cleanupPreviousSessions(applicationContext.cacheDir)
    }

    val connectionDetector =
        ConnectionDetector(
            apiHostname = BuildConfig.API_HOSTNAME,
            lanApiAddress = BuildConfig.LAN_API_ADDRESS,
            remoteApiAddress = BuildConfig.ZEROTIER_API_ADDRESS,
            localNetworkSource = localNetworkSource,
            healthProbe = AndroidHealthProbe(localNetworkSource, baseClient),
        )
    private val backupRuntimeFactory by lazy {
        AndroidBackupRuntimeFactory(
            applicationContext,
            backupDatabase,
            applicationContext.getSystemService(ConnectivityManager::class.java),
            localNetworkSource,
            connectionDetector,
            object : BackupSessionFactory {
                override fun create(route: ConnectionRoute): BackupSessionServices = backupSessionServices(route)

                override suspend fun hasStoredCredential(): Boolean =
                    createAuthentication(createApi(ConnectionRoute.REMOTE_SECURE))
                        .storedCredential() != null
            },
        )
    }

    fun backupRuntime(scope: AccountScopeId): BackupWorkerRuntime = backupRuntimeFactory.create(scope)

    fun backupUiServices(session: SessionServices): BackupUiServices {
        val userId = requireNotNull(session.authentication.userId()) { "An authenticated user is required" }
        val deviceId = requireNotNull(session.authentication.deviceId()) { "An authenticated device is required" }
        val scope = AccountScopeHasher.create(BuildConfig.API_HOSTNAME, userId, deviceId.value)
        return BackupUiServices(
            scope = scope,
            rules =
                RoomBackupRuleRepository(
                    backupDatabase.backupRuleDao(),
                    AndroidPersistableSourcePermissionController(applicationContext.contentResolver),
                    FileRepositoryRemoteBackupFolderValidator(session.files),
                ),
            wifi =
                RoomExternalWifiPolicyRepository(
                    backupDatabase.externalWifiPolicyDao(),
                    AndroidCurrentWifiSource(
                        applicationContext,
                        applicationContext.getSystemService(ConnectivityManager::class.java),
                        applicationContext.getSystemService(WifiManager::class.java),
                        applicationContext.getSystemService(LocationManager::class.java),
                    ),
                ),
            state = LocalBackupStateRepository(backupDatabase.localSyncItemDao()),
            coordinator = createBackupCoordinator(applicationContext),
        )
    }

    private fun backupSessionServices(route: ConnectionRoute): BackupSessionServices {
        val api = createApi(route)
        val auth = createAuthentication(api)
        val executor = AuthenticatedRequestExecutor(auth)
        return BackupSessionServices(
            remote = AuthenticatedBackupRemoteDataSource(api, api, executor),
            hasStoredCredential = { auth.storedCredential() != null },
        )
    }

    fun sessionServices(route: ConnectionRoute): SessionServices {
        val apiClient = createApiClient(route)
        val api = KuraStorageApi("https://${BuildConfig.API_HOSTNAME}/api/v1", apiClient)
        val auth = createAuthentication(api)
        val executor = AuthenticatedRequestExecutor(auth)
        return SessionServices(
            sessionId = UUID.randomUUID().toString(),
            authentication = auth,
            files = DefaultFileRepository(api, executor),
            textFiles = DefaultTextFileRepository(api, executor),
            sharing = DefaultSharingRepository(api, executor),
            search = DefaultSearchRepository(api, executor),
            recentFiles = DefaultRecentFileRepository(api, executor),
            organization = DefaultOrganizationRepository(api, executor),
            adminStorage = DefaultAdminStorageRepository(api, executor, auth),
            adminMediaCache = DefaultAdminMediaCacheRepository(api, executor),
            activity = DefaultActivityRepository(api, executor),
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

    private fun createApi(route: ConnectionRoute): KuraStorageApi =
        KuraStorageApi("https://${BuildConfig.API_HOSTNAME}/api/v1", createApiClient(route))

    private fun createApiClient(route: ConnectionRoute): OkHttpClient {
        val address =
            when (route) {
                ConnectionRoute.LOCAL_DIRECT -> BuildConfig.LAN_API_ADDRESS
                ConnectionRoute.REMOTE_SECURE -> BuildConfig.ZEROTIER_API_ADDRESS
            }
        return baseClient
            .newBuilder()
            .dns(FixedAddressDns(BuildConfig.API_HOSTNAME, address))
            .readTimeout(Duration.ofMinutes(TRANSFER_TIMEOUT_MINUTES))
            .callTimeout(Duration.ZERO)
            .apply {
                if (route == ConnectionRoute.LOCAL_DIRECT) {
                    socketFactory(localNetworkSource.refreshingSocketFactory())
                }
            }.build()
    }

    private fun createAuthentication(api: KuraStorageApi) =
        DefaultAuthenticationRepository(
            api = api,
            metadataStore = DataStoreCredentialMetadataStore(applicationContext),
            tokenStore =
                SharedPreferencesEncryptedTokenStore(
                    applicationContext,
                    AndroidKeystoreCredentialCipher(),
                ),
        )

    private fun createMediaSession(
        apiClient: OkHttpClient,
        executor: AuthenticatedRequestExecutor,
    ): MediaSessionScope {
        val repository =
            DefaultMediaRepository(
                OkHttpMediaApi("https://${BuildConfig.API_HOSTNAME}/api/v1", apiClient),
                executor,
            )
        val scopeId = UUID.randomUUID().toString()
        return MediaSessionScope(
            scopeId = scopeId,
            repository = repository,
            qualityPreferences = qualityPreferenceStore,
            contextResolver = networkQualityContextResolver,
            confirmationPolicy = TransferConfirmationPolicy(repository),
            downloader = MediaContentDownloader(repository),
            imageLoader = MediaImageLoaderFactory.create(applicationContext, scopeId, repository),
            temporaryPdfStore = TemporaryPdfStore(applicationContext.cacheDir, scopeId, repository),
            cleanupImageCache = { MediaImageLoaderFactory.cleanupSession(applicationContext, scopeId) },
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
    val sessionId: String,
    val authentication: DefaultAuthenticationRepository,
    val files: DefaultFileRepository,
    val textFiles: DefaultTextFileRepository,
    val sharing: DefaultSharingRepository,
    val search: DefaultSearchRepository,
    val recentFiles: DefaultRecentFileRepository,
    val organization: DefaultOrganizationRepository,
    val adminStorage: DefaultAdminStorageRepository,
    val adminMediaCache: DefaultAdminMediaCacheRepository,
    val activity: DefaultActivityRepository,
    val transfers: DefaultTransferRepository,
    val qualityPreferences: QualityPreferenceStore,
    val media: MediaSessionScope,
) : Closeable {
    override fun close() = media.close()
}

data class BackupUiServices(
    val scope: AccountScopeId,
    val rules: RoomBackupRuleRepository,
    val wifi: RoomExternalWifiPolicyRepository,
    val state: LocalBackupStateRepository,
    val coordinator: BackupCoordinator,
)

@Suppress("LongParameterList")
class MediaSessionScope(
    val scopeId: String,
    val repository: MediaRepository,
    val qualityPreferences: QualityPreferenceStore,
    val contextResolver: NetworkQualityContextResolver,
    val confirmationPolicy: TransferConfirmationPolicy,
    val downloader: MediaContentDownloader,
    val imageLoader: ImageLoader,
    val temporaryPdfStore: TemporaryPdfStore,
    private val cleanupImageCache: () -> Unit,
) : Closeable {
    val coroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val closed = AtomicBoolean(false)

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        coroutineScope.cancel()
        imageLoader.shutdown()
        cleanupImageCache()
        temporaryPdfStore.close()
    }
}
