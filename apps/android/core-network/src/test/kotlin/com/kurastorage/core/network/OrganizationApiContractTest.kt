@file:Suppress("MaxLineLength")

package com.kurastorage.core.network

import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.test.runTest
import okhttp3.OkHttpClient
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.SocketPolicy
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class OrganizationApiContractTest {
    private lateinit var server: MockWebServer
    private lateinit var api: KuraStorageApi

    @Before fun setUp() {
        server = MockWebServer().apply { start() }
        api = KuraStorageApi(server.url("/api/v1").toString().removeSuffix("/"), OkHttpClient())
    }

    @After fun tearDown() = server.shutdown()

    @Test fun `organization endpoints preserve methods paths bodies and no-content requests`() =
        runTest {
            server.enqueue(json(FAVORITE_PAGE))
            server.enqueue(MockResponse().setResponseCode(204))
            server.enqueue(MockResponse().setResponseCode(204))
            server.enqueue(json("[$TAG]"))
            server.enqueue(json(TAG).setResponseCode(201))
            server.enqueue(json(TAG))
            server.enqueue(MockResponse().setResponseCode(204))
            server.enqueue(json("{\"isFavorite\":true,\"tags\":[$TAG]}"))
            server.enqueue(MockResponse().setResponseCode(204))
            server.enqueue(MockResponse().setResponseCode(204))

            assertTrue(api.listFavorites("token", 1, 50) is NetworkCallResult.Success)
            assertEquals("/api/v1/favorites?page=1&pageSize=50", server.takeRequest().path)
            api.addFavorite("token", ENTRY)
            assertRequest("PUT", "/api/v1/favorites/$ENTRY", "")
            api.removeFavorite("token", ENTRY)
            assertRequest("DELETE", "/api/v1/favorites/$ENTRY", "")
            api.listTags("token")
            assertRequest("GET", "/api/v1/tags")
            api.createTag("token", TagNameRequestDto("Work"))
            assertRequest("POST", "/api/v1/tags", "{\"name\":\"Work\"}")
            api.renameTag("token", TAG_ID, TagNameRequestDto("Work"))
            assertRequest("PATCH", "/api/v1/tags/$TAG_ID", "{\"name\":\"Work\"}")
            api.deleteTag("token", TAG_ID)
            assertRequest("DELETE", "/api/v1/tags/$TAG_ID", "")
            api.getEntryOrganization("token", ENTRY)
            assertRequest("GET", "/api/v1/files/$ENTRY/organization")
            api.attachTag("token", ENTRY, TAG_ID)
            assertRequest("PUT", "/api/v1/files/$ENTRY/tags/$TAG_ID", "")
            api.detachTag("token", ENTRY, TAG_ID)
            assertRequest("DELETE", "/api/v1/files/$ENTRY/tags/$TAG_ID", "")
        }

    @Test fun `search encodes repeated tag IDs without tag names`() =
        runTest {
            server.enqueue(json("{\"items\":[],\"page\":1,\"pageSize\":50,\"totalCount\":0}"))
            api.search("token", SearchRequestDto(tagIds = listOf(TAG_ID, TAG_ID_2)))
            assertEquals("/api/v1/search?tagId=$TAG_ID&tagId=$TAG_ID_2&page=1&pageSize=50", server.takeRequest().path)
        }

    @Test fun `organization endpoints preserve auth status errors retry metadata and cancellation`() =
        runTest {
            server.enqueue(MockResponse().setResponseCode(401))
            assertEquals(NetworkCallResult.Unauthorized, api.listTags("expired"))

            listOf(
                Triple(ErrorCode.INVALID_ORGANIZATION_REQUEST, 400, null),
                Triple(ErrorCode.TAG_NOT_FOUND, 404, null),
                Triple(ErrorCode.TAG_NAME_CONFLICT, 409, null),
                Triple(ErrorCode.INTERNAL_ERROR, 429, 7L),
                Triple(ErrorCode.INTERNAL_ERROR, 500, null),
            ).forEach { (code, status, retryAfter) ->
                server.enqueue(error(code, status, retryAfter))
                val failure =
                    runCatching { api.createTag("token", TagNameRequestDto("Work")) }
                        .exceptionOrNull() as KuraStorageException.Api
                assertEquals(code, failure.error.code)
                assertEquals(status, failure.error.statusCode)
                assertEquals(retryAfter, failure.error.retryAfterSeconds)
            }

            server.enqueue(MockResponse().setSocketPolicy(SocketPolicy.NO_RESPONSE))
            val pending = async { api.listTags("token") }
            server.takeRequest()
            pending.cancelAndJoin()
            assertTrue(pending.isCancelled)
        }

    private fun assertRequest(
        method: String,
        path: String,
        body: String? = null,
    ) {
        val request = server.takeRequest()
        assertEquals(method, request.method)
        assertEquals(path, request.path)
        if (body != null) assertEquals(body, request.body.readUtf8())
        assertEquals("Bearer token", request.getHeader("Authorization"))
    }

    private fun json(body: String) = MockResponse().setHeader("Content-Type", "application/json").setBody(body)

    private fun error(
        code: ErrorCode,
        status: Int,
        retryAfter: Long?,
    ) = MockResponse()
        .setResponseCode(status)
        .setHeader("Content-Type", "application/json")
        .apply { if (retryAfter != null) setHeader("Retry-After", retryAfter) }
        .setBody("{\"code\":\"$code\",\"message\":\"Failed\",\"requestId\":\"request-id\"}")

    private companion object {
        const val ENTRY = "00000000-0000-4000-8000-000000000001"
        const val OWNER = "00000000-0000-4000-8000-000000000002"
        const val TAG_ID = "00000000-0000-4000-8000-000000000003"
        const val TAG_ID_2 = "00000000-0000-4000-8000-000000000004"
        const val TAG = "{\"id\":\"$TAG_ID\",\"name\":\"Work\"}"
        val FAVORITE_PAGE =
            """
            {"items":[{"id":"$ENTRY","entryType":"FILE","name":"a.pdf","mimeType":"application/pdf",
            "fileCategory":"DOCUMENT","size":1,"status":"ACTIVE","updatedAt":"2026-08-28T00:00:00Z",
            "owner":{"id":"$OWNER","displayName":"Owner"},"permission":"OWNER","permissionSource":"OWNER",
            "shareTargetId":null,"favoritedAt":"2026-08-28T00:00:00Z"}],"page":1,"pageSize":50,"totalCount":1}
            """.trimIndent()
    }
}
