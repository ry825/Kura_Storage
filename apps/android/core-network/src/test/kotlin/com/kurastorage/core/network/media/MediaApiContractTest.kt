package com.kurastorage.core.network.media

import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.network.NetworkCallResult
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withTimeout
import okhttp3.OkHttpClient
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.SocketPolicy
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class MediaApiContractTest {
    private lateinit var server: MockWebServer
    private lateinit var api: OkHttpMediaApi

    @Before
    fun setUp() {
        server = MockWebServer()
        server.start()
        api = OkHttpMediaApi(server.url("/api/v1").toString().removeSuffix("/"), OkHttpClient())
    }

    @After
    fun tearDown() {
        server.shutdown()
    }

    @Test
    fun `head original returns typed metadata without requesting content`() =
        runTest {
            server.enqueue(
                MockResponse()
                    .setHeader("Content-Length", "4096")
                    .setHeader("Content-Type", "audio/mpeg")
                    .setHeader("Accept-Ranges", "bytes"),
            )

            val result = api.headOriginal("secret-token", FILE_ID) as NetworkCallResult.Success

            assertEquals(4096L, result.value.contentLength)
            assertEquals("audio/mpeg", result.value.mimeType)
            assertTrue(result.value.acceptsRanges)
            val request = server.takeRequest()
            assertEquals("HEAD", request.method)
            assertEquals("/api/v1/files/$FILE_ID/content?variant=original&disposition=inline", request.path)
            assertEquals("Bearer secret-token", request.getHeader("Authorization"))
            assertFalse(request.path.orEmpty().contains("secret-token"))
        }

    @Test
    fun `head variant returns ready metadata for the selected derivative`() =
        runTest {
            server.enqueue(
                MockResponse()
                    .setHeader("Content-Length", "1536")
                    .setHeader("Content-Type", "video/mp4")
                    .setHeader("Accept-Ranges", "bytes"),
            )

            val result = api.headContent("token", FILE_ID, MediaVariant.VIDEO_LOW) as NetworkCallResult.Success
            val ready = result.value as MediaMetadataNetworkResult.Ready

            assertEquals(1536L, ready.metadata.contentLength)
            assertEquals("video/mp4", ready.metadata.mimeType)
            assertTrue(ready.metadata.acceptsRanges)
            assertEquals(
                "/api/v1/files/$FILE_ID/content?variant=video-low&disposition=inline",
                server.takeRequest().path,
            )
        }

    @Test
    fun `head variant maps generation headers without requiring a response body`() =
        runTest {
            server.enqueue(
                MockResponse()
                    .setResponseCode(202)
                    .setHeader("X-Kura-Media-Job-Id", JOB_ID)
                    .setHeader("Location", "/api/v1/media-jobs/$JOB_ID")
                    .setHeader("Retry-After", "3"),
            )

            val result = api.headContent("token", FILE_ID, MediaVariant.VIDEO_MEDIUM) as NetworkCallResult.Success
            val generating = result.value as MediaMetadataNetworkResult.Generating

            assertEquals(JOB_ID, generating.accepted.jobId)
            assertEquals(3, generating.accepted.retryAfterSeconds)
        }

    @Test
    fun `head variant rejects incomplete ready and generation metadata`() =
        runTest {
            server.enqueue(MockResponse().setHeader("Content-Length", "12").setHeader("Content-Type", "video/mp4"))
            assertTrue(
                runCatching { api.headContent("token", FILE_ID, MediaVariant.VIDEO_LOW) }.exceptionOrNull()
                    is KuraStorageException.InvalidServerResponse,
            )

            server.enqueue(MockResponse().setResponseCode(202).setHeader("Retry-After", "3"))
            assertTrue(
                runCatching { api.headContent("token", FILE_ID, MediaVariant.VIDEO_LOW) }.exceptionOrNull()
                    is KuraStorageException.InvalidServerResponse,
            )
        }

    @Test
    fun `head variant preserves authentication authorization not found and network failures`() =
        runTest {
            server.enqueue(MockResponse().setResponseCode(401))
            assertEquals(
                NetworkCallResult.Unauthorized,
                api.headContent("expired", FILE_ID, MediaVariant.IMAGE_LOW),
            )

            listOf(403, 404).forEach { status ->
                server.enqueue(MockResponse().setResponseCode(status).setHeader("X-Request-Id", "head-1"))
                val error =
                    runCatching { api.headContent("token", FILE_ID, MediaVariant.IMAGE_LOW) }
                        .exceptionOrNull()
                assertTrue("status=$status error=$error", error is KuraStorageException.Api)
                assertEquals(status, (error as KuraStorageException.Api).error.statusCode)
            }

            server.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AT_START))
            val noRetryApi =
                OkHttpMediaApi(
                    server.url("/api/v1").toString().removeSuffix("/"),
                    OkHttpClient.Builder().retryOnConnectionFailure(false).build(),
                )
            assertTrue(
                runCatching { noRetryApi.headContent("token", FILE_ID, MediaVariant.IMAGE_LOW) }.exceptionOrNull()
                    is KuraStorageException.Network,
            )
        }

    @Test
    fun `job lookup and retry preserve nullable progress and server state`() =
        runTest {
            server.enqueue(jsonResponse(MEDIA_JOB))
            server.enqueue(jsonResponse(MEDIA_JOB).setResponseCode(202).setHeader("Retry-After", "7"))

            val current = api.mediaJob("token", JOB_ID) as NetworkCallResult.Success
            val retried = api.retryMediaJob("token", JOB_ID) as NetworkCallResult.Success

            assertEquals("GENERATING", current.value.status)
            assertEquals(null, current.value.progressPercent)
            assertEquals(7, retried.value.retryAfterSeconds)
            assertEquals("GET", server.takeRequest().method)
            assertEquals("POST", server.takeRequest().method)
        }

    @Test
    fun `content request encodes identifiers and sends only selected variant and range`() {
        val call = api.contentRequest("token", "file/segment", MediaVariant.VIDEO_LOW, "bytes=10-19")
        val request = call.request()

        assertEquals(
            "/api/v1/files/file%2Fsegment/content?variant=video-low&disposition=inline",
            request.url.encodedPath + "?" + request.url.encodedQuery,
        )
        assertEquals("Bearer token", request.header("Authorization"))
        assertEquals("bytes=10-19", request.header("Range"))
        assertEquals(server.hostName, request.url.host)
    }

    @Test
    fun `invalid range is rejected before network access`() {
        org.junit.Assert.assertThrows(IllegalArgumentException::class.java) {
            api.contentRequest("token", FILE_ID, MediaVariant.ORIGINAL, "bytes=0-1,4-5")
        }
    }

    @Test
    fun `open content distinguishes generating range and incomplete range metadata`() =
        runTest {
            server.enqueue(
                jsonResponse(
                    GENERATING_RESPONSE,
                ).setResponseCode(202).setHeader("Retry-After", "3"),
            )
            val generating =
                api.openContent("token", FILE_ID, MediaVariant.VIDEO_LOW)
                    as NetworkCallResult.Success
            assertTrue(generating.value is MediaContentNetworkResult.Generating)

            server.enqueue(
                MockResponse()
                    .setResponseCode(206)
                    .setHeader("Content-Range", "bytes 10-19/100")
                    .setBody("0123456789"),
            )
            val ready =
                api.openContent("token", FILE_ID, MediaVariant.ORIGINAL, "bytes=10-19")
                    as NetworkCallResult.Success
            (ready.value as MediaContentNetworkResult.Ready).response.close()

            server.enqueue(MockResponse().setResponseCode(206).setBody("short"))
            assertTrue(
                runCatching {
                    api.openContent("token", FILE_ID, MediaVariant.ORIGINAL, "bytes=10-19")
                }.exceptionOrNull() is KuraStorageException.InvalidServerResponse,
            )
        }

    @Test
    fun `partial content must match the requested start and bounded end`() =
        runTest {
            server.enqueue(
                MockResponse()
                    .setResponseCode(206)
                    .setHeader("Content-Range", "bytes 11-20/100")
                    .setBody("0123456789"),
            )

            val error =
                runCatching {
                    api.openContent("token", FILE_ID, MediaVariant.ORIGINAL, "bytes=10-19")
                }.exceptionOrNull()

            assertTrue(error is KuraStorageException.InvalidServerResponse)
        }

    @Test
    fun `unauthorized and API failures retain refresh and stable error semantics`() =
        runTest {
            server.enqueue(MockResponse().setResponseCode(401))
            assertEquals(NetworkCallResult.Unauthorized, api.mediaJob("expired", JOB_ID))

            server.enqueue(
                MockResponse()
                    .setResponseCode(416)
                    .setHeader("Content-Type", "application/json")
                    .setBody(RANGE_ERROR_RESPONSE),
            )
            val error = runCatching { api.mediaJob("token", JOB_ID) }.exceptionOrNull() as KuraStorageException.Api
            assertEquals(ErrorCode.UNKNOWN, error.error.code)
            assertEquals(416, error.error.statusCode)
        }

    @Test
    fun `cancelling a suspended request cancels the underlying call`() =
        runTest {
            server.enqueue(MockResponse().setSocketPolicy(SocketPolicy.NO_RESPONSE))

            val cancelled = runCatching { withTimeout(100) { api.mediaJob("token", JOB_ID) } }.exceptionOrNull()

            assertTrue(cancelled is kotlinx.coroutines.TimeoutCancellationException)
        }

    private fun jsonResponse(body: String) = MockResponse().setHeader("Content-Type", "application/json").setBody(body)

    private companion object {
        const val FILE_ID = "11111111-1111-1111-1111-111111111111"
        const val JOB_ID = "22222222-2222-2222-2222-222222222222"
        const val GENERATING_RESPONSE =
            """{"status":"GENERATING","jobId":"$JOB_ID","jobStatusUrl":"/api/v1/media-jobs/$JOB_ID",""" +
                """"retryAfterSeconds":3}"""
        const val RANGE_ERROR_RESPONSE =
            """{"code":"RANGE_NOT_SATISFIABLE","message":"failed","requestId":"range-1","details":{}}"""
        const val MEDIA_JOB =
            """{"jobId":"$JOB_ID","status":"GENERATING","progressPercent":null,"processedDurationMs":null,""" +
                """"totalDurationMs":null,"queuePosition":2,"retryable":false,""" +
                """"retryAfterSeconds":7,"contentUrl":null}"""
    }
}
