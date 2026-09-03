package com.kurastorage.feature.backup

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.Data
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.ForegroundInfo
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import com.kurastorage.core.data.backup.BackupTransferBatchResult
import com.kurastorage.core.data.backup.ScanTrigger
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupRuleId
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

private const val SAF_PERIODIC_HOURS = 6L
private const val FOREGROUND_BYTES = 100L * 1024 * 1024
private const val FOREGROUND_ITEM_COUNT = 100
private const val MINIMUM_BACKOFF_SECONDS = 10L
private const val FOREGROUND_CHANNEL = "automatic_backup"
private const val FOREGROUND_NOTIFICATION_ID = 0x4b55
private const val KEY_SCOPE = "account_scope"
private const val KEY_RULE = "rule_id"
private const val KEY_TRIGGER = "scan_trigger"

data class BackupBackgroundCapability(
    val workManagerAvailable: Boolean = true,
    val requiresAppRestartAfterForceStop: Boolean = true,
    val usesAlwaysOnForegroundService: Boolean = false,
)

data class BackupForegroundEstimate(
    val itemCount: Int,
    val byteCount: Long,
) {
    val requiresForeground: Boolean get() = itemCount >= FOREGROUND_ITEM_COUNT || byteCount >= FOREGROUND_BYTES
}

interface BackupWorkerRuntime {
    suspend fun scan(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
        trigger: ScanTrigger,
    )

    suspend fun transfer(scope: AccountScopeId): BackupTransferBatchResult

    suspend fun foregroundEstimate(scope: AccountScopeId): BackupForegroundEstimate
}

interface BackupWorkerRuntimeOwner {
    fun backupRuntime(scope: AccountScopeId): BackupWorkerRuntime?
}

fun interface BackupWorkerRuntimeProvider {
    fun resolve(scope: AccountScopeId): BackupWorkerRuntime?
}

object BackupWorkerRuntimeRegistry {
    private val provider = AtomicReference<BackupWorkerRuntimeProvider?>()

    fun install(value: BackupWorkerRuntimeProvider) {
        provider.set(value)
    }

    fun clear() {
        provider.set(null)
    }

    fun resolve(scope: AccountScopeId): BackupWorkerRuntime? = provider.get()?.resolve(scope)
}

interface BackupWorkEnqueuer {
    fun enqueueScan(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
        trigger: ScanTrigger,
    )

    fun enqueuePeriodicSafScan(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
    )

    fun enqueueTransfer(scope: AccountScopeId)
}

class WorkManagerBackupEnqueuer(
    private val workManager: WorkManager,
) : BackupWorkEnqueuer {
    private val connected = Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()

    override fun enqueueScan(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
        trigger: ScanTrigger,
    ) {
        val request =
            OneTimeWorkRequestBuilder<BackupScanWorker>()
                .setInputData(scanData(scope, ruleId, trigger))
                .build()
        workManager.enqueueUniqueWork(BackupWorkNames.scan(scope, ruleId), ExistingWorkPolicy.KEEP, request)
    }

    override fun enqueuePeriodicSafScan(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
    ) {
        val request =
            PeriodicWorkRequestBuilder<BackupScanWorker>(SAF_PERIODIC_HOURS, TimeUnit.HOURS)
                .setInputData(scanData(scope, ruleId, ScanTrigger.PERIODIC))
                .build()
        workManager.enqueueUniquePeriodicWork(
            "${BackupWorkNames.scan(scope, ruleId)}:periodic",
            ExistingPeriodicWorkPolicy.UPDATE,
            request,
        )
    }

    override fun enqueueTransfer(scope: AccountScopeId) {
        val request =
            OneTimeWorkRequestBuilder<BackupTransferWorker>()
                .setConstraints(connected)
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, MINIMUM_BACKOFF_SECONDS, TimeUnit.SECONDS)
                .setInputData(Data.Builder().putString(KEY_SCOPE, scope.value).build())
                .build()
        workManager.enqueueUniqueWork(
            BackupWorkNames.transfer(scope),
            ExistingWorkPolicy.APPEND_OR_REPLACE,
            request,
        )
    }

    private fun scanData(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
        trigger: ScanTrigger,
    ) = Data
        .Builder()
        .putString(KEY_SCOPE, scope.value)
        .putString(KEY_RULE, ruleId.value)
        .putString(KEY_TRIGGER, trigger.name)
        .build()
}

