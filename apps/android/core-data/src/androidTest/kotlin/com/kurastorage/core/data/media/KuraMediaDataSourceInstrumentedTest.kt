package com.kurastorage.core.data.media

import androidx.media3.datasource.DataSpec
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.data.AuthenticationRepository
import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.ReadyMediaSource
import com.kurastorage.core.network.media.OkHttpMediaApi
import okhttp3.OkHttpClient
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.SocketPolicy
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.time.Instant

@RunWith(AndroidJUnit4::class)
class KuraMediaDataSourceInstrumentedTest {
    private lateinit var server: MockWebServer
    private lateinit var repository: MediaRepository
    private val authentication = FakeAuthentication()

    @Before
    fun setUp() {
        server = MockWebServer().apply { start() }
        repository =
            DefaultMediaRepository(
                OkHttpMediaApi(server.url("/api/v1").toString().removeSuffix("/"), OkHttpClient()),
                AuthenticatedRequestExecutor(authentication),
            )
    }

    @After
    fun tearDown() = server.shutdown()

    @Test
    fun initialAndSeekRangesUseAuthorizationAndRefreshOnlyOnce() {
        server.enqueue(
            MockResponse()
                .setResponseCode(206)
                .setHeader("Content-Range", "bytes 0-7/8")
                .setBody("01234567"),
        )
        source().let { dataSource ->
            try {
                assertEquals(8, dataSource.open(DataSpec.Builder().setUri("kurastorage-media://selected").build()))
                assertEquals(4, dataSource.read(ByteArray(4), 0, 4))
            } finally {
                dataSource.close()
            }
        }
        assertEquals("bytes=0-", server.takeRequest().getHeader("Range"))

        server.enqueue(MockResponse().setResponseCode(401))
        server.enqueue(
            MockResponse()
                .setResponseCode(206)
                .setHeader("Content-Range", "bytes 4-7/8")
                .setBody("4567"),
        )
        source().let { dataSource ->
            try {
                val spec =
                    DataSpec
                        .Builder()
                        .setUri("kurastorage-media://selected")
                        .setPosition(4)
                        .setLength(4)
                        .build()
                assertEquals(4, dataSource.open(spec))
                assertEquals(4, dataSource.read(ByteArray(4), 0, 4))
            } finally {
                dataSource.close()
            }
        }
        val rejected = server.takeRequest()
        val refreshed = server.takeRequest()
        assertEquals("bytes=4-7", rejected.getHeader("Range"))
        assertEquals("Bearer initial", rejected.getHeader("Authorization"))
        assertEquals("bytes=4-7", refreshed.getHeader("Range"))
        assertEquals("Bearer refreshed", refreshed.getHeader("Authorization"))
        assertEquals(1, authentication.refreshCalls)
    }

    @Test
    fun rangeFailureRetainsTheHttpStatusForThePlayer() {
        server.enqueue(
            MockResponse()
                .setResponseCode(416)
                .setHeader("Content-Type", "application/json")
                .setBody(RANGE_ERROR_RESPONSE),
        )
        val spec =
            DataSpec
                .Builder()
                .setUri("kurastorage-media://selected")
                .setPosition(8)
                .build()

        val error = assertThrows(MediaDataSourceIOException.Http::class.java) { source().open(spec) }

        assertEquals(416, error.statusCode)
        assertEquals("bytes=8-", server.takeRequest().getHeader("Range"))
    }

    @Test
    fun shortRangeResponseFailsInsteadOfLoopingOrReportingEndOfInput() {
        server.enqueue(
            MockResponse()
                .setResponseCode(206)
                .setHeader("Content-Range", "bytes 0-7/8")
                .setHeader("Content-Length", "8")
                .setBody("01234567")
                .setSocketPolicy(SocketPolicy.DISCONNECT_DURING_RESPONSE_BODY),
        )
        val dataSource = source()
        val spec =
            DataSpec
                .Builder()
                .setUri("kurastorage-media://selected")
                .setLength(8)
                .build()
        try {
            assertEquals(8, dataSource.open(spec))
            assertEquals(4, dataSource.read(ByteArray(8), 0, 8))
            assertThrows(MediaDataSourceIOException.Incomplete::class.java) {
                dataSource.read(ByteArray(8), 0, 8)
            }
        } finally {
            dataSource.close()
        }
    }

    @Test
    fun mismatchedRangeIsReportedAsInvalidRange() {
        server.enqueue(
            MockResponse()
                .setResponseCode(206)
                .setHeader("Content-Range", "bytes 1-7/8")
                .setBody("1234567"),
        )

        assertThrows(MediaDataSourceIOException.InvalidRange::class.java) {
            source().open(DataSpec.Builder().setUri("kurastorage-media://selected").build())
        }
    }

    @Test
    fun generatingVariantIsReportedWithoutPreparingInvalidContent() {
        server.enqueue(
            MockResponse()
                .setResponseCode(202)
                .setHeader("Retry-After", "3")
                .setBody(GENERATING_RESPONSE),
        )

        val error =
            assertThrows(MediaGeneratingIOException::class.java) {
                source(MediaVariant.VIDEO_LOW).open(DataSpec.Builder().setUri("kurastorage-media://selected").build())
            }

        assertEquals("job-1", error.job.jobId)
        assertEquals("bytes=0-", server.takeRequest().getHeader("Range"))
    }

    private fun source(variant: MediaVariant = MediaVariant.ORIGINAL) =
        KuraMediaDataSource(
            repository,
            ReadyMediaSource(FILE_ID, 1, variant),
        )

    private class FakeAuthentication : AuthenticationRepository {
        var refreshCalls = 0
        private var token = "initial"

        override suspend fun storedCredential(): StoredCredential? = null

        override suspend fun register(
            route: ConnectionRoute,
            username: String,
            password: String,
            deviceName: String,
        ) = session()

        override suspend fun login(
            username: String,
            password: String,
        ) = session()

        override suspend fun refresh() = session()

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String): AuthSession {
            refreshCalls++
            token = "refreshed"
            return session()
        }

        override suspend fun logout() = Unit

        override fun accessToken(): String = token

        private fun session() =
            AuthSession(
                deviceId = DeviceId("device"),
                accessToken = token,
                refreshToken = "refresh",
                accessTokenExpiresAt = Instant.MAX,
                refreshTokenExpiresAt = Instant.MAX,
                role = UserRole.MEMBER,
            )
    }

    private companion object {
        const val FILE_ID = "11111111-1111-1111-1111-111111111111"
        const val RANGE_ERROR_RESPONSE =
            """{"code":"RANGE_NOT_SATISFIABLE","message":"failed","requestId":"range-1","details":{}}"""
        const val GENERATING_RESPONSE =
            """{"status":"GENERATING","jobId":"job-1","jobStatusUrl":"/api/v1/media-jobs/job-1","retryAfterSeconds":3}"""
    }
}
