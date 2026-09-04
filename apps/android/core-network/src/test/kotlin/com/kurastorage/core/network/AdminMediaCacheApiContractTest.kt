package com.kurastorage.core.network

import com.kurastorage.core.model.KuraStorageException
import kotlinx.coroutines.test.runTest
import okhttp3.OkHttpClient
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class AdminMediaCacheApiContractTest {
    private lateinit var server: MockWebServer
    private lateinit var api: KuraStorageApi

    @Before
    fun setUp() {
        server = MockWebServer()
        server.start()
        api = KuraStorageApi(server.url("/api/v1").toString().removeSuffix("/"), OkHttpClient())
    }

    @After
    fun tearDown() = server.shutdown()

    @Test
    fun `status and cleanup request use admin contract and idempotency header`() =
        runTest {
            server.enqueue(json(CACHE_STATUS))
            server.enqueue(json(CLEANUP_RUN).setResponseCode(202))

            assertTrue(api.getMediaCache("token") is NetworkCallResult.Success)
            val get = server.takeRequest()
            assertEquals("GET", get.method)
            assertEquals("/api/v1/admin/media-cache", get.path)
            assertEquals("Bearer token", get.getHeader("Authorization"))

            assertTrue(api.requestMediaCacheCleanup("token", KEY) is NetworkCallResult.Success)
            val post = server.takeRequest()
            assertEquals("POST", post.method)
            assertEquals("/api/v1/admin/media-cache/cleanup-requests", post.path)
            assertEquals(KEY, post.getHeader("Idempotency-Key"))
        }

    @Test
    fun `member forbidden is preserved and unauthorized remains refreshable`() =
        runTest {
            server.enqueue(MockResponse().setResponseCode(401))
            assertEquals(NetworkCallResult.Unauthorized, api.getMediaCache("expired"))

            server.enqueue(
                MockResponse().setResponseCode(403).setHeader("Content-Type", "application/json").setBody(
                    """{"code":"AUTHORIZATION_FAILED","message":"failed","requestId":"cache-403","details":{}}""",
                ),
            )
            val failure = runCatching { api.getMediaCache("member") }.exceptionOrNull() as KuraStorageException.Api
            assertEquals(403, failure.error.statusCode)
            assertEquals("cache-403", failure.error.requestId)
        }

    private fun json(body: String) = MockResponse().setHeader("Content-Type", "application/json").setBody(body)

    private companion object {
        const val KEY = "11111111-1111-1111-1111-111111111111"
        const val RUN_ID = "22222222-2222-2222-2222-222222222222"
        val CLEANUP_RUN =
            """
            {
              "id":"$RUN_ID","trigger":"MANUAL","status":"PENDING","requestedAt":"2026-09-04T00:00:00Z",
              "examinedCount":0,"deletedCount":0,"releasedBytes":0,"failureCount":0
            }
            """.trimIndent()
        val CACHE_STATUS =
            """
            {
              "cacheBytes":10,"imageLowBytes":1,"imageMediumBytes":2,"videoLowBytes":3,"videoMediumBytes":4,
              "highWatermarkBytes":100,"lowWatermarkBytes":60,"queuedJobCount":1,"runningJobCount":2,
              "failedJobCount":3,"pendingRunCount":1,"runningRunCount":0,"lastCleanupRun":$CLEANUP_RUN
            }
            """.trimIndent()
    }
}
