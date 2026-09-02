@file:Suppress("MaxLineLength")

package com.kurastorage.feature.activity

import com.kurastorage.core.data.ActivityRepository
import com.kurastorage.core.model.ActivityDetail
import com.kurastorage.core.model.ActivityItem
import com.kurastorage.core.model.ActivityPage
import com.kurastorage.core.model.ActivityTargetType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.UserActivityType
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import kotlinx.coroutines.withContext
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.io.IOException
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class ActivityViewModelTest {
    private val dispatcher = StandardTestDispatcher()

    @Before fun setUp() = Dispatchers.setMain(dispatcher)

    @After fun tearDown() = Dispatchers.resetMain()

    @Test fun `filter cancels obsolete generation and keeps matching page`() =
        runTest(dispatcher) {
            val repository = DeferredRepository()
            val viewModel = ActivityViewModel(repository)
            runCurrent()
            viewModel.selectFilter(UserActivityType.EDIT)
            runCurrent()

            repository.complete(UserActivityType.EDIT, ActivityPage(listOf(item(UserActivityType.EDIT)), null))
            repository.complete(null, ActivityPage(listOf(item(UserActivityType.UPLOAD)), null))
            advanceUntilIdle()

            assertEquals(UserActivityType.EDIT, viewModel.state.value.filter)
            assertEquals(
                listOf(UserActivityType.EDIT),
                viewModel.state.value.items
                    .map { it.type },
            )
        }

    @Test fun `paging prevents duplicate loads and refresh recovers offline error`() =
        runTest(dispatcher) {
            val repository = PagingRepository()
            val viewModel = ActivityViewModel(repository)
            advanceUntilIdle()
            viewModel.loadMore()
            viewModel.loadMore()
            advanceUntilIdle()
            assertEquals(listOf<String?>(null, "next"), repository.cursors)

            repository.offline = true
            viewModel.refresh()
            advanceUntilIdle()
            assertTrue(
                viewModel.state.value.error
                    ?.message
                    ?.contains("offline") == true,
            )
            repository.offline = false
            viewModel.refresh()
            advanceUntilIdle()
            assertFalse(viewModel.state.value.refreshing)
            assertEquals(null, viewModel.state.value.error)
        }

    private class DeferredRepository : ActivityRepository {
        private val responses = mutableMapOf<UserActivityType?, CompletableDeferred<ActivityPage>>()

        override suspend fun list(
            type: UserActivityType?,
            cursor: String?,
            pageSize: Int,
        ): ActivityPage {
            val deferred = responses.getOrPut(type) { CompletableDeferred() }
            return try {
                deferred.await()
            } catch (_: kotlinx.coroutines.CancellationException) {
                withContext(NonCancellable) { deferred.await() }
            }
        }

        fun complete(
            type: UserActivityType?,
            page: ActivityPage,
        ) = responses.getOrPut(type) { CompletableDeferred() }.complete(page)
    }

    private class PagingRepository : ActivityRepository {
        val cursors = mutableListOf<String?>()
        var offline = false

        override suspend fun list(
            type: UserActivityType?,
            cursor: String?,
            pageSize: Int,
        ): ActivityPage {
            if (offline) throw KuraStorageException.Network(IOException("offline"))
            cursors += cursor
            return if (cursor == null) {
                ActivityPage(listOf(item(UserActivityType.UPLOAD)), "next")
            } else {
                ActivityPage(listOf(item(UserActivityType.MOVE)), null)
            }
        }
    }

    private companion object {
        fun item(type: UserActivityType) =
            ActivityItem(
                type,
                Instant.parse("2026-09-02T01:02:03Z"),
                "Alex",
                "Phone",
                if (type == UserActivityType.MOVE) ID_2 else ID,
                ActivityTargetType.FILE,
                "notes.txt",
                "Alex",
                when (type) {
                    UserActivityType.UPLOAD -> ActivityDetail.Upload(1)
                    UserActivityType.MOVE -> ActivityDetail.Move("A", "B")
                    UserActivityType.EDIT -> ActivityDetail.Edit(2, com.kurastorage.core.model.ActivityEditKind.TEXT_SAVE)
                    else -> ActivityDetail.Unsupported
                },
            )

        const val ID = "00000000-0000-4000-8000-000000000001"
        const val ID_2 = "00000000-0000-4000-8000-000000000002"
    }
}
