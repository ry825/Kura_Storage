package com.kurastorage.app

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.kurastorage.core.model.backup.AccountScopeId
import org.junit.Assert.assertNotNull
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class BackupApplicationRuntimeTest {
    @Test
    fun applicationReconstructsBackupRuntimeWithoutAnActivity() {
        val application = ApplicationProvider.getApplicationContext<KuraStorageApplication>()

        assertNotNull(application.backupRuntime(AccountScopeId("a".repeat(64))))
    }
}
