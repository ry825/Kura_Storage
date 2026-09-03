package com.kurastorage.feature.backup

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.work.Configuration
import androidx.work.Data
import androidx.work.WorkManager
import androidx.work.testing.SynchronousExecutor
import androidx.work.testing.TestListenableWorkerBuilder
import androidx.work.testing.WorkManagerTestInitHelper
import com.kurastorage.core.data.backup.BackupTransferBatchResult
import com.kurastorage.core.data.backup.ScanTrigger
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupRuleId
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class BackupWorkInstrumentedTest {
    private lateinit var context: Context

    @Before
    fun setup() {
        context = ApplicationProvider.getApplicationContext()
        WorkManagerTestInitHelper.initializeTestWorkManager(
            context,
            Configuration.Builder().setExecutor(SynchronousExecutor()).build(),
        )
    }

    @After
    fun cleanup() {
        BackupWorkerRuntimeRegistry.clear()
    }

    @Test
    fun workerResolvesRuntimeAfterProcessStyleRecreationAndFailsClosedWhenUnavailable() =
        runBlocking {
            val input = Data.Builder().putString("account_scope", SCOPE.value).build()
            val unavailable = TestListenableWorkerBuilder<BackupTransferWorker>(context).setInputData(input).build()
            assertTrue(unavailable.doWork() is androidx.work.ListenableWorker.Result.Failure)

            val runtime = RecordingRuntime()
            BackupWorkerRuntimeRegistry.install(BackupWorkerRuntimeProvider { runtime })
            val restored = TestListenableWorkerBuilder<BackupTransferWorker>(context).setInputData(input).build()
            assertTrue(restored.doWork() is androidx.work.ListenableWorker.Result.Success)
            assertEquals(1, runtime.transferCalls)
        }

    @Test
    fun duplicateTriggersConvergeOnUniqueWorkAndTransientFailureUsesOsRetry() =
        runBlocking {
            val manager = WorkManager.getInstance(context)
            val enqueuer = WorkManagerBackupEnqueuer(manager)
            val rule = BackupRuleId("11111111-1111-1111-1111-111111111111")
            repeat(2) {
                enqueuer.enqueueScan(SCOPE, rule, ScanTrigger.CONTENT_CHANGED)
                enqueuer.enqueueTransfer(SCOPE)
            }

            assertEquals(1, manager.getWorkInfosForUniqueWorkFlow(BackupWorkNames.scan(SCOPE, rule)).first().size)
            assertEquals(2, manager.getWorkInfosForUniqueWorkFlow(BackupWorkNames.transfer(SCOPE)).first().size)

            val runtime = RecordingRuntime(retryRecommended = true)
            BackupWorkerRuntimeRegistry.install(BackupWorkerRuntimeProvider { runtime })
            val input = Data.Builder().putString("account_scope", SCOPE.value).build()
            val worker = TestListenableWorkerBuilder<BackupTransferWorker>(context).setInputData(input).build()
            assertTrue(worker.doWork() is androidx.work.ListenableWorker.Result.Retry)
        }

    private companion object {
        val SCOPE = AccountScopeId("a".repeat(64))
    }
}

private class RecordingRuntime(
    private val retryRecommended: Boolean = false,
) : BackupWorkerRuntime {
    var transferCalls = 0

    override suspend fun scan(
        scope: AccountScopeId,
        ruleId: BackupRuleId,
        trigger: ScanTrigger,
    ) = Unit

    override suspend fun transfer(scope: AccountScopeId): BackupTransferBatchResult {
        transferCalls++
        return BackupTransferBatchResult(1, 1, 1, false, retryRecommended)
    }

    override suspend fun foregroundEstimate(scope: AccountScopeId) = BackupForegroundEstimate(1, 1)
}
