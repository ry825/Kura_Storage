package com.kurastorage.app

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.os.BatteryManager
import android.os.Build
import com.kurastorage.core.data.backup.AndroidBackupContentSource
import com.kurastorage.core.data.backup.AndroidBackupDocumentSource
import com.kurastorage.core.data.backup.AndroidCurrentWifiSource
import com.kurastorage.core.data.backup.AndroidDocumentChecksumSource
import com.kurastorage.core.data.backup.AndroidPersistableSourcePermissionController
import com.kurastorage.core.data.backup.BackupConnectionSnapshot
import com.kurastorage.core.data.backup.BackupContentSource
import com.kurastorage.core.data.backup.BackupExecutionMode
import com.kurastorage.core.data.backup.BackupPolicyContext
import com.kurastorage.core.data.backup.BackupPolicyDecision
import com.kurastorage.core.data.backup.BackupPolicyProvider
import com.kurastorage.core.data.backup.BackupPowerSnapshot
import com.kurastorage.core.data.backup.BackupRemoteDataSource
import com.kurastorage.core.data.backup.BackupRemoteException
import com.kurastorage.core.data.backup.BackupRemoteFailureKind
import com.kurastorage.core.data.backup.BackupScanCoordinator
import com.kurastorage.core.data.backup.BackupTransferBatchResult
import com.kurastorage.core.data.backup.BackupTransferRepository
import com.kurastorage.core.data.backup.BaseNetworkTransport
import com.kurastorage.core.data.backup.NetworkPolicyEvaluator
import com.kurastorage.core.data.backup.RoomBackupScanStore
import com.kurastorage.core.data.backup.RoomBackupTransferStore
import com.kurastorage.core.data.backup.RoomExternalWifiPolicyRepository
import com.kurastorage.core.data.backup.ScanTrigger
import com.kurastorage.core.database.backup.BackupDatabaseAccess
import com.kurastorage.core.database.backup.BackupEntityMapper
import com.kurastorage.core.database.backup.BackupScanPersistence
import com.kurastorage.core.database.backup.BackupTransferPersistence
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.BackupWaitReason
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.network.AndroidLocalNetworkSource
import com.kurastorage.core.network.ConnectionDetector
import com.kurastorage.feature.backup.BackupForegroundEstimate
import com.kurastorage.feature.backup.BackupWorkerRuntime
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.atomic.AtomicReference

private const val PERCENT_SCALE = 100

internal interface BackupSessionFactory {
    fun create(route: ConnectionRoute): BackupSessionServices

    suspend fun hasStoredCredential(): Boolean
}

internal data class BackupSessionServices(
    val remote: BackupRemoteDataSource,
    val hasStoredCredential: suspend () -> Boolean,
)

