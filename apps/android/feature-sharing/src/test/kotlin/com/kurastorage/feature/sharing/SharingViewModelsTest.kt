@file:Suppress("MaxLineLength")

package com.kurastorage.feature.sharing

import com.kurastorage.core.data.SharingRepository
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.ShareCandidate
import com.kurastorage.core.model.ShareItem
import com.kurastorage.core.model.SharePage
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.ShareScope
import kotlinx.coroutines.CompletableDeferred
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
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class SharingViewModelsTest {
    private val dispatcher = UnconfinedTestDispatcher()

    @Before fun before() = Dispatchers.setMain(dispatcher)

    @After fun after() = Dispatchers.resetMain()

    @Test
    fun `list loads received roots filters refreshes and pages`() =
        runTest(dispatcher) {
            val repository = FakeSharingRepository()
            val viewModel = SharingListViewModel(repository)
            assertEquals(
                listOf(SHARE),
                viewModel.state.value.items
                    .map { it.id },
            )
            viewModel.loadMore()
            assertEquals(
                listOf(SHARE, SHARE_2),
                viewModel.state.value.items
                    .map { it.id },
            )
            viewModel.selectScope(ShareScope.OWNED)
            viewModel.selectTargetType(FileEntryType.FILE)
            assertEquals(ShareScope.OWNED, repository.requests.last().first)
            assertEquals(FileEntryType.FILE, repository.requests.last().second)
            assertFalse(viewModel.state.value.loading)
        }

    @Test
    fun `file settings exclude contributor and manager requires confirmation`() =
        runTest(dispatcher) {
            val viewModel = SharingSettingsViewModel(FakeSharingRepository(), TARGET, FileEntryType.FILE, "photo.jpg")
            assertFalse(SharePermission.CONTRIBUTOR in viewModel.state.value.availablePermissions)
            viewModel.selectCandidate(USER)
            viewModel.selectPermission(SharePermission.MANAGER)
            viewModel.submitSelectedMember()
            assertEquals(Confirmation.GRANT_MANAGER, viewModel.state.value.confirmation)
            viewModel.confirm()
            assertEquals(
                SharePermission.MANAGER,
                viewModel.state.value.share
                    ?.members
                    ?.single()
                    ?.permission,
            )
            assertNull(viewModel.state.value.confirmation)
        }

    @Test
    fun `submitting blocks duplicate and deletion loss returns safe state`() =
        runTest(dispatcher) {
            val gate = CompletableDeferred<Unit>()
            val repository = FakeSharingRepository().apply { createGate = gate }
            val viewModel = SharingSettingsViewModel(repository, TARGET, FileEntryType.FOLDER, "Photos")
            viewModel.selectCandidate(USER)
            viewModel.submitSelectedMember()
            viewModel.submitSelectedMember()
            assertEquals(1, repository.createCalls)
            assertTrue(viewModel.state.value.submitting)
            gate.complete(Unit)
            assertFalse(viewModel.state.value.submitting)

            repository.detailFailure = KuraStorageException.Api(ApiError(ErrorCode.SHARE_NOT_FOUND, "request", 404))
            val lost = SharingSettingsViewModel(repository, TARGET, FileEntryType.FOLDER, "Photos", SHARE)
            assertTrue(lost.state.value.accessLost)
        }

    @Test
    fun `list exposes loading empty network error and successful refresh`() =
        runTest(dispatcher) {
            val gate = CompletableDeferred<Unit>()
            val repository = FakeSharingRepository().apply { listGate = gate }
            val viewModel = SharingListViewModel(repository)
            assertTrue(viewModel.state.value.loading)

            repository.returnEmptyList = true
            gate.complete(Unit)
            assertFalse(viewModel.state.value.loading)
            assertTrue(
                viewModel.state.value.items
                    .isEmpty(),
            )

            repository.listFailure = KuraStorageException.Network(java.io.IOException("offline"))
            viewModel.refresh()
            assertTrue(
                viewModel.state.value.error
                    ?.contains("could not be confirmed") == true,
            )

            repository.listFailure = null
            repository.returnEmptyList = false
            viewModel.refresh()
            assertEquals(
                listOf(SHARE),
                viewModel.state.value.items
                    .map { it.id },
            )
        }

    @Test
    fun `settings update removal deletion and network failure follow authoritative state`() =
        runTest(dispatcher) {
            val repository = FakeSharingRepository()
            val viewModel = SharingSettingsViewModel(repository, TARGET, FileEntryType.FOLDER, "Photos", SHARE)

            viewModel.changeMemberPermission(USER, SharePermission.EDITOR)
            assertEquals(listOf(SharePermission.EDITOR), repository.setPermissions)
            assertEquals(
                SharePermission.EDITOR,
                viewModel.state.value.share
                    ?.members
                    ?.single()
                    ?.permission,
            )

            viewModel.requestMemberRemoval(USER)
            assertEquals(Confirmation.REMOVE_MEMBER, viewModel.state.value.confirmation)
            viewModel.confirm()
            assertEquals(1, repository.removeCalls)
            assertTrue(
                viewModel.state.value.share
                    ?.members
                    ?.isEmpty() == true,
            )

            viewModel.requestShareDeletion()
            viewModel.confirm()
            assertEquals(1, repository.deleteCalls)
            assertTrue(viewModel.state.value.accessLost)

            val failureRepository =
                FakeSharingRepository().apply {
                    setFailure = KuraStorageException.Network(java.io.IOException("unknown"))
                }
            val failed = SharingSettingsViewModel(failureRepository, TARGET, FileEntryType.FOLDER, "Photos", SHARE)
            failed.changeMemberPermission(USER, SharePermission.EDITOR)
            assertFalse(failed.state.value.submitting)
            assertTrue(
                failed.state.value.error
                    ?.contains("could not be confirmed") == true,
            )

            failureRepository.setFailure =
                KuraStorageException.Api(ApiError(ErrorCode.SHARE_CONFLICT, "conflict", 409))
            failed.changeMemberPermission(USER, SharePermission.VIEWER)
            assertFalse(failed.state.value.submitting)
            assertTrue(
                failed.state.value.error
                    ?.contains("SHARE_CONFLICT") == true,
            )

            val lostRepository = FakeSharingRepository()
            val lost = SharingSettingsViewModel(lostRepository, TARGET, FileEntryType.FOLDER, "Photos", SHARE)
            lostRepository.detailFailure = KuraStorageException.Api(ApiError(ErrorCode.SHARE_NOT_FOUND, "lost", 404))
            lost.requestMemberRemoval(USER)
            lost.confirm()
            assertTrue(lost.state.value.accessLost)
            assertEquals("Access removed.", lost.state.value.message)
        }

    private class FakeSharingRepository : SharingRepository {
        val requests = mutableListOf<Pair<ShareScope, FileEntryType?>>()
        var createCalls = 0
        var createGate: CompletableDeferred<Unit>? = null
        var detailFailure: Throwable? = null
        var listGate: CompletableDeferred<Unit>? = null
        var listFailure: Throwable? = null
        var returnEmptyList = false
        var setFailure: Throwable? = null
        val setPermissions = mutableListOf<SharePermission>()
        var removeCalls = 0
        var deleteCalls = 0
        private var current = item()

        override suspend fun candidates() = listOf(ShareCandidate(USER, "Alex"))

        override suspend fun create(
            targetEntryId: String,
            members: Map<String, SharePermission>,
        ): ShareItem {
            createCalls++
            createGate?.await()
            return item()
                .copy(members = listOf(current.members.single().copy(permission = members.getValue(USER))))
                .also { current = it }
        }

        override suspend fun list(
            scope: ShareScope,
            targetType: FileEntryType?,
            page: Int,
            pageSize: Int,
        ): SharePage {
            listGate?.await()
            listGate = null
            listFailure?.let { throw it }
            requests += scope to targetType
            val values =
                when {
                    returnEmptyList -> emptyList()
                    page == 1 -> listOf(item())
                    else -> listOf(item().copy(id = SHARE_2))
                }
            return SharePage(values, page, 1, 2)
        }

        override suspend fun detail(shareId: String): ShareItem {
            detailFailure?.let { throw it }
            return current
        }

        override suspend fun setMember(
            shareId: String,
            userId: String,
            permission: SharePermission,
        ): ShareItem {
            setPermissions += permission
            setFailure?.let { throw it }
            return current.copy(members = listOf(current.members.single().copy(permission = permission))).also { current = it }
        }

        override suspend fun removeMember(
            shareId: String,
            userId: String,
        ) {
            removeCalls++
            current = current.copy(members = emptyList())
        }

        override suspend fun delete(shareId: String) {
            deleteCalls++
        }
    }

    private companion object {
        const val SHARE = "11111111-1111-1111-1111-111111111111"
        const val SHARE_2 = "11111111-1111-1111-1111-111111111112"
        const val TARGET = "22222222-2222-2222-2222-222222222222"
        const val USER = "33333333-3333-3333-3333-333333333333"

        fun item() =
            ShareItem(
                SHARE,
                TARGET,
                FileEntryType.FOLDER,
                "Photos",
                OwnerSummary("44444444-4444-4444-4444-444444444444", "Owner"),
                SharePermission.MANAGER,
                listOf(
                    com.kurastorage.core.model
                        .ShareMember(USER, "Alex", SharePermission.VIEWER),
                ),
                Instant.EPOCH,
                Instant.EPOCH,
            )
    }
}
