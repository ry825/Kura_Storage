package com.kurastorage.core.data

import android.content.Intent
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.DownloadOperation
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.TransferEvent
import com.kurastorage.core.model.UploadState
import com.kurastorage.core.network.CreateFolderRequestDto
import com.kurastorage.core.network.CreateUploadSessionRequestDto
import com.kurastorage.core.network.FileApi
import com.kurastorage.core.network.FileEntryDto
import com.kurastorage.core.network.FileEntryPageDto
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.UpdateFileRequestDto
import com.kurastorage.core.network.UploadChunkDto
import com.kurastorage.core.network.UploadSessionApi
import com.kurastorage.core.network.UploadSessionDto
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.toList
import kotlinx.coroutines.test.runTest
import okhttp3.MultipartBody
import okhttp3.RequestBody
import okhttp3.ResponseBody
import okhttp3.ResponseBody.Companion.toResponseBody
import okio.Buffer
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
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
            assertTrue(first.any { it is TransferEvent.UploadStatus && it.operation.confirmedOffset == 5L })
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
    fun `unknown chunk response is reconciled from server offset and resumes`() =
        runTest {
            val api = TransferApi().apply { failChunkOnce = true }
            val repository =
                DefaultTransferRepository(
                    api,
                    AuthenticatedRequestExecutor(FakeAuth()),
                    MemoryStreams("hello".toByteArray()),
                )

            val operation = repository.newUpload("source", "root", "hello.txt", 5, "text/plain")
            val events = repository.upload(operation).toList()

            assertArrayEquals("hello".toByteArray(), api.uploaded)
            assertTrue(api.getSessionCalls > 0)
            assertTrue(
                events.any { it is TransferEvent.UploadStatus && it.operation.state == UploadState.PAUSED },
            )
            assertTrue(events.last() is TransferEvent.UploadCompleted)
        }

    @Test
    fun `offset conflict reconciles confirmed offset and resumes without duplicating bytes`() =
        runTest {
            val api =
                TransferApi().apply {
                    acceptChunkBeforeFailure = true
                    chunkFailure =
                        KuraStorageException.Api(
                            ApiError(ErrorCode.UPLOAD_OFFSET_MISMATCH, "offset-request", 409, uploadOffset = 2),
                        )
                }
            val repository =
                DefaultTransferRepository(
                    api,
                    AuthenticatedRequestExecutor(FakeAuth()),
                    MemoryStreams("hello".toByteArray()),
                )

            val events = repository.upload(repository.newUpload("source", "root", "hello.txt", 5, null)).toList()

            assertArrayEquals("hello".toByteArray(), api.uploaded)
            assertEquals(listOf(0L, 2L, 4L), api.chunkAttempts.map { it.offset })
            assertEquals(1, api.getSessionCalls)
            assertTrue(events.any { it is TransferEvent.UploadStatus && it.operation.state == UploadState.PAUSED })
            assertTrue(events.last() is TransferEvent.UploadCompleted)
        }

    @Test
    fun `retry after delays reconciliation and resends the same offset and chunk`() =
        runTest {
            val api =
                TransferApi().apply {
                    chunkFailure =
                        KuraStorageException.Api(
                            ApiError(ErrorCode.UPLOAD_LIMIT_REACHED, "limit-request", 429, retryAfterSeconds = 7),
                        )
                }
            val repository =
                DefaultTransferRepository(
                    api,
                    AuthenticatedRequestExecutor(FakeAuth()),
                    MemoryStreams("hello".toByteArray()),
                )

            repository.upload(repository.newUpload("source", "root", "hello.txt", 5, null)).toList()

            assertTrue(testScheduler.currentTime >= 7_000)
            assertEquals(listOf(0L, 0L, 2L, 4L), api.chunkAttempts.map { it.offset })
            assertArrayEquals(api.chunkAttempts[0].bytes, api.chunkAttempts[1].bytes)
            assertArrayEquals("hello".toByteArray(), api.uploaded)
        }

    @Test
    fun `unauthorized chunk refreshes and retries with the same session offset and content`() =
        runTest {
            val api = TransferApi().apply { unauthorizedChunkOnce = true }
            val auth = FakeAuth(initialToken = "expired", refreshedToken = "fresh")
            val repository =
                DefaultTransferRepository(
                    api,
                    AuthenticatedRequestExecutor(auth),
                    MemoryStreams("hello".toByteArray()),
                )
            val operation = repository.newUpload("source", "root", "hello.txt", 5, null)

            repository.upload(operation).toList()

            assertEquals(1, auth.refreshAfterUnauthorizedCalls)
            assertEquals(listOf("expired", "fresh"), api.chunkAttempts.take(2).map { it.token })
            assertEquals(listOf(0L, 0L), api.chunkAttempts.take(2).map { it.offset })
            assertArrayEquals(api.chunkAttempts[0].bytes, api.chunkAttempts[1].bytes)
            assertEquals(listOf(operation.idempotencyKey), api.keys)
            assertArrayEquals("hello".toByteArray(), api.uploaded)
        }

    @Test
    fun `changed source fails before creating or reusing a session`() =
        runTest {
            val api = TransferApi()
            val repository =
                DefaultTransferRepository(
                    api,
                    AuthenticatedRequestExecutor(FakeAuth()),
                    MemoryStreams("hello".toByteArray()),
                )
            val operation =
                repository
                    .newUpload("source", "root", "hello.txt", 5, "text/plain")
                    .copy(sha256 = "0".repeat(64), sessionId = "session")

            val events = repository.upload(operation).toList()

            assertTrue(
                events.any {
                    it is TransferEvent.Failed && it.error is KuraStorageException.UploadSourceChanged
                },
            )
            assertEquals(0, api.getSessionCalls)
        }

    @Test
    fun `unavailable source fails without creating a session and asks for reselection`() =
        runTest {
            val api = TransferApi()
            val streams =
                object : ContentStreamProvider {
                    override fun openInput(uri: String): InputStream? = null

                    override fun openOutput(uri: String): OutputStream? = null

                    override fun delete(uri: String) = false

                    override fun openIntent(
                        uri: String,
                        mimeType: String?,
                    ): Intent = error("unused")
                }
            val repository = DefaultTransferRepository(api, AuthenticatedRequestExecutor(FakeAuth()), streams)

            val events = repository.upload(repository.newUpload("missing", "root", "missing.bin", 1, null)).toList()

            assertEquals(0, api.createSessionCalls)
            assertTrue(
                events.any {
                    it is TransferEvent.Failed &&
                        it.error is KuraStorageException.UploadSourceUnavailable
                },
            )
            assertTrue(
                events.any {
                    it is TransferEvent.UploadStatus &&
                        it.message == "The selected file can no longer be opened. Select it again." &&
                        !it.canRetry
                },
            )
        }

    @Test
    fun `expired cancelled and completed sessions remain explicit user action errors`() =
        runTest {
            val expected =
                listOf(
                    ErrorCode.UPLOAD_SESSION_EXPIRED to "The upload session expired. Start a new upload.",
                    ErrorCode.UPLOAD_SESSION_CANCELLED to "The upload was cancelled.",
                    ErrorCode.UPLOAD_SESSION_COMPLETED to "Upload failed: UPLOAD_SESSION_COMPLETED",
                )

            expected.forEach { (code, message) ->
                val api =
                    TransferApi().apply {
                        createFailure = KuraStorageException.Api(ApiError(code, "session-request", 409))
                    }
                val repository =
                    DefaultTransferRepository(
                        api,
                        AuthenticatedRequestExecutor(FakeAuth()),
                        MemoryStreams("hello".toByteArray()),
                    )

                val events = repository.upload(repository.newUpload("source", "root", "hello.txt", 5, null)).toList()
                val status = events.last() as TransferEvent.UploadStatus

                assertEquals(UploadState.FAILED, status.operation.state)
                assertEquals(message, status.message)
                assertFalse(status.canRetry)
            }
        }

    @Test
    fun `explicit cancellation calls session API while coroutine cancellation does not`() =
        runTest {
            val api = TransferApi()
            val repository =
                DefaultTransferRepository(
                    api,
                    AuthenticatedRequestExecutor(FakeAuth()),
                    MemoryStreams(ByteArray(0)),
                )
            val operation = repository.newUpload("source", "root", "empty", 0, null).copy(sessionId = "session")

            repository.cancelUpload(operation)

            assertEquals(1, api.cancelCalls)
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

    private class TransferApi :
        FileApi,
        UploadSessionApi {
        data class ChunkAttempt(
            val token: String,
            val offset: Long,
            val bytes: ByteArray,
        )

        val keys = mutableListOf<String>()
        val chunkAttempts = mutableListOf<ChunkAttempt>()
        var uploaded = ByteArray(0)
        var confirmedOffset = 0L
        var failChunkOnce = false
        var unauthorizedChunkOnce = false
        var acceptChunkBeforeFailure = false
        var chunkFailure: KuraStorageException.Api? = null
        var createFailure: KuraStorageException.Api? = null
        var createSessionCalls = 0
        var getSessionCalls = 0
        var cancelCalls = 0

        override suspend fun createUploadSession(
            accessToken: String,
            idempotencyKey: String,
            request: CreateUploadSessionRequestDto,
        ): NetworkCallResult<UploadSessionDto> {
            createSessionCalls++
            keys += idempotencyKey
            createFailure?.let { throw it }
            return NetworkCallResult.Success(session(request.size, confirmedOffset))
        }

        override suspend fun getUploadSession(
            accessToken: String,
            sessionId: String,
        ): NetworkCallResult<UploadSessionDto> {
            getSessionCalls++
            return NetworkCallResult.Success(session(5, confirmedOffset))
        }

        override suspend fun uploadChunk(
            accessToken: String,
            sessionId: String,
            offset: Long,
            sha256: String,
            body: RequestBody,
        ): NetworkCallResult<UploadChunkDto> {
            val sink = Buffer()
            body.writeTo(sink)
            val bytes = sink.readByteArray()
            chunkAttempts += ChunkAttempt(accessToken, offset, bytes)
            if (unauthorizedChunkOnce) {
                unauthorizedChunkOnce = false
                return NetworkCallResult.Unauthorized
            }
            if (failChunkOnce) {
                failChunkOnce = false
                throw KuraStorageException.Network(IOException("response unknown"))
            }
            chunkFailure?.let { error ->
                chunkFailure = null
                if (acceptChunkBeforeFailure) {
                    uploaded += bytes
                    confirmedOffset = offset + bytes.size
                }
                throw error
            }
            uploaded += bytes
            val next = offset + bytes.size
            confirmedOffset = next
            return NetworkCallResult.Success(
                UploadChunkDto(offset, bytes.size.toLong(), sha256, next, next, TIME, false),
            )
        }

        override suspend fun completeUploadSession(
            accessToken: String,
            sessionId: String,
        ): NetworkCallResult<FileEntryDto> = NetworkCallResult.Success(dto())

        override suspend fun cancelUploadSession(
            accessToken: String,
            sessionId: String,
        ): NetworkCallResult<Unit> {
            cancelCalls++
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

        override suspend fun updateFile(
            accessToken: String,
            fileId: String,
            request: UpdateFileRequestDto,
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

    private class FakeAuth(
        initialToken: String = "token",
        private val refreshedToken: String = initialToken,
    ) : AuthenticationRepository {
        private var activeToken = initialToken
        var refreshAfterUnauthorizedCalls = 0

        private fun session(token: String) = AuthSession(DeviceId("device"), token, "refresh", Instant.MAX, Instant.MAX)

        override suspend fun storedCredential(): StoredCredential? = null

        override suspend fun register(
            route: ConnectionRoute,
            username: String,
            password: String,
            deviceName: String,
        ) = session(activeToken)

        override suspend fun login(
            username: String,
            password: String,
        ) = session(activeToken)

        override suspend fun refresh() = session(activeToken)

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String): AuthSession {
            refreshAfterUnauthorizedCalls++
            activeToken = refreshedToken
            return session(activeToken)
        }

        override suspend fun logout() = Unit

        override fun accessToken() = activeToken
    }

    private companion object {
        const val TIME = "2026-07-29T00:00:00Z"

        fun dto() = FileEntryDto("file", "root", "hello.txt", "FILE", "text/plain", 5, "ACTIVE", 1, null, TIME, TIME)

        fun session(
            size: Long,
            offset: Long,
        ) = UploadSessionDto(
            "session",
            "ACTIVE",
            size,
            offset,
            offset,
            2,
            4,
            TIME,
            TIME,
            true,
        )

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
