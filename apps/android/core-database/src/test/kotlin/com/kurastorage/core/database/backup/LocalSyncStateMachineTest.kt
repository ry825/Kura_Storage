package com.kurastorage.core.database.backup

import com.kurastorage.core.model.backup.SyncLifecycleState
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class LocalSyncStateMachineTest {
    @Test
    fun permitsUploadLifecycleAndRecovery() {
        assertTrue(LocalSyncStateMachine.canTransition(SyncLifecycleState.PENDING, SyncLifecycleState.COMPARING))
        assertTrue(
            LocalSyncStateMachine.canTransition(
                SyncLifecycleState.COMPARING,
                SyncLifecycleState.READY_TO_UPLOAD,
            ),
        )
        assertTrue(LocalSyncStateMachine.canTransition(SyncLifecycleState.UPLOADING, SyncLifecycleState.PENDING))
    }

    @Test
    fun rejectsSkippingCompareAndResurrectingMissingAsCompleted() {
        assertFalse(LocalSyncStateMachine.canTransition(SyncLifecycleState.PENDING, SyncLifecycleState.UPLOADING))
        assertFalse(LocalSyncStateMachine.canTransition(SyncLifecycleState.LOCAL_MISSING, SyncLifecycleState.COMPLETED))
    }

    @Test
    fun requireTransitionAcceptsSupportedAndRejectsUnsupportedChanges() {
        LocalSyncStateMachine.requireTransition(SyncLifecycleState.PENDING, SyncLifecycleState.COMPARING)
        assertThrows(IllegalArgumentException::class.java) {
            LocalSyncStateMachine.requireTransition(SyncLifecycleState.PENDING, SyncLifecycleState.UPLOADING)
        }
    }

    @Test
    fun unknownPersistedStateFailsClosed() {
        assertThrows(IllegalArgumentException::class.java) { LocalSyncStateMachine.parse("UNKNOWN_NEW_STATE") }
    }
}
