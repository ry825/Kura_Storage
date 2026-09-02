package com.kurastorage.core.database.backup

import com.kurastorage.core.model.backup.SyncLifecycleState

object LocalSyncStateMachine {
    private val transitions =
        mapOf(
            SyncLifecycleState.DISCOVERED to setOf(SyncLifecycleState.PENDING, SyncLifecycleState.LOCAL_MISSING),
            SyncLifecycleState.PENDING to
                setOf(
                    SyncLifecycleState.COMPARING,
                    SyncLifecycleState.FAILED,
                    SyncLifecycleState.LOCAL_MISSING,
                ),
            SyncLifecycleState.COMPARING to
                setOf(
                    SyncLifecycleState.PENDING,
                    SyncLifecycleState.READY_TO_UPLOAD,
                    SyncLifecycleState.COMPLETED,
                    SyncLifecycleState.FAILED,
                    SyncLifecycleState.LOCAL_MISSING,
                ),
            SyncLifecycleState.READY_TO_UPLOAD to
                setOf(
                    SyncLifecycleState.PENDING,
                    SyncLifecycleState.UPLOADING,
                    SyncLifecycleState.FAILED,
                    SyncLifecycleState.LOCAL_MISSING,
                ),
            SyncLifecycleState.UPLOADING to
                setOf(
                    SyncLifecycleState.PENDING,
                    SyncLifecycleState.COMPLETED,
                    SyncLifecycleState.FAILED,
                    SyncLifecycleState.LOCAL_MISSING,
                ),
            SyncLifecycleState.COMPLETED to setOf(SyncLifecycleState.PENDING, SyncLifecycleState.LOCAL_MISSING),
            SyncLifecycleState.FAILED to setOf(SyncLifecycleState.PENDING, SyncLifecycleState.LOCAL_MISSING),
            SyncLifecycleState.LOCAL_MISSING to setOf(SyncLifecycleState.PENDING),
        )

    fun parse(value: String): SyncLifecycleState =
        runCatching { enumValueOf<SyncLifecycleState>(value) }
            .getOrElse { throw IllegalArgumentException("Unknown local sync state") }

    fun canTransition(
        from: SyncLifecycleState,
        to: SyncLifecycleState,
    ): Boolean = to in transitions.getValue(from)

    fun requireTransition(
        from: SyncLifecycleState,
        to: SyncLifecycleState,
    ) {
        require(canTransition(from, to)) { "Unsupported local sync state transition" }
    }
}
