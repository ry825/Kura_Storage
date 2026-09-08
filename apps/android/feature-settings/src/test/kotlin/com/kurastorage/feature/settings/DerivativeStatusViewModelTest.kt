package com.kurastorage.feature.settings

import com.kurastorage.core.data.media.AdminMediaDerivativeRepository
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.MediaDerivativeStatus
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Before
import org.junit.Test
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class DerivativeStatusViewModelTest {
    private val dispatcher = StandardTestDispatcher()

    @Before fun setUp() = Dispatchers.setMain(dispatcher)

    @After fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `loads derivative coverage`() =
        runTest(dispatcher) {
            val viewModel = DerivativeStatusViewModel(FakeRepository(status = status()))
            dispatcher.scheduler.advanceUntilIdle()
            assertEquals(
                4L,
                viewModel.state.value.status
                    ?.readyCount,
            )
            assertNull(viewModel.state.value.error)
        }

    @Test
    fun `reports administrator requirement for forbidden status`() =
        runTest(dispatcher) {
            val viewModel =
                DerivativeStatusViewModel(
                    FakeRepository(
                        failure =
                            KuraStorageException.Api(
                                ApiError(ErrorCode.AUTHENTICATION_REQUIRED, null, 403),
                            ),
                    ),
                )
            dispatcher.scheduler.advanceUntilIdle()
            assertEquals("Administrator access is required.", viewModel.state.value.error)
        }

    private class FakeRepository(
        private val status: MediaDerivativeStatus? = null,
        private val failure: Throwable? = null,
    ) : AdminMediaDerivativeRepository {
        override suspend fun get(): MediaDerivativeStatus = failure?.let { throw it } ?: checkNotNull(status)
    }

    private fun status() = MediaDerivativeStatus(1, 5, 4, 1, 0, 0, 0, 0, 100, 0, 0, Instant.EPOCH)
}
