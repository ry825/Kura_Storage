package com.kurastorage.core.data.media

import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.data.AuthenticationRepository
import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.network.media.OkHttpMediaApi
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.test.runTest
import okhttp3.OkHttpClient
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okio.Buffer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.time.Instant

class KuraMediaFetcherMockWebServerTest {
    private lateinit var server: MockWebServer
    private lateinit var repository: MediaRepository

    @Before
    fun setUp() {
        server = MockWebServer().apply { start() }
        repository =
            DefaultMediaRepository(
                OkHttpMediaApi(server.url("/api/v1").toString().removeSuffix("/"), OkHttpClient()),
                AuthenticatedRequestExecutor(FakeAuthentication()),
            )
    }

    @After
    fun tearDown() {
        server.shutdown()
    }

    @Test
    fun `ready thumbnail uses authenticated selected variant and exposes streaming body`() =
        runTest {
            server.enqueue(
                MockResponse()
                    .setHeader("Content-Type", "image/png")
                    .setBody(Buffer().write(ONE_PIXEL_PNG)),
            )

            val result = fetcher().fetch() as coil3.fetch.SourceFetchResult

            result.source.use { source -> assertTrue(source.source().readByteArray().contentEquals(ONE_PIXEL_PNG)) }
            assertEquals("image/png", result.mimeType)
            val request = server.takeRequest()
            assertEquals("Bearer access-token", request.getHeader("Authorization"))
            assertEquals(
                "/api/v1/files/$FILE_ID/content?variant=thumbnail&disposition=inline",
                request.path,
            )
        }

    @Test
    fun `generating thumbnail never falls back to original content`() =
        runTest {
            server.enqueue(
                MockResponse()
                    .setResponseCode(202)
                    .setHeader("Content-Type", "application/json")
                    .setBody(GENERATING_RESPONSE),
            )

            val error = runCatching { fetcher().fetch() }.exceptionOrNull()

            assertTrue(error is MediaGeneratingException)
            assertEquals(1, server.requestCount)
            assertEquals(
                "/api/v1/files/$FILE_ID/content?variant=thumbnail&disposition=inline",
                server.takeRequest().path,
            )
        }

    @Test
    fun `photo qualities request their exact variants and preserve response content type`() =
        runTest {
            val cases =
                listOf(
                    MediaVariant.IMAGE_LOW to "image-low",
                    MediaVariant.IMAGE_MEDIUM to "image-medium",
                    MediaVariant.ORIGINAL to "original",
                )
            cases.forEach { (variant, wireValue) ->
                server.enqueue(
                    MockResponse()
                        .setHeader("Content-Type", "image/webp; charset=binary")
                        .setBody(Buffer().write(ONE_PIXEL_PNG)),
                )

                val result = fetcher(variant).fetch() as coil3.fetch.SourceFetchResult

                assertEquals("image/webp", result.mimeType)
                result.source.close()
                assertEquals(
                    "/api/v1/files/$FILE_ID/content?variant=$wireValue&disposition=inline",
                    server.takeRequest().path,
                )
            }
        }

    @Test
    fun `media HTTP error is surfaced without a fallback request`() =
        runTest {
            server.enqueue(
                MockResponse()
                    .setResponseCode(403)
                    .setHeader("Content-Type", "application/json")
                    .setBody("""{"code":"FILE_NOT_FOUND","message":"denied","requestId":"media-403","details":{}}"""),
            )

            val failure = runCatching { fetcher(MediaVariant.IMAGE_MEDIUM).fetch() }.exceptionOrNull()

            assertTrue(failure != null)
            assertEquals(1, server.requestCount)
            assertEquals(
                "/api/v1/files/$FILE_ID/content?variant=image-medium&disposition=inline",
                server.takeRequest().path,
            )
        }

    private fun fetcher(variant: MediaVariant = MediaVariant.THUMBNAIL) =
        KuraMediaFetcher(
            KuraMediaImage("session", FILE_ID, 1, variant),
            repository,
            Semaphore(8),
        )

    private class FakeAuthentication : AuthenticationRepository {
        override suspend fun storedCredential(): StoredCredential? = null

        override suspend fun register(
            route: ConnectionRoute,
            username: String,
            password: String,
            deviceName: String,
        ): AuthSession = session()

        override suspend fun login(
            username: String,
            password: String,
        ): AuthSession = session()

        override suspend fun refresh(): AuthSession = session()

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String): AuthSession = session()

        override suspend fun logout() = Unit

        override fun accessToken(): String = "access-token"

        private fun session() =
            AuthSession(
                accessToken = "access-token",
                refreshToken = "refresh-token",
                accessTokenExpiresAt = Instant.MAX,
                refreshTokenExpiresAt = Instant.MAX,
                deviceId = DeviceId("device"),
                role = UserRole.MEMBER,
            )
    }

    private companion object {
        const val FILE_ID = "11111111-1111-1111-1111-111111111111"
        const val JOB_ID = "22222222-2222-2222-2222-222222222222"
        const val GENERATING_RESPONSE =
            """{"status":"GENERATING","jobId":"$JOB_ID",""" +
                """"jobStatusUrl":"/api/v1/media-jobs/$JOB_ID","retryAfterSeconds":2}"""
        val ONE_PIXEL_PNG =
            byteArrayOf(
                -119,
                80,
                78,
                71,
                13,
                10,
                26,
                10,
                0,
                0,
                0,
                13,
                73,
                72,
                68,
                82,
                0,
                0,
                0,
                1,
                0,
                0,
                0,
                1,
                8,
                6,
                0,
                0,
                0,
                31,
                21,
                -60,
                -119,
                0,
                0,
                0,
                13,
                73,
                68,
                65,
                84,
                8,
                -41,
                99,
                -8,
                -49,
                -64,
                -16,
                31,
                0,
                5,
                0,
                1,
                -1,
                -119,
                -103,
                -115,
                29,
                0,
                0,
                0,
                0,
                73,
                69,
                78,
                68,
                -82,
                66,
                96,
                -126,
            )
    }
}
