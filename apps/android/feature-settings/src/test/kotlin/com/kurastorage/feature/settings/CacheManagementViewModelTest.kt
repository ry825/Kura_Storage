package com.kurastorage.feature.settings

import com.kurastorage.core.data.media.AdminMediaCacheRepository
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.AdminMediaCacheStatus
import com.kurastorage.core.model.media.MediaCleanupRun
import com.kurastorage.core.model.media.MediaCleanupRunStatus
import com.kurastorage.core.model.media.MediaCleanupTrigger
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.io.IOException
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class CacheManagementViewModelTest {
    private val dispatcher = StandardTestDispatcher()

    @Before fun setUp() = Dispatchers.setMain(dispatcher)

    @After fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `accepted cleanup polls server until terminal status`() =
        runTest(dispatcher) {
            val repository = FakeRepository(mutableListOf(status(PENDING), status(RUNNING), status(COMPLETED)))
            val model = CacheManagementViewModel(repository, pollDelay = {}, maximumPolls = 5)
            dispatcher.scheduler.advanceUntilIdle()

            model.requestCleanup()
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(
                MediaCleanupRunStatus.COMPLETED,
                model.state.value.status
                    ?.lastCleanupRun
                    ?.status,
            )
            assertFalse(model.state.value.requestingCleanup)
            assertEquals(1, repository.cleanupCalls)
        }

    @Test
    fun `unknown post outcome is not presented as success and can be retried`() =
        runTest(dispatcher) {
            val repository = FakeRepository(mutableListOf(status(PENDING))).apply { failCleanup = true }
            val model = CacheManagementViewModel(repository, pollDelay = {}, maximumPolls = 1)
            dispatcher.scheduler.advanceUntilIdle()

            model.requestCleanup()
            dispatcher.scheduler.advanceUntilIdle()

            assertTrue(model.state.value.unknownCleanupOutcome)
            assertTrue(
                model.state.value.error
                    .orEmpty()
                    .contains("unknown"),
            )
            assertEquals(
                MediaCleanupRunStatus.PENDING,
                model.state.value.status
                    ?.lastCleanupRun
                    ?.status,
            )
        }

    @Test
    fun `failed cleanup is terminal and remains server authoritative`() =
        runTest(dispatcher) {
            val repository = FakeRepository(mutableListOf(status(PENDING), status(MediaCleanupRunStatus.FAILED)))
            val model = CacheManagementViewModel(repository, pollDelay = {}, maximumPolls = 5)
            dispatcher.scheduler.advanceUntilIdle()

            model.requestCleanup()
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(
                MediaCleanupRunStatus.FAILED,
                model.state.value.status
                    ?.lastCleanupRun
                    ?.status,
            )
            assertFalse(model.state.value.requestingCleanup)
        }

    @Test
    fun `forbidden response exposes no cache status or action`() =
        runTest(dispatcher) {
            val repository = FakeRepository(mutableListOf(status(PENDING))).apply { forbidden = true }
            val model = CacheManagementViewModel(repository)
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(CacheAccessState.FORBIDDEN, model.state.value.access)
            assertEquals(null, model.state.value.status)
            assertTrue(
                model.state.value.error
                    .orEmpty()
                    .contains("administrators only"),
            )
        }

    private class FakeRepository(
        private val statuses: MutableList<AdminMediaCacheStatus>,
    ) : AdminMediaCacheRepository {
        var cleanupCalls = 0
        var failCleanup = false
        var forbidden = false
        var unknown = false

        override suspend fun get(): AdminMediaCacheStatus {
            if (forbidden) {
                throw KuraStorageException.Api(ApiError(ErrorCode.UNKNOWN, null, 403))
            }
            return if (statuses.size > 1) statuses.removeAt(0) else statuses.first()
        }

        override suspend fun requestCleanup(): MediaCleanupRun {
            cleanupCalls++
            if (failCleanup) {
                unknown = true
                throw KuraStorageException.Network(IOException("unknown"))
            }
            return run(PENDING)
        }

        override fun hasUnknownCleanupOutcome() = unknown
    }

    private companion object {
        val PENDING = MediaCleanupRunStatus.PENDING
        val RUNNING = MediaCleanupRunStatus.RUNNING
        val COMPLETED = MediaCleanupRunStatus.COMPLETED

        fun run(state: MediaCleanupRunStatus) =
            MediaCleanupRun(
                "22222222-2222-2222-2222-222222222222",
                MediaCleanupTrigger.MANUAL,
                state,
                Instant.EPOCH,
                null,
                if (state == COMPLETED) Instant.EPOCH else null,
                0,
                0,
                0,
                0,
                0,
                null,
            )

        fun status(state: MediaCleanupRunStatus) =
            AdminMediaCacheStatus(
                10,
                1,
                2,
                3,
                4,
                100,
                60,
                0,
                0,
                0,
                if (state == PENDING) 1 else 0,
                if (state == RUNNING) 1 else 0,
                run(state),
            )
    }
}
