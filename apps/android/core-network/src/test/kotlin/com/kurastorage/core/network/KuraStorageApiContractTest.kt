package com.kurastorage.core.network

import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import kotlinx.coroutines.test.runTest
import okhttp3.OkHttpClient
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Before
import org.junit.Test

class KuraStorageApiContractTest {
    private lateinit var server: MockWebServer
    private lateinit var api: KuraStorageApi

    @Before
    fun setUp() {
        server = MockWebServer()
        server.start()
        api = KuraStorageApi(server.url("/api/v1").toString().removeSuffix("/"), OkHttpClient())
    }

    @After
    fun tearDown() {
        server.shutdown()
    }

    @Test
    fun `register login refresh and logout use the OpenAPI contract`() =
        runTest {
            repeat(3) {
                server.enqueue(
                    MockResponse().setHeader("Content-Type", "application/json").setBody(TOKEN_RESPONSE),
                )
            }
            server.enqueue(MockResponse().setResponseCode(204))

            api.registerDevice(RegisterDeviceRequestDto("family", "secret", "Pixel"))
            assertEquals("/api/v1/auth/register-device", server.takeRequest().path)
            api.login(LoginRequestDto("family", "secret", DEVICE_ID))
            assertEquals("/api/v1/auth/login", server.takeRequest().path)
            api.refresh(RefreshRequestDto(DEVICE_ID, REFRESH_TOKEN))
            assertEquals("/api/v1/auth/refresh", server.takeRequest().path)
            api.logout("access-token", LogoutRequestDto(DEVICE_ID, REFRESH_TOKEN))
            val logout = server.takeRequest()
            assertEquals("/api/v1/auth/logout", logout.path)
            assertEquals("Bearer access-token", logout.getHeader("Authorization"))
            assertFalse(logout.body.readUtf8().contains("access-token"))
        }

    @Test
    fun `API error preserves stable code and request ID`() =
        runTest {
            server.enqueue(
                MockResponse()
                    .setResponseCode(403)
                    .setHeader("Content-Type", "application/json")
                    .setBody(
                        """{"code":"DEVICE_REVOKED","message":"Request failed.","requestId":"req-42","details":{}}""",
                    ),
            )

            val error =
                runCatching {
                    api.login(LoginRequestDto("family", "secret", DEVICE_ID))
                }.exceptionOrNull() as KuraStorageException.Api

            assertEquals(ErrorCode.DEVICE_REVOKED, error.error.code)
            assertEquals("req-42", error.error.requestId)
        }

    private companion object {
        const val DEVICE_ID = "11111111-1111-1111-1111-111111111111"
        const val REFRESH_TOKEN = "refresh-token-with-more-than-thirty-two-characters"
        const val TOKEN_RESPONSE =
            """
            {"deviceId":"$DEVICE_ID","accessToken":"access-token","refreshToken":"$REFRESH_TOKEN",
            "accessTokenExpiresAt":"2026-07-26T01:15:00Z","refreshTokenExpiresAt":"2026-07-27T01:00:00Z"}
            """
    }
}
