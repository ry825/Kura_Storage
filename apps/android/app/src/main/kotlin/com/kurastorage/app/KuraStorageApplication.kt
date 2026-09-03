package com.kurastorage.app

import android.app.Application
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.feature.backup.BackupWorkerRuntime
import com.kurastorage.feature.backup.BackupWorkerRuntimeOwner

class KuraStorageApplication :
    Application(),
    BackupWorkerRuntimeOwner {
    val container: ServiceContainer by lazy { ServiceContainer(this) }

    override fun backupRuntime(scope: AccountScopeId): BackupWorkerRuntime = container.backupRuntime(scope)
}
