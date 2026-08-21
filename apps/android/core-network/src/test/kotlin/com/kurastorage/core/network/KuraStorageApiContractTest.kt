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
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
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

    @Test
    fun `file list detail folder trash and restore use the OpenAPI contract`() =
        runTest {
            server.enqueue(jsonResponse(resource("file-entry-page-response.json")))
            server.enqueue(jsonResponse(resource("file-entry-response.json")))
            server.enqueue(jsonResponse(resource("file-entry-response.json")))
            server.enqueue(jsonResponse(resource("file-entry-response.json")))
            server.enqueue(jsonResponse(resource("file-entry-response.json")))

            assertTrue(api.listFiles("token", null, 1, 100) is NetworkCallResult.Success)
            assertEquals("/api/v1/files?page=1&pageSize=100", server.takeRequest().path)
            api.getFile("token", DEVICE_ID)
            assertEquals("/api/v1/files/$DEVICE_ID", server.takeRequest().path)
            api.createFolder("token", CreateFolderRequestDto(null, "Docs"))
            assertEquals("/api/v1/folders", server.takeRequest().path)
            api.trash("token", DEVICE_ID)
            assertEquals("DELETE", server.takeRequest().method)
            api.restore("token", DEVICE_ID)
            assertEquals("/api/v1/files/$DEVICE_ID/restore", server.takeRequest().path)
        }

    @Test
    fun `rename and move use PATCH with exactly one OpenAPI request field`() =
        runTest {
            server.enqueue(jsonResponse(resource("file-entry-response.json")))
            server.enqueue(jsonResponse(resource("file-entry-response.json")))

            assertTrue(
                api.updateFile("token", DEVICE_ID, UpdateFileRequestDto(name = "renamed.txt")) is
                    NetworkCallResult.Success,
            )
            val rename = server.takeRequest()
            assertEquals("PATCH", rename.method)
            assertEquals("/api/v1/files/$DEVICE_ID", rename.path)
            assertEquals(compactJson(resource("file-rename-request.json")), compactJson(rename.body.readUtf8()))

            api.updateFile("token", DEVICE_ID, UpdateFileRequestDto(parentId = TARGET_PARENT_ID))
            val move = server.takeRequest()
            assertEquals("PATCH", move.method)
            assertEquals("/api/v1/files/$DEVICE_ID", move.path)
            assertEquals(compactJson(resource("file-move-request.json")), compactJson(move.body.readUtf8()))
        }

    @Test
    fun `rename and move errors preserve every new stable error code`() =
        runTest {
            val expected =
                listOf(
                    ErrorCode.VALIDATION_FAILED,
                    ErrorCode.FILE_NOT_FOUND,
                    ErrorCode.FILE_NAME_CONFLICT,
                    ErrorCode.FILE_MOVE_CYCLE,
                    ErrorCode.FILE_OPERATION_NOT_ALLOWED,
                    ErrorCode.RECOVERY_REQUIRED,
                    ErrorCode.STORAGE_UNAVAILABLE,
                    ErrorCode.DEVICE_REVOKED,
                )
            expected.forEach { code ->
                val status =
                    when (code) {
                        ErrorCode.VALIDATION_FAILED -> 400
                        ErrorCode.FILE_NOT_FOUND -> 404
                        ErrorCode.STORAGE_UNAVAILABLE -> 503
                        ErrorCode.DEVICE_REVOKED -> 403
                        else -> 409
                    }
                server.enqueue(
                    MockResponse()
                        .setResponseCode(status)
                        .setHeader("Content-Type", "application/json")
                        .setBody(
                            """{"code":"$code","message":"Request failed.","requestId":"req-update","details":{}}""",
                        ),
                )

                val error =
                    runCatching {
                        api.updateFile("token", DEVICE_ID, UpdateFileRequestDto(name = "renamed.txt"))
                    }.exceptionOrNull() as KuraStorageException.Api

                assertEquals(code, error.error.code)
                assertEquals("req-update", error.error.requestId)
            }
        }

    @Test
    fun `update returns unauthorized for the authenticated executor to refresh and retry`() =
        runTest {
            server.enqueue(MockResponse().setResponseCode(401))

            assertEquals(
                NetworkCallResult.Unauthorized,
                api.updateFile("expired", DEVICE_ID, UpdateFileRequestDto(name = "renamed.txt")),
            )
        }

    @Test
    fun `purge uses stable idempotency header and maps no content and errors`() =
        runTest {
            server.enqueue(MockResponse().setResponseCode(204))
            assertTrue(api.purge("token", DEVICE_ID, IDEMPOTENCY_KEY) is NetworkCallResult.Success)
            val request = server.takeRequest()
            assertEquals("DELETE", request.method)
            assertEquals("/api/v1/trash/$DEVICE_ID", request.path)
            assertEquals(IDEMPOTENCY_KEY, request.getHeader("Idempotency-Key"))

            listOf(
                ErrorCode.FILE_NOT_FOUND to 404,
                ErrorCode.IDEMPOTENCY_CONFLICT to 409,
                ErrorCode.RECOVERY_REQUIRED to 409,
                ErrorCode.STORAGE_UNAVAILABLE to 503,
            ).forEach { (code, status) ->
                server.enqueue(
                    MockResponse().setResponseCode(status).setHeader("Content-Type", "application/json").setBody(
                        """{"code":"$code","message":"failed","requestId":"purge-request","details":{}}""",
                    ),
                )
                val error =
                    runCatching { api.purge("token", DEVICE_ID, IDEMPOTENCY_KEY) }.exceptionOrNull()
                        as KuraStorageException.Api
                assertEquals(code, error.error.code)
            }
        }

    @Test
    fun `admin storage maps nullable capacity and latest run contract`() =
        runTest {
            server.enqueue(jsonResponse(resource("admin-storage-response.json")))
            val result = api.getAdminStorage("token") as NetworkCallResult.Success
            assertEquals(1, result.value.expiredTrashRootCount)
            assertEquals("COMPLETED", result.value.lastPurgeRun?.status)
            assertEquals("/api/v1/admin/storage", server.takeRequest().path)

            server.enqueue(
                jsonResponse(
                    """
                    {"storage":"UNAVAILABLE","totalBytes":null,"availableBytes":null,
                    "capacityWarningThresholdBytes":10737418240,"capacityWarning":null,
                    "trashBytes":0,"expiredTrashRootCount":0,"retentionDays":30,
                    "recoveryRequiredPurgeCount":0,"lastPurgeRun":null}
                    """.trimIndent(),
                ),
            )
            val unavailable = api.getAdminStorage("token") as NetworkCallResult.Success
            assertEquals("UNAVAILABLE", unavailable.value.storage)
            assertNull(unavailable.value.totalBytes)
            assertNull(unavailable.value.capacityWarning)
            assertNull(unavailable.value.lastPurgeRun)
        }

    private fun resource(name: String) = checkNotNull(javaClass.classLoader?.getResource(name)).readText()

    private fun jsonResponse(body: String) = MockResponse().setHeader("Content-Type", "application/json").setBody(body)

    private fun compactJson(body: String) = body.filterNot(Char::isWhitespace)

    private companion object {
        const val DEVICE_ID = "11111111-1111-1111-1111-111111111111"
        const val REFRESH_TOKEN = "refresh-token-with-more-than-thirty-two-characters"
        const val TARGET_PARENT_ID = "33333333-3333-3333-3333-333333333333"
        const val IDEMPOTENCY_KEY = "44444444-4444-4444-4444-444444444444"
        const val TOKEN_RESPONSE =
            """
            {"deviceId":"$DEVICE_ID","accessToken":"access-token","refreshToken":"$REFRESH_TOKEN",
            "accessTokenExpiresAt":"2026-07-26T01:15:00Z","refreshTokenExpiresAt":"2026-07-27T01:00:00Z","role":"ADMIN"}
            """
    }
}
