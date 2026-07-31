package com.kurastorage.core.data

import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.network.CreateFolderRequestDto
import com.kurastorage.core.network.FileApi
import com.kurastorage.core.network.FileEntryDto
import com.kurastorage.core.network.FileEntryPageDto
import com.kurastorage.core.network.NetworkCallResult
import kotlinx.coroutines.test.runTest
import okhttp3.MultipartBody
import okhttp3.RequestBody
import okhttp3.ResponseBody
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
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

    private class FakeFileApi : FileApi {
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

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String) = session

        override suspend fun logout() = Unit

        override fun accessToken() = "token"
    }

    private companion object {
        fun dto(
            id: String,
            type: String = "FILE",
        ) = FileEntryDto(id, "root", "$id.txt", type, "text/plain", 1, "ACTIVE", 1, null, TIME, TIME)

        const val TIME = "2026-07-29T00:00:00Z"
    }
}
