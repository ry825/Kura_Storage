package com.kurastorage.core.data

import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.network.CreateFolderRequestDto
import com.kurastorage.core.network.FileApi
import com.kurastorage.core.network.FileEntryDto
import com.kurastorage.core.network.FileEntryPageDto
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.UpdateFileRequestDto
import kotlinx.coroutines.test.runTest
import okhttp3.MultipartBody
import okhttp3.RequestBody
import okhttp3.ResponseBody
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class FileRepositoryTest {
    @Test
    fun `repository maps DTO and pager appends subsequent pages`() =
        runTest {
            val api = FakeFileApi()
            val repository = DefaultFileRepository(api, AuthenticatedRequestExecutor(FakeAuth()))
            val pager = FilePager { repository.list("root", it, 1) }

            val first = pager.refresh()
            val second = pager.loadNext()

            assertEquals("file-1", first.items.single().id)
            assertEquals(Instant.parse(PURGE_TIME), first.items.single().purgeEligibleAt)
            assertEquals(listOf("file-1", "file-2"), second.items.map { it.id })
            assertFalse(second.hasNextPage)
        }

    @Test
    fun `repository supports detail folder trash list trash and restore`() =
        runTest {
            val repository = DefaultFileRepository(FakeFileApi(), AuthenticatedRequestExecutor(FakeAuth()))

            assertEquals("detail", repository.detail("detail").id)
            assertEquals("FOLDER", repository.createFolder(null, "Docs").entryType.name)
            assertEquals("trash-me", repository.trash("trash-me").id)
            assertEquals(0, repository.listTrash().items.size)
            assertEquals("restore-me", repository.restore("restore-me").id)
        }

    @Test
    fun `repository maps missing timestamps unknown status and missing operations`() =
        runTest {
            val api = FakeFileApi()
            val repository = DefaultFileRepository(api, AuthenticatedRequestExecutor(FakeAuth()))

            val unknown = api.dtoOverride("future", "FUTURE_STATUS").toModel()
            assertEquals(FileEntryStatus.UNKNOWN, unknown.status)
            assertEquals(Instant.parse(TIME), unknown.missingDetectedAt)
            assertEquals("missing", repository.recheckMissing("missing").id)
            repository.deleteMissingIndexEntry("missing")
            assertEquals(listOf("missing"), api.rechecked)
            assertEquals(listOf("missing"), api.deletedMissing)
        }

    @Test
    fun `repository sends only name for rename and only parent ID for move`() =
        runTest {
            val api = FakeFileApi()
            val repository = DefaultFileRepository(api, AuthenticatedRequestExecutor(FakeAuth()))

            assertEquals("renamed.txt", repository.rename("file", "renamed.txt").name)
            assertEquals("target", repository.move("file", "target").parentId)
            assertEquals(
                listOf(
                    UpdateFileRequestDto(name = "renamed.txt"),
                    UpdateFileRequestDto(parentId = "target"),
                ),
                api.updateRequests,
            )
        }

    @Test
    fun `update refreshes once after 401 and preserves device revoked and unknown results`() =
        runTest {
            val api = FakeFileApi().apply { unauthorizedUpdateOnce = true }
            val auth = FakeAuth()
            val repository = DefaultFileRepository(api, AuthenticatedRequestExecutor(auth))

            repository.rename("file", "renamed.txt")

            assertEquals(listOf("token", "refreshed-token"), api.updateTokens)
            assertEquals(1, auth.refreshAfterUnauthorizedCalls)

            api.updateFailure =
                KuraStorageException.Api(ApiError(ErrorCode.DEVICE_REVOKED, "revoked-request", 403))
            val revoked = runCatching { repository.move("file", "target") }.exceptionOrNull()
            assertEquals(ErrorCode.DEVICE_REVOKED, (revoked as KuraStorageException.Api).error.code)
            assertEquals("revoked-request", revoked.error.requestId)

            api.updateFailure = KuraStorageException.Network(java.io.IOException("response unknown"))
            val unknown = runCatching { repository.rename("file", "unknown.txt") }.exceptionOrNull()
            assertTrue(unknown is KuraStorageException.Network)
        }

    @Test
    fun `purge preserves idempotency key across refresh retry and never synthesizes unknown success`() =
        runTest {
            val api = FakeFileApi().apply { unauthorizedPurgeOnce = true }
            val auth = FakeAuth()
            val repository = DefaultFileRepository(api, AuthenticatedRequestExecutor(auth))

            repository.purge("file", "key-1")

            assertEquals(listOf("key-1", "key-1"), api.purgeKeys)
            assertEquals(listOf("token", "refreshed-token"), api.purgeTokens)
            api.purgeFailure = KuraStorageException.Network(java.io.IOException("response unknown"))
            val failure = runCatching { repository.purge("file", "key-1") }.exceptionOrNull()
            assertTrue(failure is KuraStorageException.Network)
        }

    private class FakeFileApi : FileApi {
        val updateRequests = mutableListOf<UpdateFileRequestDto>()
        val updateTokens = mutableListOf<String>()
        var unauthorizedUpdateOnce = false
        var updateFailure: Throwable? = null
        val purgeKeys = mutableListOf<String>()
        val purgeTokens = mutableListOf<String>()
        var unauthorizedPurgeOnce = false
        var purgeFailure: Throwable? = null
        val rechecked = mutableListOf<String>()
        val deletedMissing = mutableListOf<String>()

        fun dtoOverride(
            id: String,
            status: String,
        ) = dto(id).copy(status = status, missingDetectedAt = TIME, missingLastCheckedAt = TIME)

        override suspend fun listFiles(
            accessToken: String,
            parentId: String?,
            page: Int,
            pageSize: Int,
        ) = NetworkCallResult.Success(FileEntryPageDto(parentId, listOf(dto("file-$page")), page, 1, 2))

        override suspend fun getFile(
            accessToken: String,
            fileId: String,
        ) = NetworkCallResult.Success(dto(fileId))

        override suspend fun createFolder(
            accessToken: String,
            request: CreateFolderRequestDto,
        ) = NetworkCallResult.Success(dto("folder", "FOLDER"))

        override suspend fun updateFile(
            accessToken: String,
            fileId: String,
            request: UpdateFileRequestDto,
        ): NetworkCallResult<FileEntryDto> {
            updateRequests += request
            updateTokens += accessToken
            updateFailure?.let { throw it }
            if (unauthorizedUpdateOnce && updateTokens.size == 1) return NetworkCallResult.Unauthorized
            val requestedName = request.name
            return NetworkCallResult.Success(
                when {
                    requestedName != null -> dto(fileId).copy(name = requestedName)
                    else -> dto(fileId).copy(parentId = request.parentId)
                },
            )
        }

        override suspend fun trash(
            accessToken: String,
            fileId: String,
        ) = NetworkCallResult.Success(dto(fileId))

        override suspend fun listTrash(
            accessToken: String,
            page: Int,
            pageSize: Int,
        ) = NetworkCallResult.Success(FileEntryPageDto(null, emptyList(), 1, 100, 0))

        override suspend fun restore(
            accessToken: String,
            fileId: String,
        ) = NetworkCallResult.Success(dto(fileId))

        override suspend fun recheckMissing(
            accessToken: String,
            fileId: String,
        ): NetworkCallResult<FileEntryDto> {
            rechecked += fileId
            return NetworkCallResult.Success(dto(fileId).copy(status = "MISSING"))
        }

        override suspend fun deleteMissingIndexEntry(
            accessToken: String,
            fileId: String,
        ): NetworkCallResult<Unit> {
            deletedMissing += fileId
            return NetworkCallResult.Success(Unit)
        }

        override suspend fun purge(
            accessToken: String,
            fileId: String,
            idempotencyKey: String,
        ): NetworkCallResult<Unit> {
            purgeTokens += accessToken
            purgeKeys += idempotencyKey
            purgeFailure?.let { throw it }
            if (unauthorizedPurgeOnce && purgeTokens.size == 1) return NetworkCallResult.Unauthorized
            return NetworkCallResult.Success(Unit)
        }

        override suspend fun upload(
            accessToken: String,
            idempotencyKey: String,
            destinationFolderId: RequestBody,
            fileName: RequestBody,
            size: RequestBody,
            contentType: RequestBody?,
            sha256: RequestBody?,
            file: MultipartBody.Part,
        ) = NetworkCallResult.Success(dto("upload"))

        override suspend fun download(
            accessToken: String,
            fileId: String,
        ): NetworkCallResult<ResponseBody> = error("unused")
    }

    private class FakeAuth : AuthenticationRepository {
        private val session = AuthSession(DeviceId("device"), "token", "refresh", Instant.MAX, Instant.MAX)
        var refreshAfterUnauthorizedCalls = 0

        override suspend fun storedCredential(): StoredCredential? = null

        override suspend fun register(
            route: com.kurastorage.core.model.ConnectionRoute,
            username: String,
            password: String,
            deviceName: String,
        ) = session

        override suspend fun login(
            username: String,
            password: String,
        ) = session

        override suspend fun refresh() = session

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String): AuthSession {
            refreshAfterUnauthorizedCalls += 1
            return session.copy(accessToken = "refreshed-token")
        }

        override suspend fun logout() = Unit

        override fun accessToken() = "token"
    }

    private companion object {
        fun dto(
            id: String,
            type: String = "FILE",
        ) = FileEntryDto(id, "root", "$id.txt", type, "text/plain", 1, "ACTIVE", 1, null, TIME, TIME, PURGE_TIME)

        const val TIME = "2026-07-29T00:00:00Z"
        const val PURGE_TIME = "2026-08-28T00:00:00Z"
    }
}
