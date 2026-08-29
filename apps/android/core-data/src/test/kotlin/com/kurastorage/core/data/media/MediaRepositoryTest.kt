package com.kurastorage.core.data.media

import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.data.AuthenticationRepository
import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.model.media.MediaJobStatus
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.media.MediaApi
import com.kurastorage.core.network.media.MediaContentNetworkResult
import com.kurastorage.core.network.media.MediaJobDto
import com.kurastorage.core.network.media.OriginalMetadataDto
import kotlinx.coroutines.test.runTest
import okhttp3.Call
import okhttp3.Request
import okhttp3.Response
import okhttp3.ResponseBody.Companion.toResponseBody
import okio.Buffer
import okio.Source
import okio.Timeout
import okio.buffer
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.time.Instant

class MediaRepositoryTest {
    @Test
    fun `metadata and jobs refresh once and map unknown states fail closed`() =
        runTest {
            val api = FakeMediaApi().apply { unauthorizedHeadOnce = true }
            val auth = FakeAuthentication()
            val repository = DefaultMediaRepository(api, AuthenticatedRequestExecutor(auth))

            val metadata = repository.inspectOriginal("file")
            assertEquals(1024, metadata.size.value)
            assertEquals(listOf("token", "refreshed-token"), api.headTokens)
            assertEquals(1, auth.refreshAfterUnauthorizedCalls)

            val job = repository.job("job")
            assertEquals(MediaJobStatus.UNKNOWN, job.status)
            assertFalse(job.retryable)
            assertEquals(30, job.retryAfterSeconds)
            assertEquals(null, job.contentUrl)
        }

    @Test
    fun `content refreshes once without changing variant or range`() =
        runTest {
            val api = FakeMediaApi().apply { unauthorizedContentOnce = true }
            val auth = FakeAuthentication()
            val repository = DefaultMediaRepository(api, AuthenticatedRequestExecutor(auth))

            val result = repository.openContent("file", MediaVariant.ORIGINAL, "bytes=10-19")
            assertTrue(result is MediaContentResult.Ready)
            (result as MediaContentResult.Ready).content.close()
            assertEquals(listOf("token", "refreshed-token"), api.contentTokens)
            assertEquals(listOf(MediaVariant.ORIGINAL, MediaVariant.ORIGINAL), api.contentVariants)
            assertEquals(listOf("bytes=10-19", "bytes=10-19"), api.contentRanges)
        }

    @Test
    fun `stream copy rejects short body`() {
        val shortBody =
            object : okhttp3.ResponseBody() {
                override fun contentType() = null

                override fun contentLength() = 10L

                override fun source() = Buffer().writeUtf8("short")
            }
        val response =
            Response
                .Builder()
                .request(Request.Builder().url("https://api.example/content").build())
                .protocol(okhttp3.Protocol.HTTP_1_1)
                .code(200)
                .message("OK")
                .body(shortBody)
                .build()

        val error = runCatching { ReadyMediaContent(response).copyTo(ByteArrayOutputStream(), 100) }.exceptionOrNull()

        assertTrue(error is com.kurastorage.core.model.KuraStorageException.InvalidServerResponse)
    }

    @Test
    fun `stream copy enforces caller byte limit before writing`() {
        val response = response("oversized".toResponseBody())
        val output = ByteArrayOutputStream()

        val error = runCatching { ReadyMediaContent(response).copyTo(output, 4) }.exceptionOrNull()

        assertTrue(error is com.kurastorage.core.model.KuraStorageException.InvalidServerResponse)
        assertEquals(0, output.size())
    }

    @Test
    fun `stream copy propagates interrupted response body`() {
        val interruptedBody =
            object : okhttp3.ResponseBody() {
                override fun contentType() = null

                override fun contentLength() = -1L

                override fun source() = InterruptingSource().buffer()
            }

        val error =
            runCatching {
                ReadyMediaContent(response(interruptedBody)).copyTo(ByteArrayOutputStream(), 100)
            }.exceptionOrNull()

        assertTrue(error is IOException)
    }

    private fun response(body: okhttp3.ResponseBody): Response =
        Response
            .Builder()
            .request(Request.Builder().url("https://api.example/content").build())
            .protocol(okhttp3.Protocol.HTTP_1_1)
            .code(200)
            .message("OK")
            .body(body)
            .build()

    private class InterruptingSource : Source {
        private var firstRead = true

        override fun read(
            sink: Buffer,
            byteCount: Long,
        ): Long {
            if (!firstRead) throw IOException("interrupted")
            firstRead = false
            sink.writeUtf8("part")
            return 4
        }

        override fun timeout(): Timeout = Timeout.NONE

        override fun close() = Unit
    }

    private class FakeMediaApi : MediaApi {
        var unauthorizedHeadOnce = false
        var unauthorizedContentOnce = false
        val headTokens = mutableListOf<String>()
        val contentTokens = mutableListOf<String>()
        val contentVariants = mutableListOf<MediaVariant>()
        val contentRanges = mutableListOf<String?>()

        override suspend fun headOriginal(
            accessToken: String,
            fileId: String,
        ): NetworkCallResult<OriginalMetadataDto> {
            headTokens += accessToken
            if (unauthorizedHeadOnce && headTokens.size == 1) return NetworkCallResult.Unauthorized
            return NetworkCallResult.Success(OriginalMetadataDto(1024, "video/mp4", true))
        }

        override suspend fun mediaJob(
            accessToken: String,
            jobId: String,
        ) = NetworkCallResult.Success(
            MediaJobDto(jobId, "FUTURE", 101, null, null, 0, true, 99, "https://evil.example/content"),
        )

        override suspend fun retryMediaJob(
            accessToken: String,
            jobId: String,
        ) = mediaJob(accessToken, jobId)

        override fun contentRequest(
            accessToken: String,
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): Call = error("not used")

        override suspend fun openContent(
            accessToken: String,
            fileId: String,
            variant: MediaVariant,
            range: String?,
        ): NetworkCallResult<MediaContentNetworkResult> {
            contentTokens += accessToken
            contentVariants += variant
            contentRanges += range
            if (unauthorizedContentOnce && contentTokens.size == 1) return NetworkCallResult.Unauthorized
            val response =
                Response
                    .Builder()
                    .request(Request.Builder().url("https://api.example/content").build())
                    .protocol(okhttp3.Protocol.HTTP_1_1)
                    .code(200)
                    .message("OK")
                    .body("payload".toResponseBody())
                    .build()
            return NetworkCallResult.Success(MediaContentNetworkResult.Ready(response))
        }
    }

    private class FakeAuthentication : AuthenticationRepository {
        var refreshAfterUnauthorizedCalls = 0
        private var token = "token"

        override suspend fun refresh() = session(token)

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String): AuthSession {
            refreshAfterUnauthorizedCalls++
            token = "refreshed-token"
            return session(token)
        }

        override fun accessToken() = token

        override suspend fun storedCredential(): StoredCredential? = null

        override suspend fun register(
            route: ConnectionRoute,
            username: String,
            password: String,
            deviceName: String,
        ) = error("unused")

        override suspend fun login(
            username: String,
            password: String,
        ) = error("unused")

        override suspend fun logout() = Unit

        private fun session(accessToken: String) =
            AuthSession(
                DeviceId("device"),
                accessToken,
                "refresh",
                Instant.MAX,
                Instant.MAX,
                UserRole.MEMBER,
            )
    }
}
