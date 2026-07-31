package com.kurastorage.core.data

import android.content.Intent
import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.DownloadOperation
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.network.CreateFolderRequestDto
import com.kurastorage.core.network.FileApi
import com.kurastorage.core.network.FileEntryDto
import com.kurastorage.core.network.FileEntryPageDto
import com.kurastorage.core.network.NetworkCallResult
import kotlinx.coroutines.flow.toList
import kotlinx.coroutines.test.runTest
import okhttp3.MultipartBody
import okhttp3.RequestBody
import okhttp3.ResponseBody
import okhttp3.ResponseBody.Companion.toResponseBody
import okio.Buffer
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.time.Instant

class TransferRepositoryTest {
    @Test
    fun `upload streams bytes reports progress and keeps idempotency key for retry`() =
        runTest {
            val api = TransferApi()
            val streams = MemoryStreams("hello".toByteArray())
            val repository = DefaultTransferRepository(api, AuthenticatedRequestExecutor(FakeAuth()), streams)
            val operation = repository.newUpload("source", "root", "hello.txt", 5, "text/plain")

            val first = repository.upload(operation).toList()
            repository.upload(operation).toList()

            assertArrayEquals("hello".toByteArray(), api.uploaded)
            assertEquals(listOf(operation.idempotencyKey, operation.idempotencyKey), api.keys)
            assertTrue(first.any { it is TransferEvent.Progress && it.transferredBytes == 5L })
            assertTrue(first.last() is TransferEvent.UploadCompleted)
        }

    @Test
    fun `download streams to destination and reports completion`() =
        runTest {
            val api = TransferApi()
            val streams = MemoryStreams(ByteArray(0))
            val repository = DefaultTransferRepository(api, AuthenticatedRequestExecutor(FakeAuth()), streams)

            val events = repository.download(DownloadOperation(file(), "destination")).toList()

            assertArrayEquals("world".toByteArray(), streams.output.toByteArray())
            assertTrue(events.last() is TransferEvent.DownloadCompleted)
        }

    @Test
    fun `failed download attempts to remove partial destination`() =
        runTest {
            val streams =
                object : ContentStreamProvider {
                    var deleted = false

                    override fun openInput(uri: String): InputStream? = null

                    override fun openOutput(uri: String) =
                        object : OutputStream() {
                            override fun write(value: Int) = throw IOException("disk full")
                        }

                    override fun delete(uri: String): Boolean {
                        deleted = true
                        return true
                    }

                    override fun openIntent(
                        uri: String,
                        mimeType: String?,
                    ): Intent = error("unused")
                }
            val repository = DefaultTransferRepository(TransferApi(), AuthenticatedRequestExecutor(FakeAuth()), streams)

            val result = repository.download(DownloadOperation(file(), "destination")).toList().last()

            assertTrue(result is TransferEvent.Failed && result.partialFileRemoved == true)
            assertTrue(streams.deleted)
        }

    private class MemoryStreams(
        private val input: ByteArray,
    ) : ContentStreamProvider {
        val output = ByteArrayOutputStream()

        override fun openInput(uri: String): InputStream = ByteArrayInputStream(input)

        override fun openOutput(uri: String): OutputStream = output

        override fun delete(uri: String) = true

        override fun openIntent(
            uri: String,
            mimeType: String?,
        ): Intent = error("unused")
    }

    private class TransferApi : FileApi {
        val keys = mutableListOf<String>()
        var uploaded = ByteArray(0)

        override suspend fun upload(
            accessToken: String,
            idempotencyKey: String,
            destinationFolderId: RequestBody,
            fileName: RequestBody,
            size: RequestBody,
            contentType: RequestBody?,
            sha256: RequestBody?,
            file: MultipartBody.Part,
        ): NetworkCallResult<FileEntryDto> {
            keys += idempotencyKey
            val sink = Buffer()
            file.body.writeTo(sink)
            uploaded = sink.readByteArray()
            return NetworkCallResult.Success(dto())
        }

        override suspend fun download(
            accessToken: String,
            fileId: String,
        ): NetworkCallResult<ResponseBody> = NetworkCallResult.Success("world".toResponseBody())

        override suspend fun listFiles(
            accessToken: String,
            parentId: String?,
            page: Int,
            pageSize: Int,
        ): NetworkCallResult<FileEntryPageDto> = error("unused")

        override suspend fun getFile(
            accessToken: String,
            fileId: String,
        ): NetworkCallResult<FileEntryDto> = error("unused")

        override suspend fun createFolder(
            accessToken: String,
            request: CreateFolderRequestDto,
        ): NetworkCallResult<FileEntryDto> = error("unused")

        override suspend fun trash(
            accessToken: String,
            fileId: String,
        ): NetworkCallResult<FileEntryDto> = error("unused")

        override suspend fun listTrash(
            accessToken: String,
            page: Int,
            pageSize: Int,
        ): NetworkCallResult<FileEntryPageDto> = error("unused")

        override suspend fun restore(
            accessToken: String,
            fileId: String,
        ): NetworkCallResult<FileEntryDto> = error("unused")
    }

    private class FakeAuth : AuthenticationRepository {
        private val session = AuthSession(DeviceId("device"), "token", "refresh", Instant.MAX, Instant.MAX)

        override suspend fun storedCredential(): StoredCredential? = null

        override suspend fun register(
            route: ConnectionRoute,
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
        const val TIME = "2026-07-29T00:00:00Z"

        fun dto() = FileEntryDto("file", "root", "hello.txt", "FILE", "text/plain", 5, "ACTIVE", 1, null, TIME, TIME)

        fun file() =
            FileEntry(
                "file",
                "root",
                "world.txt",
                FileEntryType.FILE,
                "text/plain",
                5,
                FileEntryStatus.ACTIVE,
                1,
                null,
                Instant.parse(TIME),
                Instant.parse(TIME),
            )
    }
}