class BackupCoordinator(
    private val work: BackupWorkEnqueuer,
) {
    val backgroundCapability = BackupBackgroundCapability()

    fun onAppStarted(
        scope: AccountScopeId,
        rules: Collection<BackupRuleId>,
    ) = enqueue(scope, rules, ScanTrigger.APP_START)

    fun onContentChanged(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
    ) = enqueue(scope, listOf(ruleId), ScanTrigger.CONTENT_CHANGED)

    fun onPendingAdded(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
    ) = enqueue(scope, listOf(ruleId), ScanTrigger.PENDING_ADDED)

    fun onAllowedConnection(
        scope: AccountScopeId,
        rules: Collection<BackupRuleId>,
    ) = enqueue(scope, rules, ScanTrigger.ALLOWED_CONNECTION)

    fun runNow(
        scope: AccountScopeId,
        rules: Collection<BackupRuleId>,
    ) = enqueue(scope, rules, ScanTrigger.MANUAL)

    fun scheduleSafPeriodic(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
    ) = work.enqueuePeriodicSafScan(scope, ruleId)

    private fun enqueue(
        scope: AccountScopeId,
        rules: Collection<BackupRuleId>,
        trigger: ScanTrigger,
    ) {
        rules.distinct().forEach { work.enqueueScan(scope, it, trigger) }
        work.enqueueTransfer(scope)
    }
}

class BackupScanWorker(
    context: Context,
    parameters: WorkerParameters,
) : CoroutineWorker(context, parameters) {
    @Suppress("ReturnCount")
    override suspend fun doWork(): Result {
        val scope = inputData.scope() ?: return Result.failure()
        val rule = inputData.rule() ?: return Result.failure()
        val trigger = inputData.trigger() ?: return Result.failure()
        val runtime = resolveRuntime(scope) ?: return Result.failure()
        runtime.scan(scope, rule, trigger)
        WorkManagerBackupEnqueuer(WorkManager.getInstance(applicationContext)).enqueueTransfer(scope)
        return Result.success()
    }
}

class BackupTransferWorker(
    context: Context,
    parameters: WorkerParameters,
) : CoroutineWorker(context, parameters) {
    @Suppress("ReturnCount")
    override suspend fun doWork(): Result {
        val scope = inputData.scope() ?: return Result.failure()
        val runtime = resolveRuntime(scope) ?: return Result.failure()
        val estimate = runtime.foregroundEstimate(scope)
        if (estimate.requiresForeground) {
            if (!canPostNotifications(applicationContext)) {
                return Result.success(Data.Builder().putBoolean("notification_permission_required", true).build())
            }
            setForeground(createForegroundInfo(applicationContext, estimate))
        }
        val result = runtime.transfer(scope)
        if (result.retryRecommended) return Result.retry()
        if (result.hasRemaining) {
            WorkManagerBackupEnqueuer(WorkManager.getInstance(applicationContext)).enqueueTransfer(scope)
        }
        return Result.success(Data.Builder().putBoolean("has_remaining", result.hasRemaining).build())
    }
}

private fun CoroutineWorker.resolveRuntime(scope: AccountScopeId): BackupWorkerRuntime? =
    BackupWorkerRuntimeRegistry.resolve(scope)
        ?: (applicationContext as? BackupWorkerRuntimeOwner)?.backupRuntime(scope)

private fun Data.scope() = getString(KEY_SCOPE)?.let { runCatching { AccountScopeId(it) }.getOrNull() }

private fun Data.rule() = getString(KEY_RULE)?.let { runCatching { BackupRuleId(it) }.getOrNull() }

private fun Data.trigger() = getString(KEY_TRIGGER)?.let { runCatching { enumValueOf<ScanTrigger>(it) }.getOrNull() }

private fun canPostNotifications(context: Context): Boolean =
    Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
        ContextCompat.checkSelfPermission(
            context,
            Manifest.permission.POST_NOTIFICATIONS,
        ) == PackageManager.PERMISSION_GRANTED

private fun createForegroundInfo(
    context: Context,
    estimate: BackupForegroundEstimate,
): ForegroundInfo {
    val manager = context.getSystemService(NotificationManager::class.java)
    manager.createNotificationChannel(
        NotificationChannel(FOREGROUND_CHANNEL, "Automatic backup", NotificationManager.IMPORTANCE_LOW),
    )
    val notification =
        NotificationCompat
            .Builder(context, FOREGROUND_CHANNEL)
            .setSmallIcon(android.R.drawable.stat_sys_upload)
            .setContentTitle("KuraStorage backup")
            .setContentText("Backing up ${estimate.itemCount} items")
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .build()
    return ForegroundInfo(FOREGROUND_NOTIFICATION_ID, notification)
}
