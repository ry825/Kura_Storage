package com.kurastorage.feature.search

import com.kurastorage.core.data.OrganizationRepository
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.EntryOrganizationState
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FavoriteItem
import com.kurastorage.core.model.FavoritePage
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.SearchResultItem
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.TagItem
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
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.io.IOException
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class OrganizationViewModelsTest {
    private val dispatcher = StandardTestDispatcher()

    @Before fun setUp() = Dispatchers.setMain(dispatcher)

    @After fun tearDown() = Dispatchers.resetMain()

    @Test fun `favorites loads pages refreshes and revalidates before opening`() =
        runTest(dispatcher) {
            val repository = FakeRepository()
            var opened = false
            val viewModel = FavoritesViewModel(repository) { file(it) }
            advanceUntilIdle()
            assertEquals(
                listOf(ENTRY),
                viewModel.state.value.items
                    .map { it.id },
            )
            viewModel.loadMore()
            viewModel.loadMore()
            advanceUntilIdle()
            assertEquals(listOf(1, 2), repository.favoritePages)
            assertEquals(
                listOf(ENTRY, ENTRY_2),
                viewModel.state.value.items
                    .map { it.id },
            )
            viewModel.open(
                viewModel.state.value.items
                    .first(),
            ) { opened = true }
            advanceUntilIdle()
            assertTrue(opened)

            viewModel.refresh()
            advanceUntilIdle()
            assertEquals(listOf(1, 2, 1), repository.favoritePages)
            assertEquals(
                listOf(ENTRY),
                viewModel.state.value.items
                    .map { it.id },
            )
        }

    @Test fun `tag validation CRUD and conflict errors remain authoritative`() =
        runTest(dispatcher) {
            val repository = FakeRepository()
            val viewModel = TagsViewModel(repository)
            advanceUntilIdle()
            viewModel.create()
            viewModel.input("\u0000")
            viewModel.confirm()
            assertTrue(viewModel.state.value.validationError != null)
            viewModel.input("New")
            viewModel.confirm()
            advanceUntilIdle()
            assertEquals("New", repository.tags.last().name)
            assertNull(viewModel.state.value.dialog)

            repository.createFailure =
                KuraStorageException.Api(
                    ApiError(ErrorCode.TAG_NAME_CONFLICT, "request-id", 409),
                )
            viewModel.create()
            viewModel.input("Duplicate")
            viewModel.confirm()
            advanceUntilIdle()
            assertEquals(
                "A tag with this name already exists.",
                viewModel.state.value.error
                    ?.message,
            )
            assertEquals(
                "request-id",
                viewModel.state.value.error
                    ?.requestId,
            )
        }

    @Test fun `missing entry permits detach but rejects new favorite and attach`() =
        runTest(dispatcher) {
            val repository = FakeRepository().apply { entryStatus = FileEntryStatus.MISSING }
            val viewModel = EntryOrganizationViewModel(ENTRY, repository, { file(it, FileEntryStatus.MISSING) })
            advanceUntilIdle()
            assertFalse(viewModel.state.value.canAttach)
            viewModel.toggleFavorite()
            viewModel.toggleTag(TagItem(TAG_2, "Other"))
            advanceUntilIdle()
            assertEquals(0, repository.mutations)
            viewModel.toggleTag(repository.tags.first())
            advanceUntilIdle()
            assertEquals(1, repository.mutations)
            assertTrue(
                viewModel.state.value.organization
                    ?.tags
                    ?.isEmpty() == true,
            )
        }

    @Test fun `tags discard an obsolete non cooperative refresh response`() =
        runTest(dispatcher) {
            val first = CompletableDeferred<List<TagItem>>()
            val second = CompletableDeferred<List<TagItem>>()
            val repository = FakeRepository().apply { tagResponses.addAll(listOf(first, second)) }
            val viewModel = TagsViewModel(repository)
            runCurrent()
            viewModel.refresh()
            runCurrent()
            second.complete(listOf(TagItem(TAG_2, "Current")))
            first.complete(listOf(TagItem(TAG, "Obsolete")))
            advanceUntilIdle()
            assertEquals(
                listOf("Current"),
                viewModel.state.value.tags
                    .map { it.name },
            )
        }

    @Test fun `rapid favorite taps issue one mutation and keep authoritative result`() =
        runTest(dispatcher) {
            val gate = CompletableDeferred<Unit>()
            val repository = FakeRepository().apply { favoriteGate = gate }
            val viewModel = EntryOrganizationViewModel(ENTRY, repository, { file(it) })
            advanceUntilIdle()
            viewModel.toggleFavorite()
            viewModel.toggleFavorite()
            runCurrent()
            assertEquals(1, repository.mutations)
            assertTrue(viewModel.state.value.pendingFavorite)
            gate.complete(Unit)
            advanceUntilIdle()
            assertTrue(
                viewModel.state.value.organization
                    ?.isFavorite == true,
            )
            assertFalse(viewModel.state.value.pendingFavorite)
        }

    @Test fun `unknown favorite result keeps authoritative state and exposes refresh guidance`() =
        runTest(dispatcher) {
            val repository =
                FakeRepository().apply {
                    mutationFailure = KuraStorageException.Network(IOException("response unknown"))
                }
            val viewModel = EntryOrganizationViewModel(ENTRY, repository, { file(it) })
            advanceUntilIdle()

            viewModel.toggleFavorite()
            advanceUntilIdle()

            assertFalse(checkNotNull(viewModel.state.value.organization).isFavorite)
            assertFalse(viewModel.state.value.pendingFavorite)
            assertEquals(
                "The result is unknown. Refresh and try again.",
                viewModel.state.value.error
                    ?.message,
            )
        }

    @Test fun `rapid tag taps issue one mutation and retain pending until server success`() =
        runTest(dispatcher) {
            val gate = CompletableDeferred<Unit>()
            val repository = FakeRepository().apply { tagGate = gate }
            val viewModel = EntryOrganizationViewModel(ENTRY, repository, { file(it) })
            advanceUntilIdle()
            val tag = repository.tags.first()

            viewModel.toggleTag(tag)
            viewModel.toggleTag(tag)
            runCurrent()

            assertEquals(1, repository.mutations)
            assertEquals(setOf(TAG), viewModel.state.value.pendingTagIds)
            assertEquals(listOf(TAG), checkNotNull(viewModel.state.value.organization).tags.map { it.id })

            gate.complete(Unit)
            advanceUntilIdle()
            assertTrue(checkNotNull(viewModel.state.value.organization).tags.isEmpty())
            assertTrue(
                viewModel.state.value.pendingTagIds
                    .isEmpty(),
            )
        }

    @Test fun `stale favorite selection fails closed and refreshes the list`() =
        runTest(dispatcher) {
            val repository = FakeRepository()
            var opened = false
            val viewModel =
                FavoritesViewModel(repository) {
                    throw KuraStorageException.Api(ApiError(ErrorCode.FILE_NOT_FOUND, null, 404))
                }
            advanceUntilIdle()
            viewModel.open(
                viewModel.state.value.items
                    .first(),
            ) { opened = true }
            advanceUntilIdle()
            assertFalse(opened)
            assertEquals(listOf(1, 1), repository.favoritePages)
        }

    private class FakeRepository : OrganizationRepository {
        val favoritePages = mutableListOf<Int>()
        var tags = mutableListOf(TagItem(TAG, "Work"))
        var organization = EntryOrganizationState(false, tags.toList())
        var entryStatus = FileEntryStatus.ACTIVE
        var mutations = 0
        var createFailure: Throwable? = null
        var mutationFailure: Throwable? = null
        var favoriteGate: CompletableDeferred<Unit>? = null
        var tagGate: CompletableDeferred<Unit>? = null
        val tagResponses = ArrayDeque<CompletableDeferred<List<TagItem>>>()

        override suspend fun listFavorites(
            page: Int,
            pageSize: Int,
        ): FavoritePage {
            favoritePages += page
            val item = favorite(if (page == 1) ENTRY else ENTRY_2)
            return FavoritePage(listOf(item), page, 1, 2)
        }

        override suspend fun setFavorite(
            entryId: String,
            favorite: Boolean,
        ): EntryOrganizationState {
            mutations++
            favoriteGate?.await()
            mutationFailure?.let { throw it }
            organization = organization.copy(isFavorite = favorite)
            return organization
        }

        override suspend fun listTags(): List<TagItem> {
            val response = tagResponses.removeFirstOrNull() ?: return tags.toList()
            return try {
                response.await()
            } catch (_: kotlinx.coroutines.CancellationException) {
                withContext(NonCancellable) { response.await() }
            }
        }

        override suspend fun createTag(name: String): TagItem {
            createFailure?.let { throw it }
            return TagItem(TAG_2, name).also(tags::add)
        }

        override suspend fun renameTag(
            tagId: String,
            name: String,
        ): TagItem =
            TagItem(tagId, name).also {
                tags.replaceAll { old ->
                    if (old.id ==
                        tagId
                    ) {
                        it
                    } else {
                        old
                    }
                }
            }

        override suspend fun deleteTag(tagId: String) {
            tags.removeAll { it.id == tagId }
        }

        override suspend fun state(entryId: String) = organization

        override suspend fun setTag(
            entryId: String,
            tagId: String,
            attached: Boolean,
        ): EntryOrganizationState {
            mutations++
            tagGate?.await()
            mutationFailure?.let { throw it }
            organization =
                organization.copy(
                    tags =
                        if (attached) {
                            organization.tags + tags.first { it.id == tagId }
                        } else {
                            organization.tags.filterNot {
                                it.id ==
                                    tagId
                            }
                        },
                )
            return organization
        }
    }

    private companion object {
        const val ENTRY = "00000000-0000-4000-8000-000000000001"
        const val ENTRY_2 = "00000000-0000-4000-8000-000000000002"
        const val OWNER = "00000000-0000-4000-8000-000000000003"
        const val TAG = "00000000-0000-4000-8000-000000000004"
        const val TAG_2 = "00000000-0000-4000-8000-000000000005"
        val NOW: Instant = Instant.parse("2026-08-28T00:00:00Z")

        fun favorite(id: String) = FavoriteItem(metadata(id), NOW)

        fun metadata(id: String) =
            SearchResultItem(
                id,
                FileEntryType.FILE,
                "a.pdf",
                "application/pdf",
                SearchFileCategory.DOCUMENT,
                1,
                FileEntryStatus.ACTIVE,
                NOW,
                OwnerSummary(OWNER, "Owner"),
                SharePermission.MANAGER,
                PermissionSource.OWNER,
                null,
            )

        fun file(
            id: String,
            status: FileEntryStatus = FileEntryStatus.ACTIVE,
        ) = FileEntry(
            id,
            null,
            "a.pdf",
            FileEntryType.FILE,
            "application/pdf",
            1,
            status,
            1,
            null,
            NOW,
            NOW,
            owner = OwnerSummary(OWNER, "Owner"),
            permission = SharePermission.MANAGER,
            permissionSource = PermissionSource.OWNER,
        )
    }
}
