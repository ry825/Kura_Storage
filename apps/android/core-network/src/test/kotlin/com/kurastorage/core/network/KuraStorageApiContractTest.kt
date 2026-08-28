package com.kurastorage.core.network

import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import kotlinx.coroutines.test.runTest
import okhttp3.OkHttpClient
import okhttp3.RequestBody.Companion.toRequestBody
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

    @Test
    fun `upload session endpoints preserve idempotency offset checksum and binary body`() =
        runTest {
            server.enqueue(jsonResponse(UPLOAD_SESSION_RESPONSE).setResponseCode(201))
            server.enqueue(jsonResponse(UPLOAD_SESSION_RESPONSE))
            server.enqueue(
                jsonResponse(UPLOAD_CHUNK_RESPONSE),
            )
            server.enqueue(jsonResponse(resource("file-entry-response.json")))
            server.enqueue(MockResponse().setResponseCode(204))

            val created =
                api.createUploadSession(
                    "token",
                    IDEMPOTENCY_KEY,
                    CreateUploadSessionRequestDto(DEVICE_ID, "video.mp4", "video/mp4", 5, SHA256),
                ) as NetworkCallResult.Success
            val create = server.takeRequest()
            assertEquals("POST", create.method)
            assertEquals("/api/v1/upload-sessions", create.path)
            assertEquals(IDEMPOTENCY_KEY, create.getHeader("Idempotency-Key"))
            assertEquals(
                compactJson(
                    """
                    {"destinationFolderId":"$DEVICE_ID","fileName":"video.mp4","contentType":"video/mp4",
                    "size":5,"sha256":"$SHA256"}
                    """.trimIndent(),
                ),
                compactJson(create.body.readUtf8()),
            )
            assertEquals(4_194_304, created.value.preferredChunkBytes)
            assertEquals(8_388_608, created.value.maximumChunkBytes)
            assertEquals("2026-08-29T00:00:00Z", created.value.absoluteExpiresAt)
            assertTrue(created.value.resumable)

            api.getUploadSession("token", DEVICE_ID)
            assertEquals("/api/v1/upload-sessions/$DEVICE_ID", server.takeRequest().path)

            api.uploadChunk("token", DEVICE_ID, 0, SHA256, "hello".toRequestBody())
            val chunk = server.takeRequest()
            assertEquals("PUT", chunk.method)
            assertEquals("0", chunk.getHeader("Upload-Offset"))
            assertEquals(SHA256, chunk.getHeader("X-Chunk-Sha256"))
            assertEquals("hello", chunk.body.readUtf8())

            api.completeUploadSession("token", DEVICE_ID)
            assertEquals("POST", server.takeRequest().method)
            api.cancelUploadSession("token", DEVICE_ID)
            assertEquals("DELETE", server.takeRequest().method)
        }

    @Test
    fun `upload retry headers are preserved on structured error`() =
        runTest {
            server.enqueue(
                MockResponse()
                    .setResponseCode(429)
                    .setHeader("Content-Type", "application/json")
                    .setHeader("Retry-After", "7")
                    .setHeader("Upload-Offset", "4194304")
                    .setBody(UPLOAD_LIMIT_ERROR),
            )

            val error =
                runCatching { api.getUploadSession("token", DEVICE_ID) }.exceptionOrNull()
                    as KuraStorageException.Api

            assertEquals(ErrorCode.UPLOAD_LIMIT_REACHED, error.error.code)
            assertEquals(7L, error.error.retryAfterSeconds)
            assertEquals(4194304L, error.error.uploadOffset)
            assertTrue(error.error.canRetry)
        }

    @Test
    fun `upload session errors preserve every stable upload error code`() =
        runTest {
            val expected =
                listOf(
                    ErrorCode.IDEMPOTENCY_CONFLICT,
                    ErrorCode.UPLOAD_SIZE_MISMATCH,
                    ErrorCode.UPLOAD_CHECKSUM_MISMATCH,
                    ErrorCode.UPLOAD_SESSION_NOT_FOUND,
                    ErrorCode.UPLOAD_OFFSET_MISMATCH,
                    ErrorCode.UPLOAD_INCOMPLETE,
                    ErrorCode.UPLOAD_SESSION_EXPIRED,
                    ErrorCode.UPLOAD_SESSION_CANCELLED,
                    ErrorCode.UPLOAD_SESSION_COMPLETED,
                    ErrorCode.CHUNK_SIZE_LIMIT_EXCEEDED,
                    ErrorCode.FILE_SIZE_LIMIT_EXCEEDED,
                    ErrorCode.CHUNK_CHECKSUM_MISMATCH,
                    ErrorCode.UPLOAD_LIMIT_REACHED,
                    ErrorCode.STORAGE_CAPACITY_INSUFFICIENT,
                    ErrorCode.STORAGE_UNAVAILABLE,
                    ErrorCode.RECOVERY_REQUIRED,
                    ErrorCode.DEVICE_REVOKED,
                )
            expected.forEach { code ->
                val status =
                    when (code) {
                        ErrorCode.UPLOAD_SESSION_NOT_FOUND -> 404
                        ErrorCode.CHUNK_SIZE_LIMIT_EXCEEDED,
                        ErrorCode.FILE_SIZE_LIMIT_EXCEEDED,
                        ErrorCode.CHUNK_CHECKSUM_MISMATCH,
                        ErrorCode.UPLOAD_SIZE_MISMATCH,
                        ErrorCode.UPLOAD_CHECKSUM_MISMATCH,
                        -> 400
                        ErrorCode.UPLOAD_LIMIT_REACHED -> 429
                        ErrorCode.STORAGE_CAPACITY_INSUFFICIENT -> 507
                        ErrorCode.STORAGE_UNAVAILABLE -> 503
                        ErrorCode.DEVICE_REVOKED -> 403
                        else -> 409
                    }
                server.enqueue(
                    MockResponse()
                        .setResponseCode(status)
                        .setHeader("Content-Type", "application/json")
                        .setBody(
                            """{"code":"$code","message":"failed","requestId":"upload-error","details":{}}""",
                        ),
                )

                val error =
                    runCatching { api.getUploadSession("token", DEVICE_ID) }.exceptionOrNull()
                        as KuraStorageException.Api

                assertEquals(code, error.error.code)
                assertEquals(status, error.error.statusCode)
                assertEquals("upload-error", error.error.requestId)
            }
        }

    @Test
    fun `search sends every OpenAPI query with encoding and maps the page`() =
        runTest {
            server.enqueue(jsonResponse(SEARCH_PAGE_RESPONSE))

            val result =
                api.search(
                    "token",
                    SearchRequestDto(
                        query = "report & 100%",
                        entryType = "FILE",
                        fileCategory = "DOCUMENT",
                        status = "MISSING_CANDIDATE",
                        updatedFrom = "2026-08-01T00:00:00Z",
                        updatedTo = "2026-08-25T00:00:00Z",
                        minSize = 1,
                        maxSize = 999,
                        ownerUserId = DEVICE_ID,
                        shareTargetId = TARGET_PARENT_ID,
                        page = 2,
                        pageSize = 50,
                    ),
                ) as NetworkCallResult.Success

            val request = server.takeRequest()
            assertEquals("GET", request.method)
            assertEquals("Bearer token", request.getHeader("Authorization"))
            val url = checkNotNull(request.requestUrl)
            assertEquals("report & 100%", url.queryParameter("q"))
            assertEquals("FILE", url.queryParameter("entryType"))
            assertEquals("DOCUMENT", url.queryParameter("fileCategory"))
            assertEquals("MISSING_CANDIDATE", url.queryParameter("status"))
            assertEquals("2", url.queryParameter("page"))
            assertEquals(1, result.value.items.size)
            assertEquals(
                "Owner",
                result.value.items
                    .single()
                    .owner.displayName,
            )
        }

    @Test
    fun `recent GET and bodyless idempotent PUT match OpenAPI`() =
        runTest {
            server.enqueue(jsonResponse(RECENT_PAGE_RESPONSE))
            server.enqueue(MockResponse().setResponseCode(204))

            val recent = api.listRecentFiles("token", page = 1, pageSize = 50) as NetworkCallResult.Success
            assertEquals(
                "2026-08-25T00:00:00Z",
                recent.value.items
                    .single()
                    .openedAt,
            )
            assertEquals("/api/v1/recent-files?page=1&pageSize=50", server.takeRequest().path)

            assertTrue(api.recordRecentFile("token", DEVICE_ID) is NetworkCallResult.Success)
            val put = server.takeRequest()
            assertEquals("PUT", put.method)
            assertEquals("/api/v1/recent-files/$DEVICE_ID", put.path)
            assertEquals(0, put.bodySize)
        }

    @Test
    fun `search and recent return unauthorized for token refresh retry`() =
        runTest {
            server.enqueue(MockResponse().setResponseCode(401))
            server.enqueue(MockResponse().setResponseCode(401))
            server.enqueue(MockResponse().setResponseCode(401))

            assertEquals(
                NetworkCallResult.Unauthorized,
                api.search("expired", SearchRequestDto(query = "report")),
            )
            assertEquals(NetworkCallResult.Unauthorized, api.listRecentFiles("expired", 1, 50))
            assertEquals(NetworkCallResult.Unauthorized, api.recordRecentFile("expired", DEVICE_ID))
        }

    @Test
    fun `search and recent preserve their stable validation and not found errors`() =
        runTest {
            listOf(
                Triple(ErrorCode.INVALID_SEARCH_QUERY, 400, "search"),
                Triple(ErrorCode.INVALID_SEARCH_FILTER, 400, "search"),
                Triple(ErrorCode.INVALID_RECENT_FILES_REQUEST, 400, "recent"),
                Triple(ErrorCode.FILE_NOT_FOUND, 404, "record"),
            ).forEach { (code, status, operation) ->
                server.enqueue(
                    MockResponse().setResponseCode(status).setHeader("Content-Type", "application/json").setBody(
                        """{"code":"$code","message":"failed","requestId":"contract-error","details":{}}""",
                    ),
                )
                val failure =
                    runCatching {
                        when (operation) {
                            "search" -> api.search("token", SearchRequestDto(query = "x"))
                            "recent" -> api.listRecentFiles("token", 0, 50)
                            else -> api.recordRecentFile("token", DEVICE_ID)
                        }
                    }.exceptionOrNull() as KuraStorageException.Api
                assertEquals(code, failure.error.code)
                assertEquals("contract-error", failure.error.requestId)
            }
        }

    private fun resource(name: String) = checkNotNull(javaClass.classLoader?.getResource(name)).readText()

    private fun jsonResponse(body: String) = MockResponse().setHeader("Content-Type", "application/json").setBody(body)

    private fun compactJson(body: String) = body.filterNot(Char::isWhitespace)

    private companion object {
        const val DEVICE_ID = "11111111-1111-1111-1111-111111111111"
        const val REFRESH_TOKEN = "refresh-token-with-more-than-thirty-two-characters"
        const val TARGET_PARENT_ID = "33333333-3333-3333-3333-333333333333"
        const val IDEMPOTENCY_KEY = "44444444-4444-4444-4444-444444444444"
        const val TIME = "2026-08-23T00:00:00Z"
        const val SHA256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        const val UPLOAD_SESSION_RESPONSE =
            """
            {"id":"$DEVICE_ID","status":"ACTIVE","size":5,"receivedBytes":0,"nextOffset":0,
            "preferredChunkBytes":4194304,"maximumChunkBytes":8388608,"expiresAt":"$TIME",
            "absoluteExpiresAt":"2026-08-29T00:00:00Z","resumable":true,"file":null}
            """
        const val UPLOAD_CHUNK_RESPONSE =
            """
            {"offset":0,"length":5,"sha256":"$SHA256","receivedBytes":5,"nextOffset":5,
            "expiresAt":"$TIME","replayed":false}
            """
        const val UPLOAD_LIMIT_ERROR =
            """
            {"code":"UPLOAD_LIMIT_REACHED","message":"failed","requestId":"upload-request","details":{}}
            """
        const val SEARCH_ITEM =
            """
            {"id":"$DEVICE_ID","entryType":"FILE","name":"report.pdf","mimeType":"application/pdf",
            "fileCategory":"DOCUMENT","size":20,"status":"MISSING_CANDIDATE","updatedAt":"$TIME",
            "owner":{"id":"$TARGET_PARENT_ID","displayName":"Owner"},"permission":"VIEWER",
            "permissionSource":"DIRECT","shareTargetId":"$TARGET_PARENT_ID"}
            """
        const val SEARCH_PAGE_RESPONSE =
            """
            {"items":[$SEARCH_ITEM],"page":2,"pageSize":50,"totalCount":51}
            """
        const val RECENT_PAGE_RESPONSE =
            """
            {"items":[{"id":"$DEVICE_ID","entryType":"FILE","name":"report.pdf",
            "mimeType":"application/pdf","fileCategory":"DOCUMENT","size":20,"status":"ACTIVE",
            "updatedAt":"$TIME","owner":{"id":"$TARGET_PARENT_ID","displayName":"Owner"},
            "permission":"VIEWER","permissionSource":"DIRECT","shareTargetId":"$TARGET_PARENT_ID",
            "openedAt":"2026-08-25T00:00:00Z"}],"page":1,"pageSize":50,"totalCount":1}
            """
        const val TOKEN_RESPONSE =
            """
            {"deviceId":"$DEVICE_ID","accessToken":"access-token","refreshToken":"$REFRESH_TOKEN",
            "accessTokenExpiresAt":"2026-07-26T01:15:00Z","refreshTokenExpiresAt":"2026-07-27T01:00:00Z","role":"ADMIN"}
            """
    }
}
