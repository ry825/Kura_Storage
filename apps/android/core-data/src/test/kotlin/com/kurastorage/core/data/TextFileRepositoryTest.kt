package com.kurastorage.core.data

import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.FileVersionChangeKind
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.network.FileVersionItemDto
import com.kurastorage.core.network.FileVersionPageDto
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.RestoreTextVersionRequestDto
import com.kurastorage.core.network.SaveTextRequestDto
import com.kurastorage.core.network.TextDocumentDto
import com.kurastorage.core.network.TextFileApi
import com.kurastorage.core.network.TextMutationResultDto
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Test
import java.time.Instant

class TextFileRepositoryTest {
    @Test
    fun `maps document history unknown kind and restore result`() =
        runTest {
            val repository = DefaultTextFileRepository(FakeTextApi(), AuthenticatedRequestExecutor(FakeAuth()))

            assertEquals("hello", repository.current("file").content)
            assertEquals(
                FileVersionChangeKind.UNKNOWN,
                repository
                    .versions("file")
                    .items
                    .single()
                    .changeKind,
            )
            assertEquals(FileVersionChangeKind.RESTORE, repository.restore("file", 1, 2, "op-restore").changeKind)
        }

    @Test
    fun `save refreshes once and retries the identical operation ID`() =
        runTest {
            val api = FakeTextApi().apply { unauthorizedSaveOnce = true }
            val repository = DefaultTextFileRepository(api, AuthenticatedRequestExecutor(FakeAuth()))

            repository.save("file", "updated", 1, "op-save")

            assertEquals(listOf("token", "refreshed-token"), api.saveTokens)
            assertEquals(listOf("op-save", "op-save"), api.saveRequests.map { it.operationId })
        }

    @Test
    fun `unknown mutation kind never becomes a successful save`() =
        runTest {
            val api = FakeTextApi().apply { mutationKind = "FUTURE" }
            val repository = DefaultTextFileRepository(api, AuthenticatedRequestExecutor(FakeAuth()))

            val error = runCatching { repository.save("file", "updated", 1, "op") }.exceptionOrNull()

            assertEquals(KuraStorageException.InvalidServerResponse::class, error?.javaClass?.kotlin)
        }

    private class FakeTextApi : TextFileApi {
        var unauthorizedSaveOnce = false
        val saveTokens = mutableListOf<String>()
        val saveRequests = mutableListOf<SaveTextRequestDto>()
        var mutationKind = "TEXT_EDIT"

        override suspend fun getText(
            accessToken: String,
            fileId: String,
        ) = NetworkCallResult.Success(TextDocumentDto("hello", "UTF-8", 1, 5, "a".repeat(64)))

        override suspend fun saveText(
            accessToken: String,
            fileId: String,
            request: SaveTextRequestDto,
        ): NetworkCallResult<TextMutationResultDto> {
            saveTokens += accessToken
            saveRequests += request
            if (unauthorizedSaveOnce && saveTokens.size == 1) return NetworkCallResult.Unauthorized
            return NetworkCallResult.Success(mutation(mutationKind))
        }

        override suspend fun listVersions(
            accessToken: String,
            fileId: String,
            page: Int,
            pageSize: Int,
        ) = NetworkCallResult.Success(
            FileVersionPageDto(
                listOf(FileVersionItemDto(1, 5, "a".repeat(64), "FUTURE", "External change", TIME)),
                page,
                pageSize,
                1,
            ),
        )

        override suspend fun getVersionText(
            accessToken: String,
            fileId: String,
            version: Long,
        ) = NetworkCallResult.Success(TextDocumentDto("old", "UTF-8", version, 3, "b".repeat(64)))

        override suspend fun restoreVersion(
            accessToken: String,
            fileId: String,
            version: Long,
            request: RestoreTextVersionRequestDto,
        ) = NetworkCallResult.Success(mutation("RESTORE"))

        private fun mutation(kind: String) = TextMutationResultDto(2, 5, "b".repeat(64), kind, TIME)
    }

    private class FakeAuth : AuthenticationRepository {
        private val initial = AuthSession(DeviceId("device"), "token", "refresh", Instant.MAX, Instant.MAX)

        override suspend fun storedCredential() = null

        override suspend fun register(
            route: com.kurastorage.core.model.ConnectionRoute,
            username: String,
            password: String,
            deviceName: String,
        ) = initial

        override suspend fun login(
            username: String,
            password: String,
        ) = initial

        override suspend fun refresh() = initial.copy(accessToken = "refreshed-token")

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String) = session("refreshed-token")

        override suspend fun logout() = Unit

        override fun accessToken() = "token"

        private fun session(token: String) = initial.copy(accessToken = token)
    }

    private companion object {
        const val TIME = "2026-09-01T00:00:00Z"
    }
}
