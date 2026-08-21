package com.kurastorage.feature.files

import com.kurastorage.core.data.AdminStorageRepository
import com.kurastorage.core.model.AdminStorageStatus
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class AdminStorageViewModelTest {
    private val dispatcher = UnconfinedTestDispatcher()

    @Before fun setUp() = Dispatchers.setMain(dispatcher)

    @After fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `warning unavailable failure normal and member absence stay independent`() =
        runTest(dispatcher) {
            val repository = FakeRepository(status(capacityWarning = true))
            val viewModel = AdminStorageViewModel(repository)
            assertEquals(
                true,
                viewModel.state.value.status
                    ?.capacityWarning,
            )

            repository.value = status(storage = "UNAVAILABLE", capacityWarning = null)
            viewModel.refresh()
            assertEquals(
                "UNAVAILABLE",
                viewModel.state.value.status
                    ?.storage,
            )

            repository.failure = IllegalStateException("status failed")
            viewModel.refresh()
            assertEquals(true, viewModel.state.value.error)

            repository.failure = null
            repository.value = status(capacityWarning = false)
            viewModel.refresh()
            assertEquals(
                false,
                viewModel.state.value.status
                    ?.capacityWarning,
            )

            repository.value = null
            viewModel.refresh()
            assertNull(viewModel.state.value.status)
            assertFalse(viewModel.state.value.visible)
        }

    @Test
    fun `byte formatting does not alter exact warning inputs`() {
        assertEquals("10.0 GiB", formatBytes(10_737_418_240))
        assertEquals("unknown", formatBytes(null))
    }

    private class FakeRepository(
        var value: AdminStorageStatus?,
    ) : AdminStorageRepository {
        var failure: Throwable? = null

        override suspend fun get(): AdminStorageStatus? {
            failure?.let { throw it }
            return value
        }
    }

    private companion object {
        fun status(
            storage: String = "AVAILABLE",
            capacityWarning: Boolean? = false,
        ) = AdminStorageStatus(
            storage = storage,
            totalBytes = 100,
            availableBytes = 10,
            capacityWarningThresholdBytes = 20,
            capacityWarning = capacityWarning,
            trashBytes = 5,
            expiredTrashRootCount = 1,
            retentionDays = 30,
            recoveryRequiredPurgeCount = 0,
            lastPurgeRun = null,
        )
    }
}
