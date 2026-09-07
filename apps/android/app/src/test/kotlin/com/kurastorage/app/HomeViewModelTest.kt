package com.kurastorage.app

import com.kurastorage.core.data.RecentFileRepository
import com.kurastorage.core.data.RecentRecordOutcome
import com.kurastorage.core.data.StorageCapacityRepository
import com.kurastorage.core.data.backup.BackupProgressSnapshot
import com.kurastorage.core.data.backup.BackupStateRepository
import com.kurastorage.core.model.RecentFilePage
import com.kurastorage.core.model.StorageCapacityStatus
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.LocalSyncItem
import com.kurastorage.core.model.backup.LocalSyncItemId
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

@OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
class HomeViewModelTest {
    @After
    fun resetMainDispatcher() {
        Dispatchers.resetMain()
    }

    @Test
    fun `capacity loads independently and retry preserves the last known value`() =
        runTest {
            Dispatchers.setMain(StandardTestDispatcher(testScheduler))
            val capacity =
                FakeCapacity(
                    ArrayDeque(
                        listOf(
                            Result.success(StorageCapacityStatus("AVAILABLE", 1_000, 600, 400)),
                            Result.failure(IllegalStateException("offline")),
                            Result.success(StorageCapacityStatus("AVAILABLE", 2_000, 750, 1_250)),
                        ),
                    ),
                )
            val viewModel = HomeViewModel(EmptyRecent(), capacity, EmptyBackup(), SCOPE)

            runCurrent()
            assertEquals(
                600L,
                viewModel.state.value.capacity
                    ?.usedBytes,
            )
            assertFalse(viewModel.state.value.capacityError)

            viewModel.refreshCapacity()
            runCurrent()
            assertEquals(
                600L,
                viewModel.state.value.capacity
                    ?.usedBytes,
            )
            assertTrue(viewModel.state.value.capacityError)

            viewModel.refreshCapacity()
            runCurrent()
            assertEquals(
                750L,
                viewModel.state.value.capacity
                    ?.usedBytes,
            )
            assertFalse(viewModel.state.value.capacityError)
            assertEquals(3, capacity.calls)
        }

    @Test
    fun `connection recovery retries capacity only after a failure`() =
        runTest {
            Dispatchers.setMain(StandardTestDispatcher(testScheduler))
            val recovery = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
            val capacity =
                FakeCapacity(
                    ArrayDeque(
                        listOf(
                            Result.failure(IllegalStateException("offline")),
                            Result.success(StorageCapacityStatus("AVAILABLE", 2_000, 750, 1_250)),
                        ),
                    ),
                )
            val viewModel = HomeViewModel(EmptyRecent(), capacity, EmptyBackup(), SCOPE, recovery)
            runCurrent()
            assertTrue(viewModel.state.value.capacityError)

            recovery.emit(Unit)
            runCurrent()

            assertEquals(2, capacity.calls)
            assertFalse(viewModel.state.value.capacityError)
            assertEquals(
                750L,
                viewModel.state.value.capacity
                    ?.usedBytes,
            )
        }

    private class FakeCapacity(
        private val results: ArrayDeque<Result<StorageCapacityStatus>>,
    ) : StorageCapacityRepository {
        var calls = 0

        override suspend fun get(): StorageCapacityStatus {
            calls++
            return results.removeFirst().getOrThrow()
        }
    }

    private class EmptyRecent : RecentFileRepository {
        override suspend fun list(
            page: Int,
            pageSize: Int,
        ) = RecentFilePage(emptyList(), page, pageSize, 0)

        override suspend fun record(fileId: String): RecentRecordOutcome = RecentRecordOutcome.Confirmed
    }

    private class EmptyBackup : BackupStateRepository {
        override fun observeItems(
            accountScopeId: AccountScopeId,
            limit: Int,
        ) = flowOf(emptyList<LocalSyncItem>())

        override fun observeProgress(accountScopeId: AccountScopeId) =
            flowOf(BackupProgressSnapshot(emptyMap(), emptyMap(), emptyMap(), null))

        override suspend fun retryFailed(
            accountScopeId: AccountScopeId,
            itemId: LocalSyncItemId,
        ) = false

        override suspend fun retryAllFailed(accountScopeId: AccountScopeId) = 0
    }

    private companion object {
        val SCOPE = AccountScopeId("a".repeat(64))
    }
}