internal class AndroidBackupRuntimeFactory(
    private val context: Context,
    private val database: BackupDatabaseAccess,
    private val connectivityManager: ConnectivityManager,
    private val localNetworkSource: AndroidLocalNetworkSource,
    private val connectionDetector: ConnectionDetector,
    private val sessionFactory: BackupSessionFactory,
) {
    private val persistence = BackupTransferPersistence(database)
    private val sourcePermission = AndroidPersistableSourcePermissionController(context.contentResolver)
    private val wifi =
        RoomExternalWifiPolicyRepository(
            database.externalWifiPolicyDao(),
            AndroidCurrentWifiSource(
                context,
                connectivityManager,
                context.getSystemService(android.net.wifi.WifiManager::class.java),
                context.getSystemService(android.location.LocationManager::class.java),
            ),
        )
    private val policyEvaluator = NetworkPolicyEvaluator(wifi::matchesCurrentWifi)
    private val contentSource: BackupContentSource = AndroidBackupContentSource(context.contentResolver)
    private val scanCoordinator =
        BackupScanCoordinator(
            RoomBackupScanStore(BackupScanPersistence(database)),
            AndroidBackupDocumentSource(context, context.contentResolver),
            AndroidDocumentChecksumSource(context.contentResolver),
        )
    private val generation = AtomicLong()
    private val lastConnectionSignature = AtomicReference<String?>()

    fun create(scope: AccountScopeId): BackupWorkerRuntime =
        object : BackupWorkerRuntime {
            override suspend fun scan(
                scope: AccountScopeId,
                ruleId: BackupRuleId,
                trigger: ScanTrigger,
            ) {
                val entity = requireNotNull(database.backupRuleDao().find(ruleId.value, scope.value))
                scanCoordinator.scan(BackupEntityMapper.toModel(entity), trigger)
            }

            override suspend fun transfer(scope: AccountScopeId) = transferScope(scope)

            override suspend fun foregroundEstimate(scope: AccountScopeId): BackupForegroundEstimate {
                val estimate = database.localSyncItemDao().pendingEstimate(scope.value)
                return BackupForegroundEstimate(estimate.itemCount, estimate.byteCount)
            }
        }

    private suspend fun transferScope(scope: AccountScopeId): BackupTransferBatchResult {
        val initialStatus = connectionDetector.detect()
        val initialRoute = (initialStatus as? ConnectionStatus.Connected)?.route
        val session = initialRoute?.let(sessionFactory::create)
        val remote = session?.remote ?: UnavailableRemote
        return BackupTransferRepository(
            RoomBackupTransferStore(persistence),
            remote,
            contentSource,
            BackupPolicyProvider { rule -> evaluate(rule, initialRoute, session) },
        ).transfer(scope)
    }

    private suspend fun evaluate(
        rule: LocalBackupRule,
        fixedRoute: ConnectionRoute?,
        session: BackupSessionServices?,
    ): BackupPolicyDecision {
        val status = connectionDetector.detect()
        val transport = baseTransport()
        val boundNetworkId = localNetworkSource.currentBaseNetwork()?.networkId
        val signature = "$status:$transport:$boundNetworkId"
        val previous = lastConnectionSignature.getAndSet(signature)
        if (previous != signature) generation.incrementAndGet()
        val decision =
            policyEvaluator.evaluate(
                BackupPolicyContext(
                    rule = rule,
                    sourcePermissionGranted = hasSourcePermission(rule),
                    authenticated =
                        session?.hasStoredCredential?.invoke()
                            ?: sessionFactory.hasStoredCredential(),
                    connection = BackupConnectionSnapshot(status, generation.get(), transport, boundNetworkId),
                    power = power(),
                    currentWifi = wifi.currentWifi(),
                    externalWifiPolicies = persistence.externalWifiPolicies(rule.accountScopeId),
                ),
            )
        return if (decision.mode == BackupExecutionMode.AUTO_BACKUP_ALLOWED && decision.route != fixedRoute) {
            BackupPolicyDecision(
                BackupExecutionMode.BLOCKED,
                BackupWaitReason.NETWORK,
                connectionGeneration = decision.connectionGeneration,
            )
        } else {
            decision
        }
    }

    private fun hasSourcePermission(rule: LocalBackupRule): Boolean {
        if (rule.sourceType == BackupSourceType.SAF_TREE) return sourcePermission.hasReadPermission(rule.sourceLocator)
        val permission =
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                when (rule.sourceType) {
                    BackupSourceType.MEDIA_IMAGES -> Manifest.permission.READ_MEDIA_IMAGES
                    BackupSourceType.MEDIA_VIDEOS -> Manifest.permission.READ_MEDIA_VIDEO
                    BackupSourceType.MEDIA_AUDIO -> Manifest.permission.READ_MEDIA_AUDIO
                    BackupSourceType.SAF_TREE -> error("Handled above")
                }
            } else {
                Manifest.permission.READ_EXTERNAL_STORAGE
            }
        return context.checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED
    }

    @Suppress("DEPRECATION")
    private fun baseTransport(): BaseNetworkTransport {
        val capabilities =
            connectivityManager.allNetworks
                .mapNotNull(connectivityManager::getNetworkCapabilities)
                .filterNot { it.hasTransport(NetworkCapabilities.TRANSPORT_VPN) }
        return when {
            capabilities.any { it.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) } -> BaseNetworkTransport.WIFI
            capabilities.any { it.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET) } ->
                BaseNetworkTransport.ETHERNET
            capabilities.any { it.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) } -> BaseNetworkTransport.MOBILE
            else -> BaseNetworkTransport.NONE
        }
    }

    private fun power(): BackupPowerSnapshot {
        val battery = context.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        val level = battery?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) ?: -1
        val scale = battery?.getIntExtra(BatteryManager.EXTRA_SCALE, -1) ?: -1
        val percentage =
            if (level >= 0 && scale > 0) {
                (level * PERCENT_SCALE / scale).coerceIn(0, PERCENT_SCALE)
            } else {
                0
            }
        val status = battery?.getIntExtra(BatteryManager.EXTRA_STATUS, -1)
        return BackupPowerSnapshot(
            percentage,
            status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL,
        )
    }

    private object UnavailableRemote : BackupRemoteDataSource {
        private fun unavailable(): Nothing = throw BackupRemoteException(BackupRemoteFailureKind.TRANSIENT)

        override suspend fun compare(
            destinationFolderId: String,
            candidates: List<com.kurastorage.core.data.backup.BackupCompareCandidate>,
        ) = unavailable()

        override suspend fun createSession(
            item: com.kurastorage.core.model.backup.LocalSyncItem,
            destinationFolderId: String,
            idempotencyKey: String,
            decision: com.kurastorage.core.data.backup.BackupUploadDecision,
        ) = unavailable()

        override suspend fun session(sessionId: String) = unavailable()

        override suspend fun uploadChunk(
            sessionId: String,
            offset: Long,
            bytes: ByteArray,
        ) = unavailable()

        override suspend fun complete(sessionId: String) = unavailable()
    }
}
